using System.Net;
using System.Text;
using System.Text.Json;
using ApsSamples.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

public class AecDataModelServiceTests
{
    private static (AecDataModelService Service, StubHandler Handler) NewService(params (HttpStatusCode Status, string Body)[] responses)
    {
        var handler = new StubHandler(responses);
        return (new AecDataModelService(new StubHttpClientFactory(handler), NullLogger<AecDataModelService>.Instance), handler);
    }

    [Fact]
    public async Task GetDiffAsync_TwoPages_MergesResultsAndFollowsCursor()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """
                {"data":{"diffElementGroupByVersionWithLatest":{
                  "pagination":{"pageSize":2,"cursor":"page-2"},
                  "result":[{"type":"ADDITION","element":{"id":"a"}},{"type":"REMOVAL","element":{"id":"b"}}]}}}
                """),
            (HttpStatusCode.OK, """
                {"data":{"diffElementGroupByVersionWithLatest":{
                  "pagination":{"pageSize":1,"cursor":null},
                  "result":[{"type":"ADDITION","element":{"id":"c"}}]}}}
                """));

        var result = await svc.GetDiffAsync("token", "group-1", 27, region: null);

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        var ids = result.EnumerateArray().Select(r => r.GetProperty("element").GetProperty("id").GetString()).ToList();
        Assert.Equal(["a", "b", "c"], ids);

        Assert.Equal(2, handler.Requests.Count);
        var first = handler.Requests[0].Variables;
        Assert.Equal("group-1", first.GetProperty("elementGroupId").GetString());
        Assert.Equal(27, first.GetProperty("startVersion").GetInt32());
        Assert.Equal(["ADDITION", "REMOVAL"], first.GetProperty("changeFilter").EnumerateArray().Select(e => e.GetString()).ToList());
        Assert.Equal(50, first.GetProperty("pagination").GetProperty("limit").GetInt32());
        Assert.False(first.GetProperty("pagination").TryGetProperty("cursor", out _));
        Assert.Equal("page-2", handler.Requests[1].Variables.GetProperty("pagination").GetProperty("cursor").GetString());
        Assert.Contains("diffElementGroupByVersionWithLatest", handler.Requests[0].Query);
    }

    [Fact]
    public async Task GetDiffAsync_SendsBearerTokenAndNoRegionHeaderForUs()
    {
        var (svc, handler) = NewService((HttpStatusCode.OK, EmptyDiff));

        await svc.GetDiffAsync("token-123", "group-1", 1, region: "US");

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(AecDataModelService.GraphQlEndpoint, request.Uri);
        Assert.Equal("Bearer token-123", request.Authorization);
        Assert.Null(request.Region);
    }

    [Fact]
    public async Task GetDiffAsync_NonUsRegion_SendsRegionHeader()
    {
        var (svc, handler) = NewService((HttpStatusCode.OK, EmptyDiff));

        await svc.GetDiffAsync("token", "group-1", 1, region: "EMEA");

        Assert.Equal("EMEA", handler.Requests.Single().Region);
    }

    [Fact]
    public async Task GetDiffAsync_ErrorsArray_ThrowsWithMessage()
    {
        var (svc, _) = NewService((HttpStatusCode.OK, """
            {"data":null,"errors":[{"message":"Element group not found"}]}
            """));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetDiffAsync("token", "group-1", 1, region: null));

        Assert.Contains("Element group not found", ex.Message);
    }

    [Fact]
    public async Task GetDiffAsync_NonSuccessStatus_ThrowsWithBody()
    {
        var (svc, _) = NewService((HttpStatusCode.Unauthorized, """{"detail":"token expired"}"""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetDiffAsync("token", "group-1", 1, region: null));

        Assert.Contains("401", ex.Message);
        Assert.Contains("token expired", ex.Message);
    }

    [Fact]
    public async Task FindProjectAsync_MatchesDmProjectIdAcrossHubs_IgnoringBPrefix()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """
                {"data":{"hubs":{"pagination":{"cursor":null},"results":[
                  {"name":"Hub 1","id":"urn:adsk.ace:prod.scope:hub-1"},
                  {"name":"Hub 2","id":"urn:adsk.ace:prod.scope:hub-2"}]}}}
                """),
            (HttpStatusCode.OK, """
                {"data":{"projects":{"pagination":{"cursor":null},"results":[
                  {"id":"urn:adsk.workspace:prod.project:other","name":"Other","alternativeIdentifiers":{"dataManagementAPIProjectId":"b.other-guid"}}]}}}
                """),
            (HttpStatusCode.OK, """
                {"data":{"projects":{"pagination":{"cursor":null},"results":[
                  {"id":"urn:adsk.workspace:prod.project:wanted","name":"Wanted","alternativeIdentifiers":{"dataManagementAPIProjectId":"b.1234-guid"}}]}}}
                """));

        var project = await svc.FindProjectAsync("token", "1234-guid", region: "EMEA");

        Assert.NotNull(project);
        Assert.Equal("urn:adsk.workspace:prod.project:wanted", project.Id);
        Assert.Equal("Wanted", project.Name);
        Assert.Equal("urn:adsk.ace:prod.scope:hub-2", project.HubId);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("hubs", handler.Requests[0].Query);
        Assert.Equal(50, handler.Requests[0].Variables.GetProperty("pagination").GetProperty("limit").GetInt32());
        Assert.Equal("urn:adsk.ace:prod.scope:hub-1", handler.Requests[1].Variables.GetProperty("hubId").GetString());
        Assert.Equal("urn:adsk.ace:prod.scope:hub-2", handler.Requests[2].Variables.GetProperty("hubId").GetString());
        Assert.All(handler.Requests, r => Assert.Equal("EMEA", r.Region));
    }

    [Fact]
    public async Task FindProjectAsync_PrefixedDmProjectId_Matches()
    {
        var (svc, _) = NewService(
            (HttpStatusCode.OK, """{"data":{"hubs":{"pagination":{"cursor":null},"results":[{"name":"Hub","id":"hub-1"}]}}}"""),
            (HttpStatusCode.OK, """
                {"data":{"projects":{"pagination":{"cursor":null},"results":[
                  {"id":"project-1","name":"Project","alternativeIdentifiers":{"dataManagementAPIProjectId":"b.1234-GUID"}}]}}}
                """));

        var project = await svc.FindProjectAsync("token", "b.1234-guid", region: null);

        Assert.Equal("project-1", project?.Id);
    }

    [Fact]
    public async Task FindProjectAsync_NotFound_ReturnsNull()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """{"data":{"hubs":{"pagination":{"cursor":null},"results":[{"name":"Hub","id":"hub-1"}]}}}"""),
            (HttpStatusCode.OK, """
                {"data":{"projects":{"pagination":{"cursor":null},"results":[
                  {"id":"project-1","name":"Project","alternativeIdentifiers":{"dataManagementAPIProjectId":"b.other-guid"}}]}}}
                """));

        var project = await svc.FindProjectAsync("token", "b.1234-guid", region: null);

        Assert.Null(project);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FindProjectAsync_ProjectsOnSecondPage_FollowsCursor()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """{"data":{"hubs":{"pagination":{"cursor":null},"results":[{"name":"Hub","id":"hub-1"}]}}}"""),
            (HttpStatusCode.OK, """
                {"data":{"projects":{"pagination":{"cursor":"page-2"},"results":[
                  {"id":"project-1","name":"Other","alternativeIdentifiers":{"dataManagementAPIProjectId":"b.other-guid"}}]}}}
                """),
            (HttpStatusCode.OK, """
                {"data":{"projects":{"pagination":{"cursor":null},"results":[
                  {"id":"project-2","name":"Wanted","alternativeIdentifiers":{"dataManagementAPIProjectId":"b.1234-guid"}}]}}}
                """));

        var project = await svc.FindProjectAsync("token", "b.1234-guid", region: null);

        Assert.Equal("project-2", project?.Id);
        Assert.Equal("page-2", handler.Requests[2].Variables.GetProperty("pagination").GetProperty("cursor").GetString());
        Assert.Equal("hub-1", handler.Requests[2].Variables.GetProperty("hubId").GetString());
    }

    [Fact]
    public async Task FindElementGroupAsync_UsesFileUrnFilterAndParsesVersion()
    {
        var (svc, handler) = NewService((HttpStatusCode.OK, """
            {"data":{"elementGroupsByProject":{"pagination":{"cursor":null},"results":[
              {"name":"Architecture.rvt","id":"group-1",
               "alternativeIdentifiers":{"fileUrn":"urn:adsk.wipprod:dm.lineage:abc","fileVersionUrn":"urn:adsk.wipprod:fs.file:vf.abc?version=27"},
               "version":{"versionNumber":28}}]}}}
            """));

        var group = await svc.FindElementGroupAsync("token", "project-1", "urn:adsk.wipprod:dm.lineage:abc", region: null);

        Assert.NotNull(group);
        Assert.Equal("group-1", group.Id);
        Assert.Equal("Architecture.rvt", group.Name);
        Assert.Equal("urn:adsk.wipprod:dm.lineage:abc", group.FileUrn);
        Assert.Equal("urn:adsk.wipprod:fs.file:vf.abc?version=27", group.FileVersionUrn);
        Assert.Equal(28, group.VersionNumber);

        var request = handler.Requests.Single();
        Assert.Contains("elementGroupsByProject", request.Query);
        Assert.Equal("project-1", request.Variables.GetProperty("projectId").GetString());
        Assert.Equal(["urn:adsk.wipprod:dm.lineage:abc"],
            request.Variables.GetProperty("filter").GetProperty("fileUrn").EnumerateArray().Select(e => e.GetString()).ToList());
    }

    [Fact]
    public async Task FindElementGroupAsync_VersionMissing_FallsBackToFileVersionUrn()
    {
        var (svc, _) = NewService((HttpStatusCode.OK, """
            {"data":{"elementGroupsByProject":{"pagination":{"cursor":null},"results":[
              {"name":"Architecture.rvt","id":"group-1",
               "alternativeIdentifiers":{"fileUrn":"urn:adsk.wipprod:dm.lineage:abc","fileVersionUrn":"urn:adsk.wipprod:fs.file:vf.abc?version=28"},
               "version":null}]}}}
            """));

        var group = await svc.FindElementGroupAsync("token", "project-1", "urn:adsk.wipprod:dm.lineage:abc", region: null);

        Assert.Equal(28, group?.VersionNumber);
    }

    [Fact]
    public async Task FindElementGroupAsync_PagesPastEmptyPage()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """{"data":{"elementGroupsByProject":{"pagination":{"cursor":"page-2"},"results":[]}}}"""),
            (HttpStatusCode.OK, """
                {"data":{"elementGroupsByProject":{"pagination":{"cursor":null},"results":[
                  {"name":"Architecture.rvt","id":"group-1","alternativeIdentifiers":{"fileUrn":"urn:adsk.wipprod:dm.lineage:abc"},"version":{"versionNumber":3}}]}}}
                """));

        var group = await svc.FindElementGroupAsync("token", "project-1", "urn:adsk.wipprod:dm.lineage:abc", region: null);

        Assert.Equal("group-1", group?.Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("page-2", handler.Requests[1].Variables.GetProperty("pagination").GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task FindElementGroupAsync_FilterRejected_FallsBackToClientSideMatch()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """{"errors":[{"message":"Invalid filter"}]}"""),
            (HttpStatusCode.OK, """
                {"data":{"elementGroupsByProject":{"pagination":{"cursor":null},"results":[
                  {"name":"Structure.rvt","id":"group-other","alternativeIdentifiers":{"fileUrn":"urn:adsk.wipprod:dm.lineage:other"},"version":{"versionNumber":9}},
                  {"name":"Architecture.rvt","id":"group-1","alternativeIdentifiers":{"fileUrn":"urn:adsk.wipprod:dm.lineage:ABC"},"version":{"versionNumber":3}}]}}}
                """));

        var group = await svc.FindElementGroupAsync("token", "project-1", "urn:adsk.wipprod:dm.lineage:abc?version=3", region: null);

        Assert.Equal("group-1", group?.Id);
        Assert.Equal(3, group?.VersionNumber);
        Assert.False(handler.Requests[1].Variables.TryGetProperty("filter", out _));
        Assert.Equal("project-1", handler.Requests[1].Variables.GetProperty("projectId").GetString());
    }

    [Fact]
    public async Task FindElementGroupAsync_NoMatch_ReturnsNull()
    {
        var (svc, _) = NewService((HttpStatusCode.OK, """
            {"data":{"elementGroupsByProject":{"pagination":{"cursor":null},"results":[
              {"name":"Structure.rvt","id":"group-other","alternativeIdentifiers":{"fileUrn":"urn:adsk.wipprod:dm.lineage:other"},"version":{"versionNumber":9}}]}}}
            """));

        var group = await svc.FindElementGroupAsync("token", "project-1", "urn:adsk.wipprod:dm.lineage:abc", region: null);

        Assert.Null(group);
    }

    [Fact]
    public async Task GetLinkElementIdsAsync_FollowsCursorAndSendsCategoryFilter()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """
                {"data":{"elementsByElementGroup":{"pagination":{"cursor":"next","pageSize":1},"results":[{"id":"link-type"}]}}}
                """),
            (HttpStatusCode.OK, """
                {"data":{"elementsByElementGroup":{"pagination":{"cursor":null,"pageSize":1},"results":[{"id":"link-instance"}]}}}
                """));

        var ids = await svc.GetLinkElementIdsAsync("token", "group-1", region: null);

        Assert.Equal(["link-type", "link-instance"], ids);
        Assert.Equal("property.name.category=='RVT Links'",
            handler.Requests[0].Variables.GetProperty("filter").GetProperty("query").GetString());
    }

    [Fact]
    public async Task GetLinkElementIdsAsync_FilterRejected_FallsBackToClientSideFiltering()
    {
        var (svc, handler) = NewService(
            (HttpStatusCode.OK, """{"errors":[{"message":"Invalid filter"}]}"""),
            (HttpStatusCode.OK, """
                {"data":{"elementsByElementGroup":{"pagination":{"cursor":null},"results":[
                  {"id":"wall","properties":{"results":[{"name":"Revit Category Type Id","value":"Walls"}]}},
                  {"id":"link","properties":{"results":[{"name":"Revit Category Type Id","value":"RVT Links"}]}},
                  {"id":"link-by-family","properties":{"results":[{"name":"Family Name","value":"Linked Revit Model"}]}}
                ]}}}
                """));

        var ids = await svc.GetLinkElementIdsAsync("token", "group-1", region: null);

        Assert.Equal(["link", "link-by-family"], ids);
        Assert.False(handler.Requests[1].Variables.TryGetProperty("filter", out _));
    }

    private const string EmptyDiff = """
        {"data":{"diffElementGroupByVersionWithLatest":{"pagination":{"cursor":null},"result":[]}}}
        """;

    private sealed record CapturedRequest(HttpMethod Method, string? Uri, string? Authorization, string? Region, string Query, JsonElement Variables);

    private sealed class StubHandler((HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("region", out var region) ? region.Single() : null,
                doc.RootElement.GetProperty("query").GetString() ?? "",
                doc.RootElement.GetProperty("variables").Clone()));

            var (status, responseBody) = responses[Math.Min(_next++, responses.Length - 1)];
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
