using System.Text.Json;
using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApsSamples.Controllers;

[ApiController]
[Route("api/revit-webhooks")]
public class RevitWebhooksController(
    IRevitWebhookStore store,
    RevitWebhookQueue queue,
    ILogger<RevitWebhooksController> logger) : ControllerBase
{
    // Fields checked first when looking for the model's lineage URN in an adsk.c4r callback.
    private static readonly string[] LineageUrnFields = ["resourceUrn", "lineageUrn", "itemUrn", "itemId", "urn"];

    [HttpPost("model-publish")]
    public IActionResult ModelPublish([FromBody] JsonElement body) =>
        Receive(body, RevitWebhookMode.Publish, "model.publish");

    [HttpPost("model-sync")]
    public IActionResult ModelSync([FromBody] JsonElement body) =>
        Receive(body, RevitWebhookMode.Sync, "model.sync");

    // Log-only target so the sample can be tried end to end without an external receiver.
    [HttpPost("demo-receiver")]
    public IActionResult DemoReceiver([FromBody] JsonElement body)
    {
        var signature = Request.Headers["x-revit-webhook-signature"].ToString();
        logger.LogInformation("Demo receiver got Revit webhook (signature: {Signature}): {Body}",
            string.IsNullOrEmpty(signature) ? "<none>" : signature, body.GetRawText());
        return Ok();
    }

    // APS expects a 2xx within a few seconds, so this only matches and enqueues; the
    // AEC Data Model work happens in RevitWebhookProcessor.
    private IActionResult Receive(JsonElement body, RevitWebhookMode mode, string source)
    {
        try
        {
            var hookId = body.ValueKind == JsonValueKind.Object &&
                body.TryGetProperty("hook", out var hook) &&
                hook.ValueKind == JsonValueKind.Object &&
                hook.TryGetProperty("hookId", out var hookIdProp) &&
                hookIdProp.ValueKind == JsonValueKind.String
                ? hookIdProp.GetString()
                : null;

            if (string.IsNullOrEmpty(hookId))
            {
                logger.LogWarning("Received {Source} webhook without a hookId", source);
                return Ok();
            }

            var subscriptions = store.GetByHookId(hookId).Where(s => s.Mode == mode).ToList();
            if (subscriptions.Count == 0)
            {
                logger.LogWarning("No Revit webhook subscription found for {Source} hook {HookId}", source, hookId);
                return Ok();
            }

            // Hooks are folder-scoped and shared, so narrow down to the model that fired. Without
            // a URN every subscription on the hook is enqueued; the processor's version check
            // skips models that didn't change.
            var lineageUrn = ExtractLineageUrn(body);
            if (lineageUrn != null)
            {
                subscriptions = subscriptions
                    .Where(s => string.Equals(s.LineageUrn, lineageUrn, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                logger.LogInformation("No lineage URN in {Source} callback for hook {HookId}; enqueueing all {Count} subscription(s)",
                    source, hookId, subscriptions.Count);
            }

            foreach (var subscription in subscriptions)
            {
                queue.Enqueue(new RevitWebhookWorkItem(subscription.Id, source));
                logger.LogInformation("Enqueued {Source} for Revit webhook subscription {SubscriptionId}", source, subscription.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling {Source} webhook", source);
        }

        return Ok();
    }

    // Returns the item lineage URN (urn:adsk.wipprod:dm.lineage:...) from an adsk.c4r callback, or
    // null when none is present. Known field names are tried first (top level, then payload),
    // then any string in the payload that looks like a lineage URN. Version URNs are ignored
    // because they can't be compared with a subscription's lineage URN.
    internal static string? ExtractLineageUrn(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var payload = body.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : default;

        foreach (var container in new[] { body, payload })
        {
            if (container.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in container.EnumerateObject())
            {
                if (LineageUrnFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                    AsLineageUrn(property.Value) is { } urn)
                {
                    return urn;
                }
            }
        }

        return payload.ValueKind == JsonValueKind.Object ? FindLineageUrn(payload, depth: 0) : null;
    }

    private static string? FindLineageUrn(JsonElement element, int depth)
    {
        if (depth > 5)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return AsLineageUrn(element);
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (FindLineageUrn(property.Value, depth + 1) is { } urn)
                    {
                        return urn;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FindLineageUrn(item, depth + 1) is { } urn)
                    {
                        return urn;
                    }
                }
                break;
        }

        return null;
    }

    private static string? AsLineageUrn(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrEmpty(text) || !text.Contains("dm.lineage:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Strip a query string such as "?version=3" if one is attached.
        var queryIndex = text.IndexOf('?');
        return queryIndex >= 0 ? text[..queryIndex] : text;
    }
}
