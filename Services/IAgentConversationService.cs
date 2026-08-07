using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentConversationService
{
    Task<ConversationSession> CreateConversationAsync(string projectId, string userId, string agentId);
    Task<IReadOnlyList<ConversationSession>> GetConversationsAsync(string projectId, string userId, string agentId);
    Task AddMessageAsync(string conversationId, ConversationMessage message);
    Task ClearMessagesAsync(string conversationId);
    Task<ConversationSession?> GetConversationAsync(string conversationId);
}
