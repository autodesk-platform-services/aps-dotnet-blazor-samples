using System.Net;
using System.Text.Json;
using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

public class RevitWebhookDispatcherTests
{
    private static (RevitWebhookDispatcher Dispatcher, StubHandler Handler) NewDispatcher(params HttpStatusCode[] statuses)
    {
        var handler = new StubHandler(statuses.Length == 0 ? [HttpStatusCode.OK] : statuses);
        var dispatcher = new RevitWebhookDispatcher(new StubHttpClientFactory(handler), NullLogger<RevitWebhookDispatcher>.Instance)
        {
            RetryDelay = TimeSpan.Zero
        };
        return (dispatcher, handler);
    }

    private static RevitWebhookSubscription NewSubscription(params OutgoingWebhookTarget[] targets) => new()
    {
        Id = "sub-1",
        ModelName = "Architecture.rvt",
        LineageUrn = "urn:adsk.wipprod:dm.lineage:abc",
        Targets = targets.ToList()
    };

    private static OutgoingWebhookTarget NewTarget(string url, string? secret = null, params string[] eventTypes) => new()
    {
        Url = url,
        Secret = secret,
        EventTypes = eventTypes.Length == 0 ? [RevitWebhookEventTypes.LinkAdded, RevitWebhookEventTypes.LinkRemoved] : eventTypes.ToList()
    };

    private static readonly RevitLinkChange AddedChange = new()
    {
        EventType = RevitWebhookEventTypes.LinkAdded,
        FileName = "MEP.rvt",
        InstanceCount = 1,
        TypeChanged = true,
        ElementIds = ["type-id", "instance-id"]
    };

    [Fact]
    public async Task DispatchAsync_SendsExpectedEnvelope()
    {
        var (dispatcher, handler) = NewDispatcher();
        var subscription = NewSubscription(NewTarget("https://example.com/hook"));

        var deliveries = await dispatcher.DispatchAsync(subscription, AddedChange, 27, 28, "model.publish");

        var delivery = Assert.Single(deliveries);
        Assert.True(delivery.Success);
        Assert.Equal(200, delivery.StatusCode);
        Assert.Equal("https://example.com/hook", delivery.TargetUrl);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.com/hook", request.Uri);
        Assert.Equal("application/json", request.ContentType);

        using var doc = JsonDocument.Parse(request.Body);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("version").GetString());
        Assert.Equal("revit.link.added", root.GetProperty("event").GetString());
        Assert.Equal("sub-1", root.GetProperty("subscriptionId").GetString());
        Assert.Equal("urn:adsk.wipprod:dm.lineage:abc", root.GetProperty("resourceUrn").GetString());
        Assert.True(root.GetProperty("deliveredAt").TryGetDateTime(out _));

