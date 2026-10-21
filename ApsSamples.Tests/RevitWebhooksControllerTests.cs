using System.Text.Json;
using ApsSamples.Controllers;
using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class RevitWebhooksControllerTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private readonly TempWorkingDirectoryFixture _fixture;
    private readonly RevitWebhookStore _store = new(NullLogger<RevitWebhookStore>.Instance);
    private readonly RevitWebhookQueue _queue = new();
    private readonly RevitWebhooksController _controller;

    public RevitWebhooksControllerTests(TempWorkingDirectoryFixture fixture)
    {
        _fixture = fixture;
        _controller = new RevitWebhooksController(_store, _queue, NullLogger<RevitWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private async Task<RevitWebhookSubscription> AddSubscriptionAsync(
        string hookId, RevitWebhookMode mode = RevitWebhookMode.Publish, string? lineageUrn = null)
    {
        var subscription = new RevitWebhookSubscription
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = "user-1",
            ProjectId = "b.project",
            HubId = "b.hub",
            ModelName = "Architecture.rvt",
            LineageUrn = lineageUrn ?? "urn:adsk.wipprod:dm.lineage:" + Guid.NewGuid().ToString("N"),
            FolderId = "urn:adsk.wipprod:fs.folder:co.folder",
            Mode = mode,
            HookId = hookId
        };
        await _store.UpsertAsync(subscription);
        return subscription;
    }

    private static JsonElement Callback(string? hookId, string? lineageUrn = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["version"] = "1.0",
            ["hook"] = new Dictionary<string, object?> { ["hookId"] = hookId, ["event"] = "model.publish" },
            ["payload"] = new Dictionary<string, object?>
            {
                ["ModelName"] = "Architecture.rvt",
                ["lineageUrn"] = lineageUrn
            }
        };
        return JsonSerializer.SerializeToElement(body);
    }

    private List<RevitWebhookWorkItem> DrainQueue()
    {
        var items = new List<RevitWebhookWorkItem>();
        while (_queue.Reader.TryRead(out var item))
        {
            items.Add(item);
        }
        return items;
    }

    [Fact]
    public async Task ModelPublish_MatchingHookAndUrn_EnqueuesSubscription()
    {
        var hookId = Guid.NewGuid().ToString();
        var subscription = await AddSubscriptionAsync(hookId);

        var result = _controller.ModelPublish(Callback(hookId, subscription.LineageUrn.ToUpperInvariant()));

        Assert.IsType<OkResult>(result);
        var item = Assert.Single(DrainQueue());
        Assert.Equal(subscription.Id, item.SubscriptionId);
        Assert.Equal("model.publish", item.Source);
    }

    [Fact]
    public async Task ModelPublish_NoUrnInPayload_EnqueuesEverySubscriptionOnHook()
    {
        var hookId = Guid.NewGuid().ToString();
        var first = await AddSubscriptionAsync(hookId);
        var second = await AddSubscriptionAsync(hookId);

        var result = _controller.ModelPublish(Callback(hookId));

        Assert.IsType<OkResult>(result);
        var ids = DrainQueue().Select(i => i.SubscriptionId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Contains(first.Id, ids);
        Assert.Contains(second.Id, ids);
    }

    [Fact]
    public async Task ModelPublish_UnknownHookId_ReturnsOkAndEnqueuesNothing()
    {
        await AddSubscriptionAsync(Guid.NewGuid().ToString());

        var result = _controller.ModelPublish(Callback(Guid.NewGuid().ToString()));

        Assert.IsType<OkResult>(result);
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task ModelPublish_UrnMismatch_EnqueuesNothing()
    {
        var hookId = Guid.NewGuid().ToString();
        await AddSubscriptionAsync(hookId);

        var result = _controller.ModelPublish(Callback(hookId, "urn:adsk.wipprod:dm.lineage:someOtherModel"));

        Assert.IsType<OkResult>(result);
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task ModelSync_PublishSubscriptionOnHook_EnqueuesNothing()
    {
        var hookId = Guid.NewGuid().ToString();
        var subscription = await AddSubscriptionAsync(hookId, RevitWebhookMode.Publish);

        var result = _controller.ModelSync(Callback(hookId, subscription.LineageUrn));

        Assert.IsType<OkResult>(result);
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public async Task ModelSync_MatchingSyncSubscription_EnqueuesWithSyncSource()
    {
        var hookId = Guid.NewGuid().ToString();
        var subscription = await AddSubscriptionAsync(hookId, RevitWebhookMode.Sync);

        _controller.ModelSync(Callback(hookId, subscription.LineageUrn));

        var item = Assert.Single(DrainQueue());
        Assert.Equal("model.sync", item.Source);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"not an object\"")]
    [InlineData("{}")]
    [InlineData("{\"hook\":\"oops\"}")]
    [InlineData("{\"hook\":{\"hookId\":42}}")]
    public void ModelPublish_MalformedBody_ReturnsOk(string json)
    {
        using var document = JsonDocument.Parse(json);

        var result = _controller.ModelPublish(document.RootElement.Clone());

        Assert.IsType<OkResult>(result);
        Assert.Empty(DrainQueue());
    }

    [Fact]
    public void DemoReceiver_AnyBody_ReturnsOk()
    {
        using var document = JsonDocument.Parse("{\"event\":\"revit.link.added\"}");

        var result = _controller.DemoReceiver(document.RootElement.Clone());

        Assert.IsType<OkResult>(result);
    }

    [Theory]
    [InlineData("{\"resourceUrn\":\"urn:adsk.wipprod:dm.lineage:abc\"}", "urn:adsk.wipprod:dm.lineage:abc")]
    [InlineData("{\"payload\":{\"ItemUrn\":\"urn:adsk.wipprod:dm.lineage:abc?version=3\"}}", "urn:adsk.wipprod:dm.lineage:abc")]
    [InlineData("{\"payload\":{\"model\":{\"ids\":[\"x\",\"urn:adsk.wipprod:dm.lineage:nested\"]}}}", "urn:adsk.wipprod:dm.lineage:nested")]
    [InlineData("{\"resourceUrn\":\"urn:adsk.wipprod:fs.file:vf.abc?version=3\",\"payload\":{}}", null)]
    [InlineData("{\"payload\":{\"ModelName\":\"A.rvt\"}}", null)]
    [InlineData("[]", null)]
    public void ExtractLineageUrn_VariousPayloads_ReturnsLineageUrnOrNull(string json, string? expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, RevitWebhooksController.ExtractLineageUrn(document.RootElement));
    }
}
