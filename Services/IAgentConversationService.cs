using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentConversationService
{
    Task<ConversationSession> CreateConversationAsync(string projectId, string userId);
    Task<IReadOnlyList<ConversationSession>> GetConversationsAsync(string projectId, string userId);
    Task AddMessageAsync(string conversationId, ConversationMessage message);
    Task<ConversationSession?> GetConversationAsync(string conversationId);
}
