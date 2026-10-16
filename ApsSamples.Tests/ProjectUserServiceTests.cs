using ApsSamples.Services;
using Autodesk.Construction.AccountAdmin.Model;
using Xunit;

namespace ApsSamples.Tests;

public class ProjectUserServiceTests
{
    [Fact]
    public void BuildProductPayloads_GivesProjectAdministrationAdministratorAccess()
    {
        var product = Assert.Single(ProjectUserService.BuildProductPayloads(["projectAdministration"]));

        Assert.Equal(ProductKeys.ProjectAdministration, product.Key);
        Assert.Equal(ProductAccess.Administrator, product.Access);
    }

    [Fact]
    public void BuildProductPayloads_GivesOtherProductsMemberAccess()
    {
        var products = ProjectUserService.BuildProductPayloads(["docs", "Build", "modelCoordination"]);

        Assert.Equal([ProductKeys.Docs, ProductKeys.Build, ProductKeys.ModelCoordination], products.Select(p => p.Key));
        Assert.All(products, p => Assert.Equal(ProductAccess.Member, p.Access));
    }

    [Fact]
    public void BuildProductPayloads_AppliesGivenAccessExceptToProjectAdministration()
    {
        var products = ProjectUserService.BuildProductPayloads(["docs", "projectAdministration"], ProductAccess.Administrator);

        Assert.All(products, p => Assert.Equal(ProductAccess.Administrator, p.Access));
    }

    [Theory]
    [InlineData("notAProduct")]
    [InlineData("3")]
    [InlineData("")]
    [InlineData("andcloudWorksharing")]
    public void BuildProductPayloads_ThrowsForUnknownKey(string key)
    {
        Assert.Throws<ArgumentException>(() => ProjectUserService.BuildProductPayloads(["docs", key]));
    }

    [Fact]
    public void ProductKeyNames_AreCamelCaseAndParse()
    {
        Assert.Contains("docs", ProjectUserService.ProductKeyNames);
        Assert.Contains("projectAdministration", ProjectUserService.ProductKeyNames);
        Assert.All(ProjectUserService.ProductKeyNames, k => Assert.True(ProjectUserService.TryParseProductKey(k, out _)));
    }
}
