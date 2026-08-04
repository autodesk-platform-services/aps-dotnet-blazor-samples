using ApsSamples.Models;
using ApsSamples.Tools;
using Microsoft.Extensions.AI;

namespace ApsSamples.Services;

public class ToolCatalogService : IToolCatalogService
{
    private static readonly IReadOnlyList<ToolDescriptor> _tools = new List<ToolDescriptor>
    {
        new ToolDescriptor
        {
            ToolId = "list-revit-models",
            DisplayName = "List Revit Models",
            Description = "Lists the Revit (.rvt) models found in the current project's folders, recursively. Returns each model's name and item ID. Always show both the name and the item ID together to the user (e.g. \"Model: Architecture.rvt - Id: <itemId>\"), so the ID stays visible in the conversation for later turns (e.g. publishing one of the listed models)."
        },
        new ToolDescriptor
        {
            ToolId = "publish-revit-model",
            DisplayName = "Publish Revit Model",
            Description = "Publishes (syncs) a Revit cloud-worksharing model in the current project so the latest cloud model becomes available as a new version. Automatically tracks the publish as a task (visible in the task panel) and notifies you here once it completes or fails - publishing happens asynchronously and can take a while. Call ListRevitModelsAsync first to get the model's item ID and name."
        },
        new ToolDescriptor
        {
            ToolId = "check-model-publish-eligibility",
            DisplayName = "Check Model Publish Eligibility",
            Description = "Checks whether a Revit model has ever been published successfully, or has a publish currently in progress. Call ListRevitModelsAsync first to get the item ID."
        },
        new ToolDescriptor
        {
            ToolId = "list-unpublished-models",
            DisplayName = "List Unpublished Models",
            Description = "Searches for and lists the Revit models in the current project that have changes and can be published again. Use this to answer questions like 'which models are unpublished"
        },
        new ToolDescriptor
        {
            ToolId = "link-modification",
            DisplayName = "Link Modification",
            Description = "Submits a workitem to add or remove Revit model links in a cloud model."
        },
        new ToolDescriptor
        {
            ToolId = "create-model",
            DisplayName = "Create Model",
            Description = "Submits a workitem to create a single Revit model in the cloud from a template."
        },
        new ToolDescriptor
        {
            ToolId = "create-sheets",
            DisplayName = "Create Sheets",
            Description = "Submits a workitem to create sheets in a Revit model from a list of sheet definitions and an optional title block template."
        }
    };

    public IReadOnlyList<ToolDescriptor> GetAllTools() => _tools;

    public IReadOnlyList<AIFunction> GetAIFunctions(IEnumerable<string> toolIds, BimManagerAssistantTools tools)
    {
        var enabled = new HashSet<string>(toolIds, StringComparer.OrdinalIgnoreCase);
        var functions = new List<AIFunction>();

        if (enabled.Contains("list-revit-models"))
        {
            functions.Add(AIFunctionFactory.Create(tools.ListRevitModelsAsync));
        }

        if (enabled.Contains("publish-revit-model"))
        {
            functions.Add(AIFunctionFactory.Create(tools.PublishRevitModelAsync));
        }

        if (enabled.Contains("check-model-publish-eligibility"))
        {
            functions.Add(AIFunctionFactory.Create(tools.CheckModelPublishEligibilityAsync));
        }

        if (enabled.Contains("list-unpublished-models"))
        {
            functions.Add(AIFunctionFactory.Create(tools.ListUnpublishedModelsAsync));
        }

        return functions;
    }
}
