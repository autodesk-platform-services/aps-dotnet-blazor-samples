using System.ComponentModel;
using System.Text.Json;
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
        IConfiguration configuration)
    {
        // Set by the host page (Chat.razor) for the hub/project/conversation the user is currently
        // chatting in. Tools read these instead of taking hubId/projectId/conversationId
        // parameters, so the agent can't scope a call to the wrong one and never has to spend an
        // API call resolving the hub for a project itself - it's already known.
        public string? HubId { get; set; }
        public string? ProjectId { get; set; }
        public string? ConversationId { get; set; }

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

            return models.OrderBy(m => m.Name).ToList();
        }

        [Description("Publishes (syncs) a Revit cloud-worksharing model in the current project so the latest cloud model becomes available as a new version. Automatically tracks the publish as a task (visible in the task panel) and notifies you here once it completes or fails - publishing happens asynchronously and can take a while. Call ListRevitModelsAsync first to get the model's item ID and name.")]
        public async Task<string> PublishRevitModelAsync(
            [Description("Item ID of the Revit model to publish, as returned by ListRevitModelsAsync")] string itemId,
            [Description("Display name of the model, used for the task and the completion notification")] string modelName)
        {
            var projectId = RequireProjectId();
            var hubId = RequireHubId();
            var conversationId = RequireConversationId();
            var accessToken = session.AccessToken ?? string.Empty;

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
