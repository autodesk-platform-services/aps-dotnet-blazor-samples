using System.Text.Json.Serialization;

namespace ApsSamples.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RevitWebhookMode
{
    Publish,
    Sync
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RevitWebhookSubscriptionStatus
{
    Active,
    Processing,
    Error
}

public class RevitWebhookSubscription
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string HubId { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string LineageUrn { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public RevitWebhookMode Mode { get; set; }
    public string HookId { get; set; } = string.Empty;
    public string ElementGroupId { get; set; } = string.Empty;
    // AEC Data Model project id resolved from ProjectId; null on subscriptions created before it was stored.
    public string? AecProjectId { get; set; }
    // Version the diff starts from. Publish mode advances it after every processed version;
    // Sync mode keeps it fixed so WIP changes are compared against the last published state.
    public int BaselineVersion { get; set; }
    public int LastSeenVersion { get; set; }
    public HashSet<string> KnownLinkElementIds { get; set; } = new();
    // "{eventType}:{fileName}" keys already delivered (Sync mode), so repeated syncs don't refire.
    public HashSet<string> NotifiedKeys { get; set; } = new();
    public List<OutgoingWebhookTarget> Targets { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RevitWebhookSubscriptionStatus Status { get; set; } = RevitWebhookSubscriptionStatus.Active;
    public string? LastError { get; set; }
}

public class OutgoingWebhookTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public List<string> EventTypes { get; set; } = new();
}
