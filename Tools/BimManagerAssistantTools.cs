using System.ComponentModel;
using System.Text.Json;
using ApsSamples.Models;
using ApsSamples.Services;
using Autodesk.Construction.AccountAdmin;
using Autodesk.DataManagement;
using Autodesk.DataManagement.Model;
using Autodesk.Webhooks;
using Autodesk.Webhooks.Model;

namespace ApsSamples.Tools
{
    public class BimManagerAssistantTools(
        DataManagementClient dataManagementClient,
        AdminClient adminClient,
        WebhooksClient webhooksClient,
        IUserSessionService session,
        IAgentConversationService conversationService,
        IAgentTaskService taskService,
        IConfiguration configuration)
    {
        [Description("Lists the Revit (.rvt) models found in a project's folders, recursively. Returns each model's name and item ID (needed to publish it).")]
        public async Task<List<RevitModelInfo>> ListRevitModelsAsync(
            [Description("Data Management project ID, including the 'b.' hub prefix (e.g. 'b.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx')")] string projectId)
        {
            var accessToken = session.AccessToken ?? string.Empty;
            var hubId = await GetHubIdAsync(projectId, accessToken);

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

        [Description("Publishes (syncs) a Revit cloud-worksharing model in the project so the latest cloud model becomes available as a new version. Call ListRevitModelsAsync first to get the model's item ID.")]
        public async Task<string> PublishRevitModelAsync(
            [Description("Data Management project ID, including the 'b.' hub prefix")] string projectId,
            [Description("Item ID of the Revit model to publish, as returned by ListRevitModelsAsync")] string itemId)
        {
            var accessToken = session.AccessToken ?? string.Empty;

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

            var result = await dataManagementClient.ExecutePublishModelAsync(
                projectId: projectId,
                publishModelPayload: publishPayload,
                accessToken: accessToken);

            return result != null
                ? $"Publish command submitted for item {itemId} (command id: {result.Id})."
                : $"Publish command for item {itemId} returned no result.";
        }

        [Description("Subscribes to be notified in this chat when a Revit model finishes publishing. Call this after PublishRevitModelAsync so the user gets told once the publish actually completes (publishing happens asynchronously and can take a while).")]
        public async Task<string> NotifyOnModelPublishAsync(
            [Description("Data Management project ID, including the 'b.' hub prefix")] string projectId,
            [Description("Item ID of the Revit model being published, as returned by ListRevitModelsAsync")] string itemId,
            [Description("Display name of the model, used in the notification message")] string modelName)
        {
            var callbackUrl = configuration["Webhooks:CallbackUrl"];
            if (string.IsNullOrEmpty(callbackUrl))
            {
                return "Cannot subscribe to publish notifications: no Webhooks:CallbackUrl is configured for this app. " +
                    "A publicly reachable HTTPS URL pointing at /api/webhooks/model-publish must be set in appsettings.json.";
            }

            var accessToken = session.AccessToken ?? string.Empty;
            var userId = session.UserEmail ?? session.UserName ?? "unknown";

            var hubId = await GetHubIdAsync(projectId, accessToken);
            var folder = await dataManagementClient.GetItemParentFolderAsync(
                projectId: projectId,
                itemId: itemId,
                accessToken: accessToken);
            var folderId = folder?.Data?.Id;
            if (string.IsNullOrEmpty(folderId))
            {
                return $"Could not resolve the parent folder for item {itemId}; cannot subscribe to publish notifications.";
            }

            var hookPayload = new HookPayload
            {
                CallbackUrl = callbackUrl,
                HubId = hubId,
                ProjectId = projectId,
                Scope = new Dictionary<string, object> { ["folder"] = folderId }
            };

            var response = await webhooksClient.CreateSystemEventHookAsync(
                Systems.AdskC4r,
                Events.ModelPublish,
                hookPayload,
                accessToken: accessToken);

            if (!response.IsSuccessStatusCode)
            {
                return $"Failed to create the publish webhook (HTTP {(int)response.StatusCode}).";
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var hookId = JsonSerializer.Deserialize<HookDetails>(responseBody,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })?.HookId;

            if (string.IsNullOrEmpty(hookId))
            {
                return "Publish webhook was created but its ID could not be read, so I won't be able to notify you when it fires.";
            }

            var conversation = await conversationService.GetOrCreateConversationAsync(projectId, userId);
            var task = await taskService.CreateTaskAsync(conversation.ConversationId, projectId, $"Publish notification: {modelName}");
            task.Status = AgentTaskStatus.Running;
            task.ProgressDetail = "Waiting for the model publish to complete.";
            task.WebhookHookId = hookId;
            await taskService.UpdateTaskAsync(task);

            return $"Subscribed to publish notifications for '{modelName}'. I'll let you know here once the publish completes.";
        }

        private async Task<string> GetHubIdAsync(string projectId, string accessToken)
        {
            // Account Admin project IDs drop the "b." hub prefix used by the Data Management API.
            var accountProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
                ? projectId[2..]
                : projectId;

            var project = await adminClient.GetProjectAsync(projectId: accountProjectId, accessToken: accessToken);
            return "b." + project?.AccountId;
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
