using System.Threading.Channels;

namespace ApsSamples.Services;

public record DelegatedTaskWorkItem(string TaskId);

// In-memory hand-off between the DelegateTask tool (which must return to the delegating assistant
// at once) and DelegatedTaskProcessor. Queued work is lost on restart.
public class DelegatedTaskQueue
{
    private readonly Channel<DelegatedTaskWorkItem> _channel =
        Channel.CreateUnbounded<DelegatedTaskWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<DelegatedTaskWorkItem> Reader => _channel.Reader;

    public bool Enqueue(DelegatedTaskWorkItem item) => _channel.Writer.TryWrite(item);
}
