using ApsSamples.Models;
using ApsSamples.Tools;
using Microsoft.Extensions.AI;

namespace ApsSamples.Services;

public class ToolCatalogService : IToolCatalogService
{
    // Every tool an admin can enable for an agent, in display order. The factory resolves the
    // tool class from the caller's scope (tool classes are scoped, this catalog is a singleton)
    // and binds one of its methods. A null factory means the tool is listed but not wired yet.
    private static readonly IReadOnlyList<(ToolDescriptor Descriptor, Func<IServiceProvider, AIFunction>? Factory)> _registrations =
    [
        (new ToolDescriptor
        {
            ToolId = "list-revit-models",
            DisplayName = "List Revit Models",
            Module = ToolModules.RevitModels,
            Description = "Lists the Revit (.rvt) models found in the current project's folders, recursively. Returns each model's name and item ID. Always show both the name and the item ID together to the user (e.g. \"Model: Architecture.rvt - Id: <itemId>\"), so the ID stays visible in the conversation for later turns (e.g. publishing one of the listed models)."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<RevitModelTools>().ListRevitModelsAsync)),
        (new ToolDescriptor
        {
            ToolId = "publish-revit-model",
            DisplayName = "Publish Revit Model",
            Module = ToolModules.RevitModels,
            Description = "Publishes (syncs) a Revit cloud-worksharing model in the current project so the latest cloud model becomes available as a new version. Automatically tracks the publish as a task (visible in the task panel) and notifies you here once it completes or fails - publishing happens asynchronously and can take a while. Call ListRevitModelsAsync first to get the model's item ID and name."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<RevitModelTools>().PublishRevitModelAsync)),
        (new ToolDescriptor
        {
            ToolId = "check-model-publish-eligibility",
            DisplayName = "Check Model Publish Eligibility",
            Module = ToolModules.RevitModels,
            Description = "Checks whether a Revit model has ever been published successfully, or has a publish currently in progress. Call ListRevitModelsAsync first to get the item ID."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<RevitModelTools>().CheckModelPublishEligibilityAsync)),
        (new ToolDescriptor
        {
            ToolId = "list-unpublished-models",
            DisplayName = "List Unpublished Models",
            Module = ToolModules.RevitModels,
            Description = "Searches for and lists the Revit models in the current project that have changes and can be published again. Use this to answer questions like 'which models are unpublished"
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<RevitModelTools>().ListUnpublishedModelsAsync)),
        (new ToolDescriptor
        {
            ToolId = "get-linked-revit-files",
            DisplayName = "Get Linked Revit Files",
            Module = ToolModules.RevitModels,
            Description = "Lists the Revit models linked to a Revit model, based on the model's latest published version. Returns each linked model's name, item ID, version ID and publish status (names and IDs only, no file contents). Call ListRevitModelsAsync first to get the host model's item ID and name."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<RevitModelTools>().GetLinkedRevitFilesAsync)),
        (new ToolDescriptor
        {
            ToolId = "list-folders",
            DisplayName = "List Folders",
            Module = ToolModules.Folders,
            Description = "Lists the subfolders of a folder in the current project. Without a parent folder ID it lists the folders under \"Project Files\". Returns the parent folder's ID and name and each subfolder's ID and name. Use it to find the folder ID to pass to the other folder tools."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<FolderTools>().ListFoldersAsync)),
        (new ToolDescriptor
        {
            ToolId = "create-folder",
            DisplayName = "Create Folder",
            Module = ToolModules.Folders,
            Description = "Creates one folder in the current project, under \"Project Files\" unless a parent folder ID is given. If a folder with that name already exists under the parent, it is returned instead of creating a duplicate. Returns the folder's ID and name and whether it was created. To create several folders or nested folders at once, use CreateFolderStructure instead."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<FolderTools>().CreateFolderAsync)),
        (new ToolDescriptor
        {
            ToolId = "create-folder-structure",
            DisplayName = "Create Folder Structure",
            Module = ToolModules.Folders,
            Description = "Creates a folder structure in the current project from a list of folder paths, under \"Project Files\" unless a parent folder ID is given. Each path uses \"/\" between levels, for example \"01 WIP/Architecture\". Existing folders are reused (matched by name, ignoring case), so running it again creates nothing new. Returns, per path, the deepest folder's ID, the segments that were created and the ones that already existed, or the error for that path."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<FolderTools>().CreateFolderStructureAsync)),
        (new ToolDescriptor
        {
            ToolId = "search-hub-users",
            DisplayName = "Search Hub Users",
            Module = ToolModules.ProjectUsers,
            Description = "Searches the users of the hub (ACC account) by name and/or email, matching partial text. Returns each user's name, email, status and company. The result only covers account members: it does not mean the user is in the current project."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<ProjectUserTools>().SearchHubUsersAsync)),
        (new ToolDescriptor
        {
            ToolId = "find-project-users",
            DisplayName = "Find Project Users",
            Module = ToolModules.ProjectUsers,
            Description = "Finds users in the current project by name and/or email. Returns each user's company, roles, access levels (account admin, project admin, executive) and the products they have access to. Use it to check whether a user is in the project and with what permissions."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<ProjectUserTools>().FindProjectUsersAsync)),
        (new ToolDescriptor
        {
            ToolId = "list-project-roles-companies",
            DisplayName = "List Project Roles and Companies",
            Module = ToolModules.ProjectUsers,
            Description = "Lists the roles and companies that can be assigned to users in the current project, with their names and IDs."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<ProjectUserTools>().ListProjectRolesAndCompaniesAsync)),
        (new ToolDescriptor
        {
            ToolId = "add-project-user",
            DisplayName = "Add Project User",
            Module = ToolModules.ProjectUsers,
            Description = "Adds a user to the current project by email, with a role and optionally a company and products (Docs as a member by default). Giving the projectAdministration product makes the user a project administrator. Needs project admin access for the agent's service account."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<ProjectUserTools>().AddUserToProjectAsync)),
        (new ToolDescriptor
        {
            ToolId = "update-project-user",
            DisplayName = "Update Project User",
            Module = ToolModules.ProjectUsers,
            Description = "Updates the role and/or company of a user in the current project, found by email. A new role replaces the user's current roles. Needs project admin access for the agent's service account."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<ProjectUserTools>().UpdateProjectUserAsync)),
        (new ToolDescriptor
        {
            ToolId = "search-issues",
            DisplayName = "Search Issues",
            Module = ToolModules.Issues,
            Description = "Searches the issues of the current project by text, status and assignee. Returns each issue's ID, display ID, title, status, assignee and due date."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<IssueTools>().SearchIssuesAsync)),
        (new ToolDescriptor
        {
            ToolId = "get-issue",
            DisplayName = "Get Issue",
            Module = ToolModules.Issues,
            Description = "Gets the full details of an issue in the current project, with its comments, so the agent can review it."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<IssueTools>().GetIssueAsync)),
        (new ToolDescriptor
        {
            ToolId = "list-issue-types",
            DisplayName = "List Issue Types",
            Module = ToolModules.Issues,
            Description = "Lists the active issue types and subtypes of the current project, with their names and IDs."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<IssueTools>().ListIssueTypesAsync)),
        (new ToolDescriptor
        {
            ToolId = "create-issue",
            DisplayName = "Create Issue",
            Module = ToolModules.Issues,
            Description = "Creates an issue in the current project with a title, subtype, and optionally a description, assignee (by project user email), due date and status (open by default)."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<IssueTools>().CreateIssueAsync)),
        (new ToolDescriptor
        {
            ToolId = "update-issue",
            DisplayName = "Update Issue",
            Module = ToolModules.Issues,
            Description = "Updates the title, description, status, assignee or due date of an issue in the current project. The ACC Issues API has no delete: issues are retired by setting their status to closed, and deleting must be done in the ACC UI."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<IssueTools>().UpdateIssueAsync)),
        (new ToolDescriptor
        {
            ToolId = "add-issue-comment",
            DisplayName = "Add Issue Comment",
            Module = ToolModules.Issues,
            Description = "Adds a comment to an issue in the current project."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<IssueTools>().AddIssueCommentAsync)),
        (new ToolDescriptor
        {
            ToolId = "list-project-assistants",
            DisplayName = "List Project Assistants",
            Module = ToolModules.Delegation,
            Description = "Lists the other assistants of the current project that tasks can be delegated to. Returns each assistant's ID, name, role and the tool modules it has enabled. Use it to pick the assistant to pass to DelegateTask."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<DelegationTools>().ListProjectAssistantsAsync)),
        (new ToolDescriptor
        {
            ToolId = "delegate-task",
            DisplayName = "Delegate Task",
            Module = ToolModules.Delegation,
            Description = "Delegates a task to another assistant of the current project, which carries it out with its own tools and service account. Delegation is asynchronous: this returns a task ID at once, the task runs in the background, and the result is posted in this conversation when it completes or fails. Call ListProjectAssistants first to pick the assistant."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<DelegationTools>().DelegateTaskAsync)),
        (new ToolDescriptor
        {
            ToolId = "get-delegated-tasks",
            DisplayName = "Get Delegated Tasks",
            Module = ToolModules.Delegation,
            Description = "Lists the tasks delegated in this conversation, with each task's ID, title, assigned assistant, status, the task IDs it depends on, and its result or error."
        }, sp => AIFunctionFactory.Create(sp.GetRequiredService<DelegationTools>().GetDelegatedTasksAsync)),
        (new ToolDescriptor
        {
            ToolId = "link-modification",
            DisplayName = "Link Modification",
            Module = ToolModules.RevitAutomation,
            Description = "Submits a workitem to add or remove Revit model links in a cloud model."
        }, null),
        (new ToolDescriptor
        {
            ToolId = "create-model",
            DisplayName = "Create Model",
            Module = ToolModules.RevitAutomation,
            Description = "Submits a workitem to create a single Revit model in the cloud from a template."
        }, null),
        (new ToolDescriptor
        {
            ToolId = "create-sheets",
            DisplayName = "Create Sheets",
            Module = ToolModules.RevitAutomation,
            Description = "Submits a workitem to create sheets in a Revit model from a list of sheet definitions and an optional title block template."
        }, null)
    ];

    private static readonly IReadOnlyList<ToolDescriptor> _tools = _registrations.Select(r => r.Descriptor).ToList();

    public IReadOnlyList<ToolDescriptor> GetAllTools() => _tools;

    public IReadOnlyList<AIFunction> GetAIFunctions(IEnumerable<string> toolIds, IServiceProvider services)
    {
        var enabled = new HashSet<string>(toolIds, StringComparer.OrdinalIgnoreCase);

        return _registrations
            .Where(r => r.Factory is not null && enabled.Contains(r.Descriptor.ToolId))
            .Select(r => r.Factory!(services))
            .ToList();
    }
}
