using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IRevitWebhookStore
{
    IReadOnlyList<RevitWebhookSubscription> GetSubscriptions(string? projectId = null);
    IReadOnlyList<RevitWebhookSubscription> GetByHookId(string hookId);
    RevitWebhookSubscription? Get(string id);
    Task UpsertAsync(RevitWebhookSubscription subscription);
    Task<bool> RemoveAsync(string id);

    // The event log keeps only the newest entries (see RevitWebhookStore.MaxEvents).
    Task AddEventAsync(RevitWebhookEvent webhookEvent);
    IReadOnlyList<RevitWebhookEvent> GetEvents(string? subscriptionId = null);
}