        var payload = root.GetProperty("payload");
        Assert.Equal("Architecture.rvt", payload.GetProperty("modelName").GetString());
        Assert.Equal("MEP.rvt", payload.GetProperty("fileName").GetString());
        Assert.Equal("model.publish", payload.GetProperty("source").GetString());
        Assert.Equal(27, payload.GetProperty("fromVersion").GetInt32());
        Assert.Equal(28, payload.GetProperty("toVersion").GetInt32());
        Assert.Equal(1, payload.GetProperty("instanceCount").GetInt32());
        Assert.True(payload.GetProperty("typeChanged").GetBoolean());
        Assert.Equal(["type-id", "instance-id"], payload.GetProperty("elementIds").EnumerateArray().Select(e => e.GetString()).ToList());
    }

    [Fact]
    public async Task DispatchAsync_TargetWithSecret_AddsVerifiableSignature()
    {
        var (dispatcher, handler) = NewDispatcher();
        var subscription = NewSubscription(NewTarget("https://example.com/hook", secret: "s3cret"));

        await dispatcher.DispatchAsync(subscription, AddedChange, 1, 2, "model.publish");

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"sha256={RevitWebhookDispatcher.ComputeSignature("s3cret", request.Body)}", request.Signature);
    }

    [Fact]
    public async Task DispatchAsync_TargetWithoutSecret_SendsNoSignature()
    {
        var (dispatcher, handler) = NewDispatcher();
        var subscription = NewSubscription(NewTarget("https://example.com/hook"));

        await dispatcher.DispatchAsync(subscription, AddedChange, 1, 2, "model.publish");

        Assert.Null(Assert.Single(handler.Requests).Signature);
    }

    [Fact]
    public void ComputeSignature_MatchesKnownHmacSha256()
    {
        // HMAC-SHA256("key", "The quick brown fox jumps over the lazy dog")
        Assert.Equal(
            "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
            RevitWebhookDispatcher.ComputeSignature("key", "The quick brown fox jumps over the lazy dog"));
    }

    [Fact]
    public async Task DispatchAsync_ServerErrorThenSuccess_RetriesOnce()
    {
        var (dispatcher, handler) = NewDispatcher(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var subscription = NewSubscription(NewTarget("https://example.com/hook"));

        var deliveries = await dispatcher.DispatchAsync(subscription, AddedChange, 1, 2, "model.publish");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        var delivery = Assert.Single(deliveries);
        Assert.True(delivery.Success);
        Assert.Equal(200, delivery.StatusCode);
    }

    [Fact]
    public async Task DispatchAsync_ServerErrorTwice_ReportsFailureAfterOneRetry()
    {
        var (dispatcher, handler) = NewDispatcher(HttpStatusCode.InternalServerError);
        var subscription = NewSubscription(NewTarget("https://example.com/hook"));

        var deliveries = await dispatcher.DispatchAsync(subscription, AddedChange, 1, 2, "model.publish");

        Assert.Equal(2, handler.Requests.Count);
        var delivery = Assert.Single(deliveries);
        Assert.False(delivery.Success);
        Assert.Equal(500, delivery.StatusCode);
        Assert.Contains("500", delivery.Error);
    }

    [Fact]
    public async Task DispatchAsync_ConnectionFailure_RecordsErrorWithoutThrowing()
    {
        var handler = new ThrowingHandler();
        var dispatcher = new RevitWebhookDispatcher(new StubHttpClientFactory(handler), NullLogger<RevitWebhookDispatcher>.Instance)
        {
            RetryDelay = TimeSpan.Zero
        };
        var subscription = NewSubscription(NewTarget("https://unreachable.example.com/hook"));

        var deliveries = await dispatcher.DispatchAsync(subscription, AddedChange, 1, 2, "model.publish");

        Assert.Equal(2, handler.Calls);
        var delivery = Assert.Single(deliveries);
        Assert.False(delivery.Success);
        Assert.Null(delivery.StatusCode);
        Assert.Contains("Connection refused", delivery.Error);
    }

    [Fact]
    public async Task DispatchAsync_SkipsTargetsNotSubscribedToEventType()
    {
        var (dispatcher, handler) = NewDispatcher();
        var subscription = NewSubscription(
            NewTarget("https://example.com/removed-only", null, RevitWebhookEventTypes.LinkRemoved),
            NewTarget("https://example.com/added", null, RevitWebhookEventTypes.LinkAdded));

        var deliveries = await dispatcher.DispatchAsync(subscription, AddedChange, 1, 2, "model.publish");

        Assert.Equal("https://example.com/added", Assert.Single(deliveries).TargetUrl);
        Assert.Equal("https://example.com/added", Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task DispatchAsync_NoMatchingTargets_SendsNothing()
    {
        var (dispatcher, handler) = NewDispatcher();
        var subscription = NewSubscription(NewTarget("https://example.com/hook", null, RevitWebhookEventTypes.LinkRemoved));

        var deliveries = await dispatcher.DispatchAsync(subscription, AddedChange, 1, 2, "model.publish");

        Assert.Empty(deliveries);
        Assert.Empty(handler.Requests);
    }

    private sealed record CapturedRequest(HttpMethod Method, string? Uri, string? ContentType, string? Signature, string Body);

    private sealed class StubHandler(HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int _next;

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.TryGetValues(RevitWebhookDispatcher.SignatureHeader, out var signature) ? signature.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(statuses[Math.Min(_next++, statuses.Length - 1)]);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException("Connection refused");
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
