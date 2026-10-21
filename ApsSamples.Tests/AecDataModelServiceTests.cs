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
        Assert.Equal(100, first.GetProperty("pagination").GetProperty("limit").GetInt32());
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
    public async Task GetExtractionStatusAsync_Success_ParsesStatusGroupAndVersion()
    {
        var (svc, handler) = NewService((HttpStatusCode.OK, """
            {"data":{"elementGroupExtractionStatusAtTip":{
              "status":"SUCCESS","details":null,
              "elementGroup":{"id":"group-1","name":"Model.rvt","version":{"versionNumber":28}}}}}
            """));

        var status = await svc.GetExtractionStatusAsync("token", "urn:adsk.wipprod:dm.lineage:abc", "1234-guid", region: null);

        Assert.Equal("SUCCESS", status.Status);
        Assert.True(status.IsSuccess);
        Assert.Equal("group-1", status.ElementGroupId);
        Assert.Equal(28, status.VersionNumber);

        var variables = handler.Requests.Single().Variables;
        Assert.Equal("urn:adsk.wipprod:dm.lineage:abc", variables.GetProperty("fileUrn").GetString());
        Assert.Equal("b.1234-guid", variables.GetProperty("accProjectId").GetString());
    }

    [Fact]
    public async Task GetExtractionStatusAsync_ProjectIdWithPrefix_KeepsSinglePrefix()
    {
        var (svc, handler) = NewService((HttpStatusCode.OK, """
            {"data":{"elementGroupExtractionStatusAtTip":{"status":"IN_PROGRESS","details":null,"elementGroup":null}}}
            """));

        var status = await svc.GetExtractionStatusAsync("token", "urn", "b.1234-guid", region: null);

        Assert.Equal("IN_PROGRESS", status.Status);
        Assert.False(status.IsSuccess);
        Assert.Null(status.ElementGroupId);
        Assert.Null(status.VersionNumber);
        Assert.Equal("b.1234-guid", handler.Requests.Single().Variables.GetProperty("accProjectId").GetString());
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
