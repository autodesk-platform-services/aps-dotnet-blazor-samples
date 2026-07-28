using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApsSamples.Models;
using ApsSamples.Services;
using Autodesk.DataManagement;
using Autodesk.DataManagement.Model;
using Autodesk.Webhooks;
using Autodesk.Webhooks.Model;

namespace ApsSamples.Tools
{
    public class BimManagerAssistantTools(
        DataManagementClient dataManagementClient,
        WebhooksClient webhooksClient,
        IUserSessionService session,
        IAgentTaskService taskService,
        IAPSAuthenticationService apsAuth,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        // Set by the host page (Chat.razor) for the hub/project/conversation the user is currently
        // chatting in. Tools read these instead of taking hubId/projectId/conversationId
        // parameters, so the agent can't scope a call to the wrong one and never has to spend an
        // API call resolving the hub for a project itself - it's already known.
        public string? HubId { get; set; }
        public string? ProjectId { get; set; }
        public string? ConversationId { get; set; }

        // The chat model reliably remembers a model's display name across turns but not
        // necessarily its item ID (that only ever showed up as raw tool output, never in the text
        // shown to the user, and isn't guaranteed to survive in the conversation history sent back
        // to the model on later turns). Remember every item ID we've handed out by name, keyed for
        // the lifetime of this chat session, so later calls resolve the authoritative ID themselves
        // instead of trusting whatever the model passes back.
        private readonly Dictionary<string, string> _knownModelItemIds = new(StringComparer.OrdinalIgnoreCase);

        [Description("Lists the Revit (.rvt) models found in the current project's folders, recursively. Returns each model's name and item ID (needed to publish it).")]
        public async Task<List<RevitModelInfo>> ListRevitModelsAsync()
        {
            var projectId = RequireProjectId();
            var hubId = RequireHubId();
            var accessToken = session.AccessToken ?? string.Empty;

            var models = new List<RevitModelInfo>();

            var topFolders = await dataManagementClient.GetProjectTopFoldersAsync(
                hubId: hubId,
                projectId: projectId,
                accessToken: accessToken);

            if (topFolders.Data != null)
            {
                foreach (var folder in topFolders.Data)
                {
                    await CollectRevitModelsAsync(projectId, folder.Id ?? string.Empty, accessToken, models);
                }
            }

            foreach (var model in models)
            {
                _knownModelItemIds[model.Name] = model.ItemId;
            }

            return models.OrderBy(m => m.Name).ToList();
        }

        // If this model name was seen in an earlier ListRevitModelsAsync/ListUnpublishedModelsAsync
        // call this session, use that item ID instead of whatever the model passed - it's the
        // source of truth, not the model's recollection of a value it never actually saw in text.
        private string ResolveItemId(string itemId, string modelName) =>
            _knownModelItemIds.TryGetValue(modelName, out var knownItemId) ? knownItemId : itemId;

        [Description("Publishes a Revit cloud-worksharing model in the current project so the latest cloud model becomes available as a new version. Automatically tracks the publish as a task (visible in the task panel) and notifies you here once it completes or fails - publishing happens asynchronously and can take a while. Call ListRevitModelsAsync first to get the model's item ID and name.")]
        public async Task<string> PublishRevitModelAsync(
            [Description("Item ID of the Revit model to publish, as returned by ListRevitModelsAsync")] string itemId,
            [Description("Display name of the model, used for the task and the completion notification")] string modelName)
        {
            var projectId = RequireProjectId();
            var hubId = RequireHubId();
            var conversationId = RequireConversationId();
            var accessToken = session.AccessToken ?? string.Empty;
            itemId = ResolveItemId(itemId, modelName);

            var task = await taskService.CreateTaskAsync(conversationId, projectId, $"Publish: {modelName}");
            task.Status = AgentTaskStatus.Running;
            task.ProgressDetail = "Submitting publish command.";
            await taskService.UpdateTaskAsync(task);

            var publishPayload = new PublishModelPayload
            {
                Type = TypeCommands.Commands,
                Attributes = new PublishModelPayloadAttributes
                {
                    Extension = new PublishModelPayloadAttributesExtension
                    {
                        Type = TypeCommandtypePublishmodel.CommandsautodeskBim360C4RModelPublish,
                        VarVersion = "1.0.0"
                    }
                },
                Relationships = new PublishModelPayloadRelationships
                {
                    Resources = new PublishModelPayloadRelationshipsResources
                    {
                        Data = new List<PublishModelPayloadRelationshipsResourcesData>
                        {
                            new PublishModelPayloadRelationshipsResourcesData
                            {
                                Type = TypeItem.Items,
                                Id = itemId
                            }
                        }
                    }
                }
            };

            PublishModel? result;
            try
            {
                result = await dataManagementClient.ExecutePublishModelAsync(
                    projectId: projectId,
                    publishModelPayload: publishPayload,
                    accessToken: accessToken);
            }
            catch (Exception ex)
            {
                await FailTaskAsync(task, $"Publish command failed: {ex.Message}");
                return $"Publish command for '{modelName}' failed: {ex.Message}";
            }

            if (result == null)
            {
                await FailTaskAsync(task, "Publish command returned no result.");
                return $"Publish command for '{modelName}' returned no result.";
            }

            task.ProgressDetail = "Publish command submitted, waiting for it to complete.";
            await taskService.UpdateTaskAsync(task);

            var subscribed = await TrySubscribeToPublishCompletionAsync(task, hubId, projectId, itemId, modelName, accessToken);

            return subscribed
                ? $"Publishing '{modelName}'. I'll let you know here once it completes."
                : $"Publishing '{modelName}' (command id: {result.Id}). I couldn't set up a completion notification for it, " +
                    "so the task will show as failed if it doesn't complete in time.";
        }

        [Description("Checks whether a Revit model has ever been published successfully, or has a publish currently in progress. Call ListRevitModelsAsync first to get the item ID.")]
        public async Task<string> CheckModelPublishEligibilityAsync(
            [Description("Item ID of the Revit model to check, as returned by ListRevitModelsAsync")] string itemId,
            [Description("Display name of the model, used in the response")] string modelName)
        {
            var projectId = RequireProjectId();
            var accessToken = session.AccessToken ?? string.Empty;
            itemId = ResolveItemId(itemId, modelName);

            var status = await GetLastPublishJobStatusAsync(projectId, itemId, accessToken);

            return status switch
            {
                PublishJobStatus.NotFound => $"'{modelName}' has no publish job on record - it hasn't been published yet and is ready to publish.",
                PublishJobStatus.Complete => $"'{modelName}' was published successfully and can be published again if needed.",
                PublishJobStatus.Failed => $"'{modelName}' last publish failed - it can be retried.",
                _ => $"'{modelName}' currently has a publish in progress; wait for it to finish before publishing it again."
            };
        }

        [Description("Searches for and lists the Revit models in the current project that have changes and can be published again. Use this to answer questions like 'which models are unpublished")]
        public async Task<List<RevitModelInfo>> ListUnpublishedModelsAsync()
        {
            var projectId = RequireProjectId();
            var accessToken = session.AccessToken ?? string.Empty;
            var allModels = await ListRevitModelsAsync();

            var unpublished = new List<RevitModelInfo>();
            foreach (var model in allModels)
            {
                var status = await GetLastPublishJobStatusAsync(projectId, model.ItemId, accessToken);
                if (status is PublishJobStatus.NotFound or PublishJobStatus.Failed)
                {
                    unpublished.Add(model);
                }
            }

            return unpublished;
        }

        private enum PublishJobStatus
        {
            NotFound,
            InProgress,
            Complete,
            Failed
        }

        // Queries the last "publish model" command recorded for the item, using the
        // GetPublishModelJob command (https://aps.autodesk.com/en/docs/data/v2/reference/http/GetPublishModelJob/).
        // Called directly over HTTP - DataManagementClient.ExecuteGetPublishModelJobAsync throws a
        // NullReferenceException inside the generated SDK client for this particular command.
        // NotFound means no publish job was ever recorded for the item, i.e. it has never been
        // published.
        private async Task<PublishJobStatus> GetLastPublishJobStatusAsync(string projectId, string itemId, string accessToken)
        {
            var requestBody = new JsonObject
            {
                ["jsonapi"] = new JsonObject { ["version"] = "1.0" },
                ["data"] = new JsonObject
                {
                    ["type"] = "commands",
                    ["attributes"] = new JsonObject
                    {
                        ["extension"] = new JsonObject
                        {
                            ["type"] = "commands:autodesk.bim360:C4RModelGetPublishJob",
                            ["version"] = "1.0.0"
                        }
                    },
                    ["relationships"] = new JsonObject
                    {
                        ["resources"] = new JsonObject
                        {
                            ["data"] = new JsonArray(new JsonObject { ["type"] = "items", ["id"] = itemId })
                        }
                    }
                }
            };

            try
            {
                var httpClient = httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/commands")
                {
                    Content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
                };

                var response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return PublishJobStatus.NotFound;
                }

                var body = await response.Content.ReadAsStringAsync();
                var root = JsonNode.Parse(body);

                // The command-level status ("complete"/"failed"/"pending") lives at data.attributes.status;
                // the actual publish job details (if any) are nested under data.attributes.extension.data,
                // whose exact shape isn't part of the published schema, so read defensively.
                var statusText =
                    root?["data"]?["attributes"]?["extension"]?["data"]?["status"]?.GetValue<string>() ??
                    root?["data"]?["attributes"]?["status"]?.GetValue<string>();

                return ParsePublishJobStatus(statusText);
            }
            catch (Exception)
            {
                return PublishJobStatus.NotFound;
            }
        }

