using System.Text.Json;
using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class RevitWebhookProcessorTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private const string LinkAddedDiff = """
        [
          {"type":"ADDITION","element":{"id":"new-type","name":"MEP.rvt","properties":{"results":[
            {"name":"Revit Category Type Id","value":"RVT Links"},{"name":"Element Context","value":"Type"}]}}},
          {"type":"ADDITION","element":{"id":"new-instance","name":"MEP.rvt : 1 : location <Not Shared>","properties":{"results":[
            {"name":"Revit Category Type Id","value":"RVT Links"},{"name":"Element Context","value":"Instance"}]}}}
        ]
        """;

    private readonly TempWorkingDirectoryFixture _fixture;

    public RevitWebhookProcessorTests(TempWorkingDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Harness(RevitWebhookProcessor Processor, RevitWebhookStore Store, FakeAecDataModelService Aec, FakeDispatcher Dispatcher);

    private static Harness NewHarness()
    {
        var store = new RevitWebhookStore(NullLogger<RevitWebhookStore>.Instance);
        var aec = new FakeAecDataModelService();
        var dispatcher = new FakeDispatcher();
        var scopeFactory = new ServiceCollection()
            .AddSingleton<IAecDataModelService>(aec)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        var processor = new RevitWebhookProcessor(
            new RevitWebhookQueue(),
            store,
            new FakeTokenService(),
            dispatcher,
            scopeFactory,
            NullLogger<RevitWebhookProcessor>.Instance);

        return new Harness(processor, store, aec, dispatcher);
    }

    private static async Task<RevitWebhookSubscription> AddSubscriptionAsync(
        RevitWebhookStore store, RevitWebhookMode mode, string? aecProjectId = "aec-project")
    {
        var subscription = new RevitWebhookSubscription
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = "user-1",
            ProjectId = "b.project",
            AecProjectId = aecProjectId,
            ModelName = "Architecture.rvt",
            LineageUrn = "urn:adsk.wipprod:dm.lineage:abc",
            Mode = mode,
            HookId = Guid.NewGuid().ToString(),
            ElementGroupId = "group-1",
            BaselineVersion = 27,
            LastSeenVersion = 27,
            KnownLinkElementIds = ["existing-link"],
            Targets = [new OutgoingWebhookTarget { Url = "https://example.com/hook", EventTypes = [RevitWebhookEventTypes.LinkAdded] }]
        };
        await store.UpsertAsync(subscription);
        return subscription;
    }

    private static AecElementGroup Group(int version) =>
        new("group-1", "Architecture.rvt", "urn:adsk.wipprod:dm.lineage:abc", null, version);

    private static RevitWebhookWorkItem Item(RevitWebhookSubscription subscription, string source) =>
        new(subscription.Id, source) { EnqueuedAt = DateTime.UtcNow };

    [Fact]
    public async Task ProcessAsync_PublishWithLinkAdded_DispatchesRecordsEventAndAdvancesBaseline()
    {
        var h = NewHarness();
        var subscription = await AddSubscriptionAsync(h.Store, RevitWebhookMode.Publish);
        h.Aec.Groups.Enqueue(Group(28));
        h.Aec.Diff = LinkAddedDiff;

        await h.Processor.ProcessAsync(Item(subscription, "model.publish"), CancellationToken.None);

        var call = Assert.Single(h.Dispatcher.Calls);
        Assert.Equal(RevitWebhookEventTypes.LinkAdded, call.Change.EventType);
        Assert.Equal("MEP.rvt", call.Change.FileName);
        Assert.Equal(27, call.FromVersion);
        Assert.Equal(28, call.ToVersion);
        Assert.Equal("model.publish", call.Source);
        Assert.Equal([27], h.Aec.DiffStartVersions);

        var webhookEvent = Assert.Single(h.Store.GetEvents(subscription.Id));
        Assert.Equal("MEP.rvt", webhookEvent.FileName);
        Assert.Equal(27, webhookEvent.FromVersion);
        Assert.Equal(28, webhookEvent.ToVersion);
        Assert.True(Assert.Single(webhookEvent.Deliveries).Success);

        var stored = h.Store.Get(subscription.Id)!;
        Assert.Equal(28, stored.BaselineVersion);
        Assert.Equal(28, stored.LastSeenVersion);
        Assert.Empty(stored.NotifiedKeys);
        Assert.Equal(["existing-link", "new-instance", "new-type"], stored.KnownLinkElementIds.Order().ToList());
        Assert.Equal(RevitWebhookSubscriptionStatus.Active, stored.Status);
        Assert.Null(stored.LastError);
    }

    [Fact]
    public async Task ProcessAsync_SyncTwiceWithSameChange_NotifiesOnceAndKeepsBaseline()
    {
        var h = NewHarness();
        var subscription = await AddSubscriptionAsync(h.Store, RevitWebhookMode.Sync);
        h.Aec.Diff = LinkAddedDiff;

        h.Aec.Groups.Enqueue(Group(28));
        await h.Processor.ProcessAsync(Item(subscription, "model.sync"), CancellationToken.None);

        h.Aec.Groups.Enqueue(Group(28));
        await h.Processor.ProcessAsync(Item(subscription, "model.sync"), CancellationToken.None);

        Assert.Single(h.Dispatcher.Calls);
        Assert.Equal([28, 28], h.Aec.DiffStartVersions);

        var stored = h.Store.Get(subscription.Id)!;
        Assert.Equal(28, stored.BaselineVersion);
        Assert.Equal(28, stored.LastSeenVersion);
        Assert.Contains("revit.link.added:MEP.rvt", stored.NotifiedKeys);
    }

    [Fact]
    public async Task ProcessAsync_NoElementGroup_RecordsError()
    {
        var h = NewHarness();
        var subscription = await AddSubscriptionAsync(h.Store, RevitWebhookMode.Publish);
        h.Aec.Groups.Enqueue(null);

        await h.Processor.ProcessAsync(Item(subscription, "model.publish"), CancellationToken.None);

        Assert.Empty(h.Dispatcher.Calls);
        Assert.Empty(h.Aec.DiffStartVersions);
        var stored = h.Store.Get(subscription.Id)!;
        Assert.Equal(RevitWebhookSubscriptionStatus.Error, stored.Status);
        Assert.Contains("no element group", stored.LastError);
    }

    [Fact]
    public async Task ProcessAsync_SubscriptionWithoutAecProjectId_ResolvesAndPersistsIt()
    {
        var h = NewHarness();
        var subscription = await AddSubscriptionAsync(h.Store, RevitWebhookMode.Publish, aecProjectId: null);
        h.Aec.Groups.Enqueue(Group(28));

        await h.Processor.ProcessAsync(Item(subscription, "model.publish"), CancellationToken.None);

        Assert.Equal(1, h.Aec.ProjectCalls);
        Assert.Equal(["aec-project"], h.Aec.GroupProjectIds);
        var stored = h.Store.Get(subscription.Id)!;
        Assert.Equal("aec-project", stored.AecProjectId);
        Assert.Equal(RevitWebhookSubscriptionStatus.Active, stored.Status);
    }

    [Fact]
    public async Task ProcessAsync_SubscriptionWithAecProjectId_DoesNotResolveProject()
    {
        var h = NewHarness();
        var subscription = await AddSubscriptionAsync(h.Store, RevitWebhookMode.Publish, aecProjectId: "stored-project");
        h.Aec.Groups.Enqueue(Group(28));

        await h.Processor.ProcessAsync(Item(subscription, "model.publish"), CancellationToken.None);

        Assert.Equal(0, h.Aec.ProjectCalls);
        Assert.Equal(["stored-project"], h.Aec.GroupProjectIds);
        Assert.Equal("stored-project", h.Store.Get(subscription.Id)!.AecProjectId);
    }

    [Fact]
    public async Task ProcessAsync_DiffFails_SetsLastError()
    {
        var h = NewHarness();
        var subscription = await AddSubscriptionAsync(h.Store, RevitWebhookMode.Publish);
        h.Aec.Groups.Enqueue(Group(28));
        h.Aec.DiffException = new InvalidOperationException("AEC Data Model error: boom");

        await h.Processor.ProcessAsync(Item(subscription, "model.publish"), CancellationToken.None);

        var stored = h.Store.Get(subscription.Id)!;
        Assert.Equal(RevitWebhookSubscriptionStatus.Error, stored.Status);
        Assert.Equal("AEC Data Model error: boom", stored.LastError);
        Assert.Equal(27, stored.BaselineVersion);
    }

    [Fact]
    public async Task ProcessAsync_RemovedSubscription_DoesNothing()
    {
        var h = NewHarness();
        var subscription = await AddSubscriptionAsync(h.Store, RevitWebhookMode.Publish);
        await h.Store.RemoveAsync(subscription.Id);

        await h.Processor.ProcessAsync(Item(subscription, "model.publish"), CancellationToken.None);

        Assert.Equal(0, h.Aec.GroupCalls);
        Assert.Null(h.Store.Get(subscription.Id));
    }

    private sealed class FakeAecDataModelService : IAecDataModelService
    {
        public Queue<AecElementGroup?> Groups { get; } = new();
        private AecElementGroup? _last;
        public int GroupCalls => GroupProjectIds.Count;
        public List<string> GroupProjectIds { get; } = [];
        public int ProjectCalls { get; private set; }
        public string Diff { get; set; } = "[]";
        public Exception? DiffException { get; set; }
        public List<int> DiffStartVersions { get; } = [];

        public Task<AecProject?> FindProjectAsync(string accessToken, string dataManagementProjectId, string? region, CancellationToken cancellationToken = default)
        {
            ProjectCalls++;
            return Task.FromResult<AecProject?>(new AecProject("aec-project", "Project", "aec-hub"));
        }

        public Task<AecElementGroup?> FindElementGroupAsync(string accessToken, string aecProjectId, string lineageUrn, string? region, CancellationToken cancellationToken = default)
        {
            GroupProjectIds.Add(aecProjectId);
            if (Groups.Count > 0)
            {
                _last = Groups.Dequeue();
            }
            return Task.FromResult(_last);
        }

        public Task<IReadOnlyList<string>> GetLinkElementIdsAsync(string accessToken, string elementGroupId, string? region, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<JsonElement> GetDiffAsync(string accessToken, string elementGroupId, int startVersion, string? region, CancellationToken cancellationToken = default)
        {
            DiffStartVersions.Add(startVersion);
            if (DiffException != null)
            {
                throw DiffException;
            }
            using var doc = JsonDocument.Parse(Diff);
            return Task.FromResult(doc.RootElement.Clone());
        }
    }

    private sealed record DispatchCall(RevitLinkChange Change, int FromVersion, int ToVersion, string Source);

    private sealed class FakeDispatcher : IRevitWebhookDispatcher
    {
        public List<DispatchCall> Calls { get; } = [];

        public Task<IReadOnlyList<RevitWebhookDelivery>> DispatchAsync(
            RevitWebhookSubscription subscription, RevitLinkChange change, int fromVersion, int toVersion, string source, CancellationToken cancellationToken = default)
        {
            Calls.Add(new DispatchCall(change, fromVersion, toVersion, source));
            IReadOnlyList<RevitWebhookDelivery> deliveries = subscription.Targets
                .Select(t => new RevitWebhookDelivery { TargetUrl = t.Url, Success = true, StatusCode = 200 })
                .ToList();
            return Task.FromResult(deliveries);
        }
    }

    private sealed class FakeTokenService : IRevitWebhookTokenService
    {
        public Task SaveRefreshTokenAsync(string userId, string refreshToken) => Task.CompletedTask;
        public Task<bool> HasTokenAsync(string userId) => Task.FromResult(true);
        public Task<string> GetAccessTokenAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult("access-token");
    }
}
