using System.Text.Json.Nodes;
using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class RevitWebhookStoreTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private readonly TempWorkingDirectoryFixture _fixture;

    public RevitWebhookStoreTests(TempWorkingDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private static RevitWebhookStore NewStore() => new(NullLogger<RevitWebhookStore>.Instance);

    private static RevitWebhookSubscription NewSubscription(string? hookId = null, string projectId = "b.project") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserId = "user-1",
        ProjectId = projectId,
        HubId = "b.hub",
        ModelName = "Architecture.rvt",
        LineageUrn = "urn:adsk.wipprod:dm.lineage:" + Guid.NewGuid().ToString("N"),
        FolderId = "urn:adsk.wipprod:fs.folder:co.folder",
        Mode = RevitWebhookMode.Publish,
        HookId = hookId ?? Guid.NewGuid().ToString(),
        ElementGroupId = "element-group-1",
        BaselineVersion = 3,
        LastSeenVersion = 3,
        KnownLinkElementIds = ["link-type-1", "link-instance-1"],
        Targets =
        [
            new OutgoingWebhookTarget
            {
                Url = "https://example.com/hook",
                Secret = "s3cret",
                EventTypes = [RevitWebhookEventTypes.LinkAdded]
            }
        ]
    };

    [Fact]
    public async Task UpsertAsync_ThenGet_ReturnsSubscription()
    {
        var store = NewStore();
        var subscription = NewSubscription();

        await store.UpsertAsync(subscription);

        var loaded = store.Get(subscription.Id);
        Assert.NotNull(loaded);
        Assert.Equal(subscription.LineageUrn, loaded.LineageUrn);
        Assert.Contains(store.GetSubscriptions("b.project"), s => s.Id == subscription.Id);
        Assert.DoesNotContain(store.GetSubscriptions("b.other-project"), s => s.Id == subscription.Id);
    }

    [Fact]
    public async Task GetByHookId_ReturnsOnlySubscriptionsOnThatHook()
    {
        var store = NewStore();
        var hookId = Guid.NewGuid().ToString();
        var first = NewSubscription(hookId);
        var second = NewSubscription(hookId);
        var other = NewSubscription();
        await store.UpsertAsync(first);
        await store.UpsertAsync(second);
        await store.UpsertAsync(other);

        var onHook = store.GetByHookId(hookId);

        Assert.Equal(
            new[] { first.Id, second.Id }.OrderBy(id => id),
            onHook.Select(s => s.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task UpsertAsync_BaselineUpdate_PersistsAcrossInstances()
    {
        var store = NewStore();
        var subscription = NewSubscription();
        await store.UpsertAsync(subscription);

        subscription.BaselineVersion = 5;
        subscription.LastSeenVersion = 5;
        subscription.NotifiedKeys.Add("revit.link.added:MEP.rvt");
        await store.UpsertAsync(subscription);

        var reloaded = NewStore().Get(subscription.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(5, reloaded.BaselineVersion);
        Assert.Equal(5, reloaded.LastSeenVersion);
        Assert.Contains("revit.link.added:MEP.rvt", reloaded.NotifiedKeys);
    }

    [Fact]
    public async Task NewInstance_LoadsSubscriptionFromDisk()
    {
        var subscription = NewSubscription();
        subscription.Mode = RevitWebhookMode.Sync;
        await NewStore().UpsertAsync(subscription);

        var reloaded = NewStore().Get(subscription.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(RevitWebhookMode.Sync, reloaded.Mode);
        Assert.Equal(subscription.HookId, reloaded.HookId);
        Assert.Equal(subscription.KnownLinkElementIds, reloaded.KnownLinkElementIds);
        var target = Assert.Single(reloaded.Targets);
        Assert.Equal("https://example.com/hook", target.Url);
        Assert.Equal("s3cret", target.Secret);
        Assert.Equal([RevitWebhookEventTypes.LinkAdded], target.EventTypes);
    }

    [Fact]
    public async Task UpsertAsync_AecProjectId_PersistsAcrossInstances()
    {
        var subscription = NewSubscription();
        subscription.AecProjectId = "aec-project-1";
        await NewStore().UpsertAsync(subscription);

        var reloaded = NewStore().Get(subscription.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("aec-project-1", reloaded.AecProjectId);
    }

    [Fact]
    public async Task NewInstance_SubscriptionSavedWithoutAecProjectId_LoadsAsNull()
    {
        var subscription = NewSubscription();
        subscription.AecProjectId = "aec-project-1";
        await NewStore().UpsertAsync(subscription);

        // Rewrite the file as if it had been saved before AecProjectId existed.
        var path = Path.Combine(_fixture.TempPath, "Data", "revit-webhook-subscriptions.json");
        var items = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsArray();
        foreach (var item in items)
        {
            item!.AsObject().Remove("aecProjectId");
        }
        await File.WriteAllTextAsync(path, items.ToJsonString());

        var reloaded = NewStore().Get(subscription.Id);

        Assert.NotNull(reloaded);
        Assert.Null(reloaded.AecProjectId);
        Assert.Equal(subscription.ElementGroupId, reloaded.ElementGroupId);
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromStoreAndDisk()
    {
        var store = NewStore();
        var subscription = NewSubscription();
        await store.UpsertAsync(subscription);

        Assert.True(await store.RemoveAsync(subscription.Id));
        Assert.False(await store.RemoveAsync(subscription.Id));

        Assert.Null(store.Get(subscription.Id));
        Assert.Null(NewStore().Get(subscription.Id));
    }

    [Fact]
    public async Task AddEventAsync_MoreThanMax_KeepsNewestEntries()
    {
        var store = NewStore();
        var subscriptionId = Guid.NewGuid().ToString("N");
        var start = DateTime.UtcNow;

        for (var i = 0; i < RevitWebhookStore.MaxEvents + 5; i++)
        {
            await store.AddEventAsync(new RevitWebhookEvent
            {
                SubscriptionId = subscriptionId,
                EventType = RevitWebhookEventTypes.LinkAdded,
                FileName = $"Link{i}.rvt",
                Timestamp = start.AddSeconds(i)
            });
        }

        // The cap is global, so count across all subscriptions (other tests share this directory).
        Assert.Equal(RevitWebhookStore.MaxEvents, store.GetEvents().Count);

        var events = store.GetEvents(subscriptionId);
        Assert.Equal($"Link{RevitWebhookStore.MaxEvents + 4}.rvt", events[0].FileName);
        Assert.DoesNotContain(events, e => e.FileName == "Link4.rvt");
        Assert.Contains(events, e => e.FileName == "Link5.rvt");

        Assert.Equal(RevitWebhookStore.MaxEvents, NewStore().GetEvents().Count);
    }

    [Fact]
    public async Task AddEventAsync_NewInstance_LoadsEventWithDeliveries()
    {
        var subscriptionId = Guid.NewGuid().ToString("N");
        await NewStore().AddEventAsync(new RevitWebhookEvent
        {
            SubscriptionId = subscriptionId,
            EventType = RevitWebhookEventTypes.LinkRemoved,
            FileName = "MEP.rvt",
            FromVersion = 3,
            ToVersion = 4,
            Source = "model.publish",
            Deliveries = [new RevitWebhookDelivery { TargetUrl = "https://example.com/hook", Success = true, StatusCode = 200 }]
        });

        var loaded = Assert.Single(NewStore().GetEvents(subscriptionId));

        Assert.Equal(RevitWebhookEventTypes.LinkRemoved, loaded.EventType);
        Assert.Equal(4, loaded.ToVersion);
        var delivery = Assert.Single(loaded.Deliveries);
        Assert.True(delivery.Success);
        Assert.Equal(200, delivery.StatusCode);
    }
}
