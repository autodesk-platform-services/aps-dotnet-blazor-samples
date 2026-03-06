using ApsSamples.Services;
using System.ComponentModel;
using System.Text.Json.Serialization;

/// <summary>
/// Tools for Autodesk Automation API for Revit operations.
/// </summary>
internal class RevitAutomationTools(IRevitAutomationService revitAutomationService)
{
    [Description("Submits a workitem to add or remove Revit model links in a cloud model.")]
    public async Task<WorkItemSubmissionResult> LinkModification(
        [Description("Region of the Autodesk cloud environment (e.g., 'US', 'EMEA')")] string region,
        [Description("Project GUID")] string projectGuid,
        [Description("Model GUID")] string modelGuid,
        [Description("List of links to add to the model")] List<LinkModification> linksToAdd,
        [Description("List of links to remove from the model")] List<LinkModification> linksToRemove,
        [Description("Save the model after modification (default: true)")] bool save = true)
    {
        var modelConfig = ModelConfiguration.Create()
            .WithRegion(region)
            .WithProject(projectGuid)
            .WithModelGuid(modelGuid)
            .WithToolName("link_models")
            .Save(save)
            .Configure();

        var toolInputs = new Dictionary<string, object>
        {
            { "region", region },
            { "projectGuid", projectGuid },
            { "linksToAdd", linksToAdd },
            { "linksToRemove", linksToRemove }
        };

        return await revitAutomationService.SubmitWorkItemAsync(modelConfig, toolInputs);
    }

    [Description("Submits a workitem to create a single Revit model in the cloud from a template.")]
    public async Task<WorkItemSubmissionResult> CreateModel(
        [Description("Region of the Autodesk cloud environment (e.g., 'US', 'EMEA')")] string region,
        [Description("Project GUID")] string projectGuid,
        [Description("Account ID (GUID)")] Guid accountId,
        [Description("Project ID (GUID)")] Guid projectId,
        [Description("Folder ID where the model will be created")] string folderId,
        [Description("Name of the template to use")] string templateName,
        [Description("Template model GUID")] string templateModelGuid,
        [Description("Enable worksharing for the new model")] bool enableWorksharing,
        [Description("Name for the new model")] string modelName,
        [Description("Save the model after creation (default: true)")] bool save = true)
    {
        var modelConfig = ModelConfiguration.Create()
            .WithRegion(region)
            .WithProject(projectGuid)
            .WithModelGuid(templateModelGuid)
            .WithToolName("create_model")
            .Save(save)
            .Configure();

        var toolInputs = new Dictionary<string, object>
        {
            { "accountId", accountId },
            { "projectId", projectId },
            { "folderId", folderId },
            { "enableWorksharing", enableWorksharing },
            { "modelName", modelName }
        };

        return await revitAutomationService.SubmitWorkItemAsync(modelConfig, toolInputs);
    }

    [Description("Submits a workitem to create sheets in a Revit model from a list of sheet definitions and an optional title block template.")]
    public async Task<WorkItemSubmissionResult> CreateSheets(
        [Description("Region of the Autodesk cloud environment (e.g., 'US', 'EMEA')")] string region,
        [Description("Project GUID")] string projectGuid,
        [Description("Model GUID")] string modelGuid,
        [Description("List of sheet definitions to create")] List<SheetDefinition> sheets,
        [Description("Name of the title block to use (optional, omit to use the default)")] string? titleBlockName = null,
        [Description("Save the model after sheet creation (default: true)")] bool save = true)
    {
        var modelConfig = ModelConfiguration.Create()
            .WithRegion(region)
            .WithProject(projectGuid)
            .WithModelGuid(modelGuid)
            .WithToolName("create_sheets")
            .Save(save)
            .Configure();

        var toolInputs = new Dictionary<string, object>
        {
            { "sheets", sheets }
        };

        if (!string.IsNullOrEmpty(titleBlockName))
            toolInputs["titleBlockName"] = titleBlockName;

        return await revitAutomationService.SubmitWorkItemAsync(modelConfig, toolInputs);
    }
}

class SheetDefinition
{
    [JsonPropertyName("sheetNumber")]
    public string SheetNumber { get; set; } = string.Empty;

    [JsonPropertyName("sheetName")]
    public string SheetName { get; set; } = string.Empty;
}

class LinkModification
{
    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = string.Empty;

    [JsonPropertyName("modelGuid")]
    public string? ModelGuid { get; set; }
}
