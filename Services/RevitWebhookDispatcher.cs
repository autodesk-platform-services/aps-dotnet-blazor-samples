using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApsSamples.Models;

namespace ApsSamples.Services;

/// <summary>
/// Sends custom <c>revit.link.*</c> events to the subscription's outgoing targets. The body is a
/// versioned JSON envelope; targets with a secret also get an HMAC-SHA256 signature header so the
/// receiver can verify the sender. A failed delivery is retried once.
/// </summary>
public class RevitWebhookDispatcher(IHttpClientFactory httpClientFactory, ILogger<RevitWebhookDispatcher> logger) : IRevitWebhookDispatcher
{
    public const string SignatureHeader = "x-revit-webhook-signature";
    public const string EnvelopeVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // Settable so tests don't have to wait between attempts.
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<RevitWebhookDelivery>> DispatchAsync(
        RevitWebhookSubscription subscription,
        RevitLinkChange change,
        int fromVersion,
        int toVersion,
        string source,
        CancellationToken cancellationToken = default)
    {
        var targets = subscription.Targets
            .Where(t => !string.IsNullOrWhiteSpace(t.Url)
                && t.EventTypes.Contains(change.EventType, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (targets.Count == 0)
        {
            return [];
        }

        var body = JsonSerializer.Serialize(new
        {
            version = EnvelopeVersion,
            @event = change.EventType,
            subscriptionId = subscription.Id,
            resourceUrn = subscription.LineageUrn,
            deliveredAt = DateTime.UtcNow,
            payload = new
            {
                modelName = subscription.ModelName,
                fileName = change.FileName,
                source,
                fromVersion,
                toVersion,
                instanceCount = change.InstanceCount,
                typeChanged = change.TypeChanged,
                elementIds = change.ElementIds
            }
        }, JsonOptions);

        var deliveries = new List<RevitWebhookDelivery>();
        foreach (var target in targets)
        {
            deliveries.Add(await DeliverWithRetryAsync(target, body, change.EventType, cancellationToken));
        }

        return deliveries;
    }

    // Lowercase hex HMAC-SHA256 of the exact request body; sent as "sha256=<hex>".
    public static string ComputeSignature(string secret, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<RevitWebhookDelivery> DeliverWithRetryAsync(
        OutgoingWebhookTarget target, string body, string eventType, CancellationToken cancellationToken)
    {
        var delivery = await SendAsync(target, body, cancellationToken);
        if (delivery.Success)
        {
            return delivery;
        }

        logger.LogWarning("Delivery of {EventType} to {Url} failed ({Error}); retrying in {Delay}",
            eventType, target.Url, delivery.Error, RetryDelay);

        await Task.Delay(RetryDelay, cancellationToken);
        delivery = await SendAsync(target, body, cancellationToken);

        if (delivery.Success)
        {
            logger.LogInformation("Delivered {EventType} to {Url} on retry", eventType, target.Url);
        }
        else
        {
            logger.LogWarning("Delivery of {EventType} to {Url} failed after retry: {Error}", eventType, target.Url, delivery.Error);
        }

        return delivery;
    }

    private async Task<RevitWebhookDelivery> SendAsync(OutgoingWebhookTarget target, string body, CancellationToken cancellationToken)
    {
        var delivery = new RevitWebhookDelivery { TargetUrl = target.Url };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(target.Secret))
            {
                request.Headers.TryAddWithoutValidation(SignatureHeader, $"sha256={ComputeSignature(target.Secret, body)}");
            }

            var httpClient = httpClientFactory.CreateClient();
            using var response = await httpClient.SendAsync(request, timeout.Token);

            delivery.StatusCode = (int)response.StatusCode;
            delivery.Success = response.IsSuccessStatusCode;
            if (!delivery.Success)
            {
                delivery.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            delivery.Success = false;
            delivery.Error = ex is OperationCanceledException ? "Request timed out" : ex.Message;
        }

        return delivery;
    }
}
