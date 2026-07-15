using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentChatService
{
    IAsyncEnumerable<string> StreamResponseAsync(IList<ConversationMessage> history, CancellationToken ct = default);
}
