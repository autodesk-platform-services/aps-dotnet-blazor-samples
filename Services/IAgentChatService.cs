namespace ApsSamples.Services;

public interface IAgentChatService
{
    void SetHubContext(string hubId);
    void SetProjectContext(string projectId);
    void SetConversationContext(string conversationId);
    Task SetAgentContextAsync(string agentId);

    /// <summary>
    /// Streams the agent response for <paramref name="userMessage"/>.
    /// <see cref="SetConversationContext"/> and <see cref="SetAgentContextAsync"/> must be
    /// called first, otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    IAsyncEnumerable<string> StreamResponseAsync(string userMessage, CancellationToken ct = default);
    Task ResetSessionAsync(string conversationId, CancellationToken ct = default);
}
