namespace ApsSamples.Models;

public class RevitWebhookEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SubscriptionId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int FromVersion { get; set; }
    public int ToVersion { get; set; }
    // Incoming APS event that triggered processing, e.g. "model.publish" or "model.sync".
    public string Source { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<RevitWebhookDelivery> Deliveries { get; set; } = new();
}

public class RevitWebhookDelivery
{
    public string TargetUrl { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
}
