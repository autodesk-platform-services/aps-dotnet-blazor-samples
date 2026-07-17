using System.Text.Json;
using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApsSamples.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController(
    IAgentTaskService taskService,
    IAgentConversationService conversationService,
    ILogger<WebhooksController> logger) : ControllerBase
{
    // dm.version.added is scoped by folder, not by item, so it fires for every new version
    // added anywhere in that folder - not just the model our task is waiting on. Filter by the
    // event's source file name and ignore anything that doesn't match our target model.
    [HttpPost("version-added")]
    public async Task<IActionResult> VersionAdded([FromBody] JsonElement body)
    {
        // Always acknowledge quickly, whether or not we can match the notification to a
        // tracked task - Autodesk retries/disables hooks that don't get a fast 200 response.
        try
        {
            var hookId = body.TryGetProperty("hook", out var hook) && hook.TryGetProperty("hookId", out var hookIdProp)
                ? hookIdProp.GetString()
                : null;

            if (string.IsNullOrEmpty(hookId))
            {
                logger.LogWarning("Received dm.version.added webhook without a hookId");
                return Ok();
            }

            var task = await taskService.GetTaskByWebhookHookIdAsync(hookId);
            if (task == null)
            {
                logger.LogWarning("No tracked task found for webhook hook {HookId}", hookId);
                return Ok();
            }

            var sourceFileName = body.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("sourceFileName", out var sourceFileNameProp)
                ? sourceFileNameProp.GetString()
                : null;

            if (!string.IsNullOrEmpty(sourceFileName) &&
                !string.IsNullOrEmpty(task.WebhookTargetFileName) &&
                !string.Equals(sourceFileName, task.WebhookTargetFileName, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Ignoring dm.version.added for '{SourceFileName}', task {TaskId} is waiting on '{TargetFileName}'",
                    sourceFileName, task.TaskId, task.WebhookTargetFileName);
                return Ok();
            }

            task.Status = AgentTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.ProgressDetail = "Model published successfully.";
            await taskService.UpdateTaskAsync(task);

            await conversationService.AddMessageAsync(task.ConversationId, new ConversationMessage
            {
                Role = "assistant",
                Content = $"✅ **{task.Name}** — the model was published successfully.",
                Timestamp = DateTime.UtcNow
            });

            logger.LogInformation("Recorded dm.version.added webhook for task {TaskId}", task.TaskId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling dm.version.added webhook");
        }

        return Ok();
    }
}
