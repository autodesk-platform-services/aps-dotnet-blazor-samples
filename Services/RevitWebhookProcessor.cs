using System.Collections.Concurrent;
using ApsSamples.Models;
using Microsoft.Extensions.Options;

namespace ApsSamples.Services;

/// <summary>
/// Processes queued Revit webhook work: waits for the AEC Data Model element group to reach a new
/// version, diffs it against the subscription's baseline, classifies link changes, delivers them to the
/// outgoing targets, and records the result. Each work item runs on its own task; work for the same
/// subscription is serialized.
/// </summary>
public class RevitWebhookProcessor(
    RevitWebhookQueue queue,
    IRevitWebhookStore store,
    IRevitWebhookTokenService tokenService,
    IRevitWebhookDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    IOptions<RevitWebhookOptions> options,
    ILogger<RevitWebhookProcessor> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriptionLocks = new();
    // Per subscription: when the element group version that the last completed run processed was
    // read. Work enqueued before that moment is already covered by that run (the element group's
    // version reflects any version published before the read), so it is skipped instead of waiting
    // for a version that will never come.
    private readonly ConcurrentDictionary<string, DateTime> _coveredUntil = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResetInterruptedSubscriptionsAsync();

        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                _ = Task.Run(() => ProcessAsync(item, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task ProcessAsync(RevitWebhookWorkItem item, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received {Source} work for subscription {SubscriptionId}", item.Source, item.SubscriptionId);
        var gate = _subscriptionLocks.GetOrAdd(item.SubscriptionId, _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RevitWebhookSubscription? subscription = null;
        try
        {
            subscription = store.Get(item.SubscriptionId);
            if (subscription == null)
            {
                logger.LogInformation("Skipping {Source} work for removed subscription {SubscriptionId}", item.Source, item.SubscriptionId);
                return;
            }

            if (_coveredUntil.TryGetValue(subscription.Id, out var coveredUntil) && item.EnqueuedAt < coveredUntil)
            {
                logger.LogInformation("Skipping {Source} work for {ModelName}: already covered by a previous run", item.Source, subscription.ModelName);
                return;
            }

            await ProcessSubscriptionAsync(subscription, item.Source, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (subscription != null)
            {
                subscription.Status = RevitWebhookSubscriptionStatus.Active;
                await PersistIfStillSubscribedAsync(subscription);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Processing {Source} for subscription {SubscriptionId} failed", item.Source, item.SubscriptionId);
            if (subscription != null)
            {
                subscription.Status = RevitWebhookSubscriptionStatus.Error;
                subscription.LastError = ex.Message;
                await PersistIfStillSubscribedAsync(subscription);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ProcessSubscriptionAsync(RevitWebhookSubscription subscription, string source, CancellationToken cancellationToken)
    {
        // Publish compares against the last processed publish; Sync against the last processed sync,
        // while still diffing from the published baseline.
        var minimumVersion = subscription.Mode == RevitWebhookMode.Publish ? subscription.BaselineVersion : subscription.LastSeenVersion;

        logger.LogInformation("{ModelName}: processing {Source} ({Mode}); waiting for an AEC Data Model version newer than {Minimum}",
            subscription.ModelName, source, subscription.Mode, minimumVersion);

        subscription.Status = RevitWebhookSubscriptionStatus.Processing;
        await PersistIfStillSubscribedAsync(subscription);

        using var scope = scopeFactory.CreateScope();
        var aecDataModel = scope.ServiceProvider.GetRequiredService<IAecDataModelService>();

        var (elementGroup, observedAt) = await WaitForElementGroupAsync(aecDataModel, subscription, minimumVersion, cancellationToken);
        if (elementGroup == null)
        {
            var message = $"Timed out after {options.Value.ExtractionTimeoutMinutes} min waiting for an AEC Data Model version newer than {minimumVersion}.";
            logger.LogWarning("{ModelName}: {Message}", subscription.ModelName, message);
            subscription.Status = RevitWebhookSubscriptionStatus.Error;
            subscription.LastError = message;
            await PersistIfStillSubscribedAsync(subscription);
            return;
        }

        var newVersion = elementGroup.VersionNumber!.Value;
        logger.LogInformation("{ModelName}: AEC Data Model reached version {Version}; diffing from version {From}",
            subscription.ModelName, newVersion, subscription.BaselineVersion);
        subscription.ElementGroupId = elementGroup.Id;

        var fromVersion = subscription.BaselineVersion;
        var accessToken = await tokenService.GetAccessTokenAsync(subscription.UserId, cancellationToken);
        var diff = await aecDataModel.GetDiffAsync(accessToken, subscription.ElementGroupId, fromVersion, subscription.Region, cancellationToken);
        var changes = RevitLinkChangeClassifier.Classify(diff, subscription.KnownLinkElementIds);
        logger.LogInformation("{ModelName}: classified {Count} link change(s)", subscription.ModelName, changes.Count);

        foreach (var change in changes)
        {
            if (change.EventType == RevitWebhookEventTypes.LinkAdded)
            {
                subscription.KnownLinkElementIds.UnionWith(change.ElementIds);
            }
            else
            {
                subscription.KnownLinkElementIds.ExceptWith(change.ElementIds);
            }
        }

        var toNotify = subscription.Mode == RevitWebhookMode.Sync
            ? changes.Where(c => !subscription.NotifiedKeys.Contains(NotifiedKey(c))).ToList()
            : changes.ToList();

        logger.LogInformation("{ModelName}: delivering {Count} change(s) to {TargetCount} target(s)",
            subscription.ModelName, toNotify.Count, subscription.Targets.Count);

        foreach (var change in toNotify)
        {
            logger.LogInformation("{ModelName}: dispatching {EventType} for {FileName}", subscription.ModelName, change.EventType, change.FileName);
            var deliveries = await dispatcher.DispatchAsync(subscription, change, fromVersion, newVersion, source, cancellationToken);
            await store.AddEventAsync(new RevitWebhookEvent
            {
                SubscriptionId = subscription.Id,
                EventType = change.EventType,
                FileName = change.FileName,
                FromVersion = fromVersion,
                ToVersion = newVersion,
                Source = source,
                Deliveries = deliveries.ToList()
            });
        }

        if (subscription.Mode == RevitWebhookMode.Publish)
        {
            subscription.BaselineVersion = newVersion;
            subscription.NotifiedKeys.Clear();
        }
        else
        {
            subscription.NotifiedKeys.UnionWith(toNotify.Select(NotifiedKey));
        }
        subscription.LastSeenVersion = newVersion;
        subscription.Status = RevitWebhookSubscriptionStatus.Active;
        subscription.LastError = null;
        _coveredUntil[subscription.Id] = observedAt;

        await PersistIfStillSubscribedAsync(subscription);

        logger.LogInformation(
            "Processed {Source} for {ModelName}: versions {From}->{To}, {ChangeCount} link change(s), {NotifiedCount} delivered",
            source, subscription.ModelName, fromVersion, newVersion, changes.Count, toNotify.Count);
    }

    // Returns the element group once it reports a version newer than minimumVersion, together with
    // the time that version was requested, or (null, _) on timeout.
    private async Task<(AecElementGroup? ElementGroup, DateTime ObservedAt)> WaitForElementGroupAsync(
        IAecDataModelService aecDataModel, RevitWebhookSubscription subscription, int minimumVersion, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(0, options.Value.ExtractionPollSeconds));
        var deadline = DateTime.UtcNow.AddMinutes(Math.Max(0, options.Value.ExtractionTimeoutMinutes));

        // Subscriptions created before the AEC project id was stored resolve it once.
        if (string.IsNullOrEmpty(subscription.AecProjectId))
        {
            logger.LogInformation("{ModelName}: resolving the missing AEC project id", subscription.ModelName);
            var token = await tokenService.GetAccessTokenAsync(subscription.UserId, cancellationToken);
            var aecProject = await aecDataModel.FindProjectAsync(token, subscription.ProjectId, subscription.Region, cancellationToken)
                ?? throw new InvalidOperationException($"Couldn't find the project of {subscription.ModelName} in the AEC Data Model.");
            subscription.AecProjectId = aecProject.Id;
            await PersistIfStillSubscribedAsync(subscription);
        }

        while (true)
        {
            var observedAt = DateTime.UtcNow;
            // Fetched on every poll: the wait can outlive an access token, and the token service caches it.
            var accessToken = await tokenService.GetAccessTokenAsync(subscription.UserId, cancellationToken);
            var elementGroup = await aecDataModel.FindElementGroupAsync(
                accessToken, subscription.AecProjectId, subscription.LineageUrn, subscription.Region, cancellationToken);

            if (elementGroup?.VersionNumber > minimumVersion)
            {
                return (elementGroup, observedAt);
            }

            if (DateTime.UtcNow >= deadline)
            {
                return (null, observedAt);
            }

            logger.LogInformation("{ModelName}: AEC Data Model version {Version}; waiting for a version newer than {Minimum}",
                subscription.ModelName, elementGroup?.VersionNumber, minimumVersion);
            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    // A subscription removed while its work was in flight must not be written back.
    private async Task PersistIfStillSubscribedAsync(RevitWebhookSubscription subscription)
    {
        if (store.Get(subscription.Id) != null)
        {
            await store.UpsertAsync(subscription);
        }
    }

    // Work in flight when the app stopped is lost (the queue is in memory), so don't leave those
    // subscriptions stuck in Processing.
    private async Task ResetInterruptedSubscriptionsAsync()
    {
        foreach (var subscription in store.GetSubscriptions().Where(s => s.Status == RevitWebhookSubscriptionStatus.Processing))
        {
            subscription.Status = RevitWebhookSubscriptionStatus.Active;
            await store.UpsertAsync(subscription);
        }
    }

    private static string NotifiedKey(RevitLinkChange change) => $"{change.EventType}:{change.FileName}";
}
