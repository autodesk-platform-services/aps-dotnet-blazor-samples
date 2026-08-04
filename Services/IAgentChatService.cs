using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentChatService
{
    void SetHubContext(string hubId);
    void SetProjectContext(string projectId);
    void SetConversationContext(string conversationId);
    Task SetAgentContextAsync(string agentId);
    IAsyncEnumerable<string> StreamResponseAsync(IList<ConversationMessage> history, CancellationToken ct = default);
}
