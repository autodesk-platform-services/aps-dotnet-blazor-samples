namespace ApsSamples.Models;

public class RevitWebhookOptions
{
    // Public base URL that APS calls back, e.g. "https://<ngrok-url>/api/revit-webhooks".
    // "/model-publish" or "/model-sync" is appended per event type.
    public string CallbackBaseUrl { get; set; } = string.Empty;
    public int ExtractionPollSeconds { get; set; } = 30;
    public int ExtractionTimeoutMinutes { get; set; } = 60;
}
