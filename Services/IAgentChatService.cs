namespace ApsSamples.Services;

public interface IAgentChatService
{
    void SetHubContext(string hubId);
    void SetProjectContext(string projectId);
    void SetConversationContext(string conversationId);

    /// <summary>
    /// Streams the response of assistant <paramref name="agentId"/> to <paramref name="userMessage"/>
    /// in the current conversation. <see cref="SetConversationContext"/> must be called first and the
    /// assistant must exist, otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    IAsyncEnumerable<string> StreamResponseAsync(string agentId, string userMessage, CancellationToken ct = default);
    Task ResetSessionAsync(string conversationId, CancellationToken ct = default);
}
