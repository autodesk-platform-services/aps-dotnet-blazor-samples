using System.ComponentModel;
using ApsSamples.Services;
using Autodesk.Construction.AccountAdmin;
using Autodesk.DataManagement;
using Autodesk.DataManagement.Model;

namespace ApsSamples.Tools
{
    public class BimManagerAssistantTools(
        DataManagementClient dataManagementClient,
        AdminClient adminClient,
        IUserSessionService session)
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