        private static PublishJobStatus ParsePublishJobStatus(string? statusText)
        {
            if (string.IsNullOrEmpty(statusText))
            {
                return PublishJobStatus.NotFound;
            }

            if (statusText.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                statusText.Contains("success", StringComparison.OrdinalIgnoreCase))
            {
                return PublishJobStatus.Complete;
            }

            if (statusText.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                statusText.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return PublishJobStatus.Failed;
            }

            return PublishJobStatus.InProgress;
        }

        private async Task FailTaskAsync(AgentTaskInfo task, string errorDetail)
        {
            task.Status = AgentTaskStatus.Failed;
            task.CompletedAt = DateTime.UtcNow;
            task.ErrorDetail = errorDetail;
            await taskService.UpdateTaskAsync(task);
        }

        // Subscribes the task started above to a dm.version.added webhook so it gets marked
        // Completed/Failed once the publish actually lands, instead of just reflecting that the
        // command was submitted. The webhook is temporary: reused across models published from the
        // same folder while any of them are still pending, and deleted by AgentTaskService once
        // none are (see AgentTaskService.TryDeleteHookIfUnusedAsync).
        private async Task<bool> TrySubscribeToPublishCompletionAsync(
            AgentTaskInfo task, string hubId, string projectId, string itemId, string modelName, string accessToken)
        {
            var callbackUrl = configuration["Webhooks:CallbackUrl"];
            if (string.IsNullOrEmpty(callbackUrl))
            {
                return false;
            }

            var folder = await dataManagementClient.GetItemParentFolderAsync(
                projectId: projectId,
                itemId: itemId,
                accessToken: accessToken);
            var folderId = folder?.Data?.Id;
            if (string.IsNullOrEmpty(folderId))
            {
                return false;
            }

            // Webhook management uses an app-level (2-legged) token rather than the user's, since
            // it must also work later from a background timeout or an incoming webhook callback,
            // where no user session is available.
            var webhookToken = await apsAuth.GetTwoLeggedTokenAsync();

            var hookId = await FindExistingHookIdAsync(folderId, webhookToken)
                ?? await CreateHookAsync(hubId, projectId, folderId, callbackUrl, webhookToken);

            if (string.IsNullOrEmpty(hookId))
            {
                return false;
            }

            task.WebhookHookId = hookId;
            task.WebhookTargetFileName = modelName;
            await taskService.UpdateTaskAsync(task);
            return true;
        }

