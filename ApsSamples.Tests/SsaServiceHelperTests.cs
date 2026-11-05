using System.Net;
using ApsSamples.Services;
using Xunit;

namespace ApsSamples.Tests;

public class SsaServiceHelperTests
{
    [Fact]
    public void ResolveProjectUserId_NonBlankProjectUserId_ReturnsProjectUserId()
    {
        Assert.Equal("project-user", SsaService.ResolveProjectUserId("project-user", "ssa-id"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveProjectUserId_BlankProjectUserId_ReturnsSsaId(string? projectUserId)
    {
        Assert.Equal("ssa-id", SsaService.ResolveProjectUserId(projectUserId, "ssa-id"));
    }

    [Fact]
    public void ResolveProductKeys_EmptyList_ReturnsDocs()
    {
        Assert.Equal(["docs"], SsaService.ResolveProductKeys([]));
    }

    [Fact]
    public void ResolveProductKeys_Null_ReturnsDocs()
    {
        Assert.Equal(["docs"], SsaService.ResolveProductKeys(null));
    }

    [Fact]
    public void ResolveProductKeys_OnlyBlankEntries_ReturnsDocs()
    {
        Assert.Equal(["docs"], SsaService.ResolveProductKeys(["", "   ", null!]));
    }

    [Fact]
    public void ResolveProductKeys_NonEmptyList_ReturnsKeysInOrder()
    {
        Assert.Equal(["build", "cost"], SsaService.ResolveProductKeys(["build", "cost"]));
    }

    [Fact]
    public void ResolveProductKeys_PaddedEntries_ReturnsTrimmedKeys()
    {
        Assert.Equal(["build", "cost"], SsaService.ResolveProductKeys([" build ", "", "cost  "]));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void IsNotFound_NotFoundOrGoneStatus_ReturnsTrue(HttpStatusCode status)
    {
        Assert.True(SsaService.IsNotFound(new HttpRequestException(null, null, status)));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void IsNotFound_OtherStatus_ReturnsFalse(HttpStatusCode status)
    {
        Assert.False(SsaService.IsNotFound(new HttpRequestException(null, null, status)));
    }

    [Fact]
    public void IsNotFound_NonHttpException_ReturnsFalse()
    {
        Assert.False(SsaService.IsNotFound(new InvalidOperationException("boom")));
    }

    [Fact]
    public void IsNotFound_NoStatusCode_ReturnsFalse()
    {
        Assert.False(SsaService.IsNotFound(new HttpRequestException("no status")));
    }

    [Fact]
    public void IsNotFound_WrappedInnerNotFound_ReturnsTrue()
    {
        var inner = new HttpRequestException(null, null, HttpStatusCode.NotFound);

        Assert.True(SsaService.IsNotFound(new InvalidOperationException("wrapped", inner)));
    }

    [Fact]
    public void IsForbidden_ForbiddenStatus_ReturnsTrue()
    {
        Assert.True(SsaService.IsForbidden(new HttpRequestException(null, null, HttpStatusCode.Forbidden)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public void IsForbidden_OtherStatus_ReturnsFalse(HttpStatusCode status)
    {
        Assert.False(SsaService.IsForbidden(new HttpRequestException(null, null, status)));
    }

    [Fact]
    public void IsForbidden_WrappedInnerForbidden_ReturnsTrue()
    {
        var inner = new HttpRequestException(null, null, HttpStatusCode.Forbidden);

        Assert.True(SsaService.IsForbidden(new InvalidOperationException("wrapped", inner)));
    }

    [Fact]
    public void SelectAppsWithCapacity_AllFull_ReturnsEmpty()
    {
        Assert.Empty(SsaService.SelectAppsWithCapacity([("app-a", 10), ("app-b", 12)], 10));
    }

    [Fact]
    public void SelectAppsWithCapacity_OneWithSpace_ReturnsThatApp()
    {
        Assert.Equal(["app-b"], SsaService.SelectAppsWithCapacity([("app-a", 10), ("app-b", 9)], 10));
    }

    [Fact]
    public void SelectAppsWithCapacity_SeveralWithSpace_PreservesInputOrder()
    {
        Assert.Equal(
            ["app-c", "app-a"],
            SsaService.SelectAppsWithCapacity([("app-c", 3), ("app-b", 10), ("app-a", 0)], 10));
    }

    [Fact]
    public void SelectAppsWithCapacity_NoApps_ReturnsEmpty()
    {
        Assert.Empty(SsaService.SelectAppsWithCapacity([], 10));
    }
}
