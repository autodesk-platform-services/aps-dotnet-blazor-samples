namespace ApsSamples.Services;

public interface IAgentChatService
{
    void SetHubContext(string hubId);
    void SetProjectContext(string projectId);
    void SetConversationContext(string conversationId);
    IAsyncEnumerable<string> StreamResponseAsync(string userMessage, CancellationToken ct = default);
    Task ResetSessionAsync(string conversationId, CancellationToken ct = default);
}