        private async Task<string?> FindExistingHookIdAsync(string folderId, string webhookToken)
        {
            var existingHooks = await webhooksClient.GetSystemEventHooksAsync(
                Systems.Data,
                Events.DmVersionAdded,
                scopeName: "folder",
                accessToken: webhookToken);

            return existingHooks.Data?
                .FirstOrDefault(h => string.Equals(h.Scope?.Folder, folderId, StringComparison.OrdinalIgnoreCase))
                ?.HookId;
        }

        private async Task<string?> CreateHookAsync(string hubId, string projectId, string folderId, string callbackUrl, string webhookToken)
        {
            var hookPayload = new HookPayload
            {
                CallbackUrl = callbackUrl,
                HubId = hubId,
                ProjectId = projectId,
                Scope = new Dictionary<string, object> { ["folder"] = folderId }
            };

            var response = await webhooksClient.CreateSystemEventHookAsync(
                Systems.Data,
                Events.DmVersionAdded,
                hookPayload,
                accessToken: webhookToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<HookDetails>(responseBody,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })?.HookId;
        }

        private string RequireProjectId()
        {
            if (string.IsNullOrEmpty(ProjectId))
            {
                throw new InvalidOperationException("No active project is set for this chat session.");
            }

            return ProjectId;
        }

        private string RequireConversationId()
        {
            if (string.IsNullOrEmpty(ConversationId))
            {
                throw new InvalidOperationException("No active conversation is set for this chat session.");
            }

            return ConversationId;
        }

        private string RequireHubId()
        {
            if (string.IsNullOrEmpty(HubId))
            {
                throw new InvalidOperationException("No active hub is set for this chat session.");
            }

            return HubId;
        }

        private async Task CollectRevitModelsAsync(string projectId, string folderId, string accessToken, List<RevitModelInfo> models)
        {
            if (string.IsNullOrEmpty(folderId)) return;

            var contents = await dataManagementClient.GetFolderContentsAsync(
                projectId: projectId,
                folderId: folderId,
                accessToken: accessToken);

            if (contents.Data == null) return;

            foreach (var entry in contents.Data)
            {
                if (entry is FolderData folder)
                {
                    await CollectRevitModelsAsync(projectId, folder.Id ?? string.Empty, accessToken, models);
                }
                else if (entry is ItemData item)
                {
                    var displayName = item.Attributes?.DisplayName ?? string.Empty;
                    if (displayName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) &&
                        !displayName.Contains("template", StringComparison.OrdinalIgnoreCase))
                    {
                        models.Add(new RevitModelInfo
                        {
                            Name = displayName,
                            ItemId = item.Id ?? string.Empty
                        });
                    }
                }
            }
        }
    }

    public class RevitModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
    }
}
