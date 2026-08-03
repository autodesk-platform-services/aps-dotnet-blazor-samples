using ApsSamples.Models;

namespace ApsSamples.Services;

public interface IAgentConversationService
{
    Task<ConversationSession> CreateConversationAsync(string projectId, string userId);
    Task<IReadOnlyList<ConversationSession>> GetConversationsAsync(string projectId, string userId);
    Task AddMessageAsync(string conversationId, ConversationMessage message);
    Task ClearMessagesAsync(string conversationId);
    Task<ConversationSession?> GetConversationAsync(string conversationId);
    Task<System.Text.Json.JsonElement?> GetSerializedSessionAsync(string conversationId);
    Task SaveSerializedSessionAsync(string conversationId, System.Text.Json.JsonElement serializedSession);
    Task ClearSerializedSessionAsync(string conversationId);
}
