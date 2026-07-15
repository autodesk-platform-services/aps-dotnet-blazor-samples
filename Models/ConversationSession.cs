namespace ApsSamples.Models;

public class ConversationSession
{
    public string ConversationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; set; } = new();
}
