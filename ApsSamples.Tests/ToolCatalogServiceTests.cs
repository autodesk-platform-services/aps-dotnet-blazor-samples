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

    private static IServiceProvider NewServices() =>
        new StubServiceProvider(
            new RevitModelTools(null!, null!, new AgentToolContext(null!, null!), null!, null!, null!, null!, null!, null!));

    private static readonly ToolCatalogService Catalog = new();

    public static TheoryData<string, string> MappedTools => new()
    {
        { "list-revit-models", "ListRevitModels" },
        { "publish-revit-model", "PublishRevitModel" },
        { "check-model-publish-eligibility", "CheckModelPublishEligibility" },
        { "list-unpublished-models", "ListUnpublishedModels" },
        { "get-linked-revit-files", "GetLinkedRevitFiles" },
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
