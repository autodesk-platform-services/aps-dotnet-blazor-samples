using ApsSamples.Services;
using Xunit;

namespace ApsSamples.Tests;

public class IssuesServiceTests
{
    [Fact]
    public void BuildSearchQuery_WithNoFilters_HasOnlyTheDefaultLimit()
    {
        Assert.Equal("limit=25", IssuesService.BuildSearchQuery(new IssueSearchCriteria()));
    }

    [Fact]
    public void BuildSearchQuery_SkipsEmptyAndWhitespaceFilters()
    {
        var query = IssuesService.BuildSearchQuery(new IssueSearchCriteria
        {
            Status = "",
            AssignedTo = "   ",
            Search = null,
        });

        Assert.Equal("limit=25", query);
    }

    [Fact]
    public void BuildSearchQuery_IncludesEveryFilterAndOffset()
    {
        var query = IssuesService.BuildSearchQuery(new IssueSearchCriteria
        {
            Status = "open",
            AssignedTo = "ABC123",
            IssueTypeId = "type-1",
            IssueSubtypeId = "subtype-1",
            Search = "leak",
            Limit = 10,
            Offset = 20,
        });

        Assert.Equal(
            "filter[status]=open&filter[assignedTo]=ABC123&filter[issueTypeId]=type-1&filter[issueSubtypeId]=subtype-1&filter[search]=leak&limit=10&offset=20",
            query);
    }

    [Fact]
    public void BuildSearchQuery_UrlEncodesValues()
    {
        var query = IssuesService.BuildSearchQuery(new IssueSearchCriteria
        {
            Status = "open,in_review",
            Search = " water & leak ",
        });

        Assert.Equal("filter[status]=open%2Cin_review&filter[search]=water%20%26%20leak&limit=25", query);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    public void BuildSearchQuery_ClampsLimit(int limit, int expected)
    {
        var query = IssuesService.BuildSearchQuery(new IssueSearchCriteria { Limit = limit });

        Assert.Equal($"limit={expected}", query);
    }

    [Theory]
    [InlineData("open", "open")]
    [InlineData("OPEN", "open")]
    [InlineData(" Closed ", "closed")]
    [InlineData("in_progress", "in_progress")]
    [InlineData("In Progress", "in_progress")]
    [InlineData("not-approved", "not_approved")]
    public void TryNormalizeStatus_AcceptsKnownStatuses(string value, string expected)
    {
        Assert.True(IssuesService.TryNormalizeStatus(value, out var status));
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryNormalizeStatus_RejectsUnknownStatuses(string? value)
    {
        Assert.False(IssuesService.TryNormalizeStatus(value, out var status));
        Assert.Equal(string.Empty, status);
    }

    [Fact]
    public void Statuses_AreAllAccepted()
    {
        Assert.Equal(9, IssuesService.Statuses.Count);
        Assert.All(IssuesService.Statuses, s => Assert.True(IssuesService.TryNormalizeStatus(s, out _)));
    }

    [Fact]
    public void BuildIssueBody_HoldsOnlySuppliedFields()
    {
        var body = IssuesService.BuildIssueBody(new IssueFields { Status = "closed" });

        var property = Assert.Single(body);
        Assert.Equal("status", property.Key);
        Assert.Equal("closed", property.Value!.GetValue<string>());
        Assert.Equal("{\"status\":\"closed\"}", body.ToJsonString());
    }

    [Fact]
    public void BuildIssueBody_WithNoFields_IsEmpty()
    {
        Assert.Equal("{}", IssuesService.BuildIssueBody(new IssueFields()).ToJsonString());
    }

    [Fact]
    public void BuildIssueBody_UsesApiFieldNamesAndNeverNulls()
    {
        var body = IssuesService.BuildIssueBody(new IssueFields
        {
            Title = "Leak",
            Description = "Water on level 2",
            Status = "open",
            IssueSubtypeId = "subtype-1",
            AssignedTo = "ABC123",
            AssignedToType = "user",
            DueDate = "2026-12-31",
        });

        Assert.Equal(
            ["title", "description", "status", "issueSubtypeId", "assignedTo", "assignedToType", "dueDate"],
            body.Select(p => p.Key));
        Assert.All(body, p => Assert.NotNull(p.Value));
    }
}
