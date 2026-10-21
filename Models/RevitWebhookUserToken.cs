namespace ApsSamples.Models;

// The 3LO refresh token of a user who set up Revit webhook subscriptions, so the background
// processor can query the AEC Data Model on their behalf long after their session has ended.
public class RevitWebhookUserToken
{
    public string UserId { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
