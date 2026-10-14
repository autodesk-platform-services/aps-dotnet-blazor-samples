using ApsSamples.Models;
using Microsoft.Agents.AI;

namespace ApsSamples.Services;

public interface IAgentConversationService
{
    Task<ConversationSession> CreateConversationAsync(string projectId, string userId, string agentId);
    Task<IReadOnlyList<ConversationSession>> GetConversationsAsync(string projectId, string userId, string agentId);
    Task AddMessageAsync(string conversationId, ConversationMessage message);
    Task ClearMessagesAsync(string conversationId);
    Task<ConversationSession?> GetConversationAsync(string conversationId);
    Task<System.Text.Json.JsonElement?> GetSerializedSessionAsync(string conversationId);
    Task SaveSerializedSessionAsync(string conversationId, System.Text.Json.JsonElement serializedSession);
    Task ClearSerializedSessionAsync(string conversationId);
    Task<AgentSession> GetOrLoadAgentSessionAsync(string conversationId, Func<CancellationToken, Task<AgentSession>> factory, CancellationToken ct = default);
    Task RemoveAgentSessionAsync(string conversationId);
}
