namespace ApsSamples.Models;

public class ConversationSession
{
    public string ConversationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public List<ConversationMessage> Messages { get; set; } = new();
    public System.Text.Json.JsonElement? SerializedAgentSession { get; set; }
}
