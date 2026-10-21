using System.Threading.Channels;

namespace ApsSamples.Services;

// Source is the incoming APS event that triggered the work, e.g. "model.publish" or "model.sync".
public record RevitWebhookWorkItem(string SubscriptionId, string Source)
{
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}

// In-memory hand-off between the webhook receiver (which must answer APS within seconds) and
// RevitWebhookProcessor. Queued work is lost on restart.
public class RevitWebhookQueue
{
    private readonly Channel<RevitWebhookWorkItem> _channel =
        Channel.CreateUnbounded<RevitWebhookWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<RevitWebhookWorkItem> Reader => _channel.Reader;

    public bool Enqueue(RevitWebhookWorkItem item) => _channel.Writer.TryWrite(item);
}
