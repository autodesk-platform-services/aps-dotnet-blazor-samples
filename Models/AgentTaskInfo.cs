namespace ApsSamples.Models;

public class AgentTaskInfo
{
    public string TaskId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AgentTaskStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ProgressDetail { get; set; }
    public string? ErrorDetail { get; set; }
    public string? WebhookHookId { get; set; }
    public string? WebhookTargetFileName { get; set; }
    public string? RelatedMessageId { get; set; }
}
