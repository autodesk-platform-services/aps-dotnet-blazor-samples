namespace ApsSamples.Models;

public static class RevitWebhookEventTypes
{
    public const string LinkAdded = "revit.link.added";
    public const string LinkRemoved = "revit.link.removed";

    public static readonly IReadOnlyList<string> All = [LinkAdded, LinkRemoved];
}
