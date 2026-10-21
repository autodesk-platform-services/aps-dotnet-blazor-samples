using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IRevitWebhookDispatcher
{
    // POSTs the change to every target of the subscription that listens to its event type and
    // returns one delivery result per target. Never throws for delivery failures.
    Task<IReadOnlyList<RevitWebhookDelivery>> DispatchAsync(
        RevitWebhookSubscription subscription,
        RevitLinkChange change,
        int fromVersion,
        int toVersion,
        string source,
        CancellationToken cancellationToken = default);
}
