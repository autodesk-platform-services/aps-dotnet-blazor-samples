using ApsSamples.Models;
using ApsSamples.Services;
using ApsSamples.Tools;
using Xunit;

namespace ApsSamples.Tests;

public class ToolCatalogServiceTests
{
    // Resolves pre-built tool instances by type, without a DI container. The tools are built
    // with null dependencies: GetAIFunctions only binds methods, it never invokes them.
    private sealed class StubServiceProvider(params object[] services) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            services.FirstOrDefault(serviceType.IsInstanceOfType);
    }

    private static IServiceProvider NewServices()
    {
        var context = new AgentToolContext(null!, null!);
        return new StubServiceProvider(
            new RevitModelTools(null!, null!, context, null!, null!, null!, null!, null!, null!),
            new FolderTools(null!, context),
            new ProjectUserTools(null!, null!, null!, context),
            new IssueTools(null!, null!, context));
    }

    private static readonly ToolCatalogService Catalog = new();

    public static TheoryData<string, string> MappedTools => new()
    {
        { "list-revit-models", "ListRevitModels" },
        { "publish-revit-model", "PublishRevitModel" },
        { "check-model-publish-eligibility", "CheckModelPublishEligibility" },
        { "list-unpublished-models", "ListUnpublishedModels" },
        { "get-linked-revit-files", "GetLinkedRevitFiles" },
        { "list-folders", "ListFolders" },
        { "create-folder", "CreateFolder" },
        { "create-folder-structure", "CreateFolderStructure" },
        { "search-hub-users", "SearchHubUsers" },
        { "find-project-users", "FindProjectUsers" },
        { "list-project-roles-companies", "ListProjectRolesAndCompanies" },
        { "add-project-user", "AddUserToProject" },
        { "update-project-user", "UpdateProjectUser" },
        { "search-issues", "SearchIssues" },
        { "get-issue", "GetIssue" },
        { "list-issue-types", "ListIssueTypes" },
        { "create-issue", "CreateIssue" },
        { "update-issue", "UpdateIssue" },
        { "add-issue-comment", "AddIssueComment" },
    };

    [Theory]
    [InlineData("list-revit-models")]
    [InlineData("publish-revit-model")]
    [InlineData("check-model-publish-eligibility")]
    [InlineData("list-unpublished-models")]
    [InlineData("get-linked-revit-files")]
    public void ExistingRevitTools_AreListedUnderRevitModels(string toolId)
    {
        var tool = Assert.Single(Catalog.GetAllTools(), t => t.ToolId == toolId);
        Assert.Equal(ToolModules.RevitModels, tool.Module);
    }

    [Theory]
    [InlineData("list-folders")]
    [InlineData("create-folder")]
    [InlineData("create-folder-structure")]
    public void FolderTools_AreListedUnderFolders(string toolId)
    {
        var tool = Assert.Single(Catalog.GetAllTools(), t => t.ToolId == toolId);
        Assert.Equal(ToolModules.Folders, tool.Module);
    }

    [Theory]
    [InlineData("search-hub-users")]
    [InlineData("find-project-users")]
    [InlineData("list-project-roles-companies")]
    [InlineData("add-project-user")]
    [InlineData("update-project-user")]
    public void ProjectUserTools_AreListedUnderProjectUsers(string toolId)
    {
        var tool = Assert.Single(Catalog.GetAllTools(), t => t.ToolId == toolId);
        Assert.Equal(ToolModules.ProjectUsers, tool.Module);
    }

    [Theory]
    [InlineData("search-issues")]
    [InlineData("get-issue")]
    [InlineData("list-issue-types")]
    [InlineData("create-issue")]
    [InlineData("update-issue")]
    [InlineData("add-issue-comment")]
    public void IssueTools_AreListedUnderIssues(string toolId)
    {
        var tool = Assert.Single(Catalog.GetAllTools(), t => t.ToolId == toolId);
        Assert.Equal(ToolModules.Issues, tool.Module);
    }

    [Fact]
    public void ToolIds_AreUnique()
    {
        var ids = Catalog.GetAllTools().Select(t => t.ToolId).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryTool_HasAModule()
    {
        Assert.All(Catalog.GetAllTools(), t => Assert.False(string.IsNullOrWhiteSpace(t.Module)));
    }

    [Theory]
    [MemberData(nameof(MappedTools))]
    public void GetAIFunctions_MapsToolIdToFunctionName(string toolId, string functionName)
    {
        var function = Assert.Single(Catalog.GetAIFunctions([toolId], NewServices()));
        Assert.Equal(functionName, function.Name);
    }

    [Fact]
    public void GetAIFunctions_IsCaseInsensitiveAndKeepsRegistrationOrder()
    {
        var functions = Catalog.GetAIFunctions(
            ["LIST-UNPUBLISHED-MODELS", "list-revit-models"], NewServices());

        Assert.Equal(["ListRevitModels", "ListUnpublishedModels"], functions.Select(f => f.Name));
    }

    [Theory]
    [InlineData("link-modification")]
    [InlineData("create-model")]
    [InlineData("create-sheets")]
    [InlineData("no-such-tool")]
    public void GetAIFunctions_SkipsUnmappedAndUnknownIds(string toolId)
    {
        Assert.Empty(Catalog.GetAIFunctions([toolId], NewServices()));
    }
}
