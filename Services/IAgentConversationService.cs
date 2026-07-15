using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentConversationService
{
    Task<ConversationSession> GetOrCreateConversationAsync(string projectId, string userId);
    Task AddMessageAsync(string conversationId, ConversationMessage message);
    Task<ConversationSession?> GetConversationAsync(string conversationId);
}
