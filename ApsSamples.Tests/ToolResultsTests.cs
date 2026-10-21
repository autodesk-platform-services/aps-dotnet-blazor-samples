using System.Net;
using ApsSamples.Services;
using ApsSamples.Tools;
using Xunit;

namespace ApsSamples.Tests;

public class ToolResultsTests
{
    [Fact]
    public void Json_UsesCamelCaseAndSkipsNulls()
    {
        var json = ToolResults.Json(new { ItemId = "1", ModelName = (string?)null });

        Assert.Equal("{\"itemId\":\"1\"}", json);
    }

    [Fact]
    public void Error_UsesExceptionMessage()
    {
        var result = ToolResults.Error("create the folder", new InvalidOperationException("boom"));

        Assert.Equal("Could not create the folder: boom", result);
    }

    [Fact]
    public void Error_UsesIssuesApiResponseBody()
    {
        var ex = new IssuesApiException(HttpStatusCode.Forbidden, "{\"detail\":\"no access\"}");

        Assert.Equal("Could not update the issue: {\"detail\":\"no access\"}", ToolResults.Error("update the issue", ex));
        Assert.Equal(HttpStatusCode.Forbidden, ToolResults.StatusCodeOf(ex));
    }

    [Fact]
    public void StatusCodeOf_ReturnsNullForNonHttpErrors()
    {
        Assert.Null(ToolResults.StatusCodeOf(new InvalidOperationException()));
    }
}
