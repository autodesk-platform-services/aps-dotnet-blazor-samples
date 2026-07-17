using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentChatService
{
    void SetProjectContext(string projectId);
    IAsyncEnumerable<string> StreamResponseAsync(IList<ConversationMessage> history, CancellationToken ct = default);
}
