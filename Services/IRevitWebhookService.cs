using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IRevitWebhookService
{
    // Subscribes a Revit Cloud Worksharing model (lineageUrn is the Data Management item id) to
    // model.publish or model.sync. Throws InvalidOperationException with a user-readable message
    // when the subscription can't be created (missing configuration, no stored token, no AEC Data
    // Model element group for the model yet, ...).
    Task<RevitWebhookSubscription> SubscribeAsync(
        string userId,
        string projectId,
        string hubId,
        string? region,
        string modelName,
        string lineageUrn,
        RevitWebhookMode mode,
        IEnumerable<OutgoingWebhookTarget> targets,
        CancellationToken cancellationToken = default);

    Task UnsubscribeAsync(string subscriptionId);

    Task<RevitWebhookSubscription> UpdateTargetsAsync(string subscriptionId, IEnumerable<OutgoingWebhookTarget> targets);
}
