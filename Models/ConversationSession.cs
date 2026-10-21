using System.Text.Json;
using System.Text.Json.Serialization;

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

    // Serialized agent sessions keyed by agent id, so each assistant in the conversation keeps its own session.
    public Dictionary<string, JsonElement> SerializedAgentSessions { get; set; } = new();

    // Legacy single-agent session; only read on load to migrate into SerializedAgentSessions.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? SerializedAgentSession { get; set; }
}
