using System.Text.Json;
using ApsSamples.Models;
using Autodesk.DataManagement;
using Autodesk.Webhooks;
using Autodesk.Webhooks.Model;
using Microsoft.Extensions.Options;

namespace ApsSamples.Services;

// Creates and removes Revit webhook subscriptions. One adsk.c4r hook exists per (event type,
// folder) and is shared by every subscription on a model in that folder; it is deleted once the
// last of them is removed.
public class RevitWebhookService(
    WebhooksClient webhooksClient,
    DataManagementClient dataManagementClient,
    IAPSAuthenticationService apsAuth,
    IAecDataModelService aecDataModel,
    IRevitWebhookTokenService tokenService,
    IRevitWebhookStore store,
    IOptions<RevitWebhookOptions> options,
    ILogger<RevitWebhookService> logger) : IRevitWebhookService
{
    public async Task<RevitWebhookSubscription> SubscribeAsync(
        string userId,
        string projectId,
        string hubId,
        string? region,
        string modelName,
        string lineageUrn,
        RevitWebhookMode mode,
        IEnumerable<OutgoingWebhookTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var callbackBaseUrl = options.Value.CallbackBaseUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(callbackBaseUrl))
        {
            throw new InvalidOperationException(
                "RevitWebhooks:CallbackBaseUrl is not configured. Set it to your public URL followed by /api/revit-webhooks.");
        }

        if (string.IsNullOrEmpty(userId) || !await tokenService.HasTokenAsync(userId))
        {
            throw new InvalidOperationException(
                "Sign in again: no stored refresh token is available, so the webhook couldn't query the AEC Data Model on your behalf.");
        }

        var alreadySubscribed = store.GetSubscriptions(projectId)
            .Any(s => s.Mode == mode && string.Equals(s.LineageUrn, lineageUrn, StringComparison.OrdinalIgnoreCase));
        if (alreadySubscribed)
        {
            throw new InvalidOperationException($"{modelName} is already subscribed to {EventName(mode)}.");
        }

        var userToken = await tokenService.GetAccessTokenAsync(userId, cancellationToken);

        var folder = await dataManagementClient.GetItemParentFolderAsync(
            projectId: projectId,
            itemId: lineageUrn,
            accessToken: userToken);
        var folderId = folder?.Data?.Id;
        if (string.IsNullOrEmpty(folderId))
        {
            throw new InvalidOperationException($"Couldn't resolve the parent folder of {modelName}.");
        }

        // Resolve the AEC Data Model element group before creating the hook so a model that isn't
        // available yet doesn't leave an unused hook behind.
        var aecProject = await aecDataModel.FindProjectAsync(userToken, projectId, region, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Couldn't find {modelName}'s project in the AEC Data Model. Make sure your account has access to it.");

        var elementGroup = await aecDataModel.FindElementGroupAsync(userToken, aecProject.Id, lineageUrn, region, cancellationToken);
        if (elementGroup?.VersionNumber is not int baselineVersion)
        {
            throw new InvalidOperationException(
                $"The AEC Data Model has no element group for {modelName} yet. " +
                "Make sure the model is a published Revit Cloud Worksharing model and try again in a few minutes.");
        }

        var linkElementIds = await aecDataModel.GetLinkElementIdsAsync(userToken, elementGroup.Id, region, cancellationToken);

        // Hook management uses an app-level (2-legged) token, so the hook can still be deleted
        // when no user session is available.
        var webhookToken = await apsAuth.GetTwoLeggedTokenAsync();
        var webhookEvent = ToEvent(mode);
        var webhookRegion = ToWebhooksRegion(region);
        var callbackUrl = $"{callbackBaseUrl}/{(mode == RevitWebhookMode.Publish ? "model-publish" : "model-sync")}";

        var hookId = await FindExistingHookIdAsync(webhookEvent, folderId, callbackUrl, webhookRegion, webhookToken)
            ?? await CreateHookAsync(webhookEvent, hubId, projectId, folderId, callbackUrl, webhookRegion, webhookToken);
        if (string.IsNullOrEmpty(hookId))
        {
            throw new InvalidOperationException($"Creating the {EventName(mode)} webhook for {modelName} failed.");
        }

        var subscription = new RevitWebhookSubscription
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            ProjectId = projectId,
            HubId = hubId,
            Region = region,
            ModelName = modelName,
            LineageUrn = lineageUrn,
            FolderId = folderId,
            Mode = mode,
            HookId = hookId,
            ElementGroupId = elementGroup.Id,
            AecProjectId = aecProject.Id,
            BaselineVersion = baselineVersion,
            LastSeenVersion = baselineVersion,
            KnownLinkElementIds = linkElementIds.ToHashSet(),
            Targets = targets.ToList(),
            CreatedAt = DateTime.UtcNow,
            Status = RevitWebhookSubscriptionStatus.Active
        };

        await store.UpsertAsync(subscription);

        logger.LogInformation(
            "Subscribed {ModelName} ({LineageUrn}) to {Event} with hook {HookId}, baseline version {Version}",
            modelName, lineageUrn, EventName(mode), hookId, subscription.BaselineVersion);

        return subscription;
    }

    public async Task UnsubscribeAsync(string subscriptionId)
    {
        var subscription = store.Get(subscriptionId);
        if (subscription == null || !await store.RemoveAsync(subscriptionId))
        {
            return;
        }

        logger.LogInformation("Removed Revit webhook subscription {SubscriptionId} for {ModelName}",
            subscriptionId, subscription.ModelName);

        if (string.IsNullOrEmpty(subscription.HookId) || store.GetByHookId(subscription.HookId).Count > 0)
        {
            return;
        }

        try
        {
            var webhookToken = await apsAuth.GetTwoLeggedTokenAsync();
            await webhooksClient.DeleteSystemEventHookAsync(
                Systems.AdskC4r,
                ToEvent(subscription.Mode),
                subscription.HookId,
                region: ToWebhooksRegion(subscription.Region),
                accessToken: webhookToken);

            logger.LogInformation("Deleted webhook {HookId} - no subscriptions use it anymore", subscription.HookId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete webhook {HookId}", subscription.HookId);
        }
    }

    public async Task<RevitWebhookSubscription> UpdateTargetsAsync(string subscriptionId, IEnumerable<OutgoingWebhookTarget> targets)
    {
        var subscription = store.Get(subscriptionId)
            ?? throw new InvalidOperationException("The subscription no longer exists.");

        subscription.Targets = targets.ToList();
        await store.UpsertAsync(subscription);
        return subscription;
    }

    // Reuse a hook only if it also calls back to the current URL: a stale hook pointing at an old
    // tunnel URL would never reach this app.
    private async Task<string?> FindExistingHookIdAsync(
        Events webhookEvent, string folderId, string callbackUrl, Region region, string webhookToken)
    {
        var existingHooks = await webhooksClient.GetSystemEventHooksAsync(
            Systems.AdskC4r,
            webhookEvent,
            region: region,
            scopeName: "folder",
            accessToken: webhookToken);

        return existingHooks.Data?
            .FirstOrDefault(h =>
                string.Equals(h.Scope?.Folder, folderId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(h.CallbackUrl, callbackUrl, StringComparison.OrdinalIgnoreCase))
            ?.HookId;
    }

    private async Task<string?> CreateHookAsync(
        Events webhookEvent, string hubId, string projectId, string folderId, string callbackUrl, Region region, string webhookToken)
    {
        var hookPayload = new HookPayload
        {
            CallbackUrl = callbackUrl,
            HubId = hubId,
            ProjectId = projectId,
            Scope = new Dictionary<string, object> { ["folder"] = folderId }
        };

        var response = await webhooksClient.CreateSystemEventHookAsync(
            Systems.AdskC4r,
            webhookEvent,
            hookPayload,
            region: region,
            accessToken: webhookToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            var hookId = JsonSerializer.Deserialize<HookDetails>(responseBody,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })?.HookId;
            if (!string.IsNullOrEmpty(hookId))
            {
                return hookId;
            }
        }

        // Some responses only carry the new hook in the Location header (".../hooks/<hookId>").
        return response.Headers.Location?.ToString().TrimEnd('/').Split('/').LastOrDefault();
    }

    private static Events ToEvent(RevitWebhookMode mode) =>
        mode == RevitWebhookMode.Publish ? Events.ModelPublish : Events.ModelSync;

    private static string EventName(RevitWebhookMode mode) =>
        mode == RevitWebhookMode.Publish ? "model.publish" : "model.sync";

    private static Region ToWebhooksRegion(string? region) =>
        Enum.TryParse<Region>(region, ignoreCase: true, out var parsed) ? parsed : Region.US;
}
