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
    [HttpPost("model-publish")]
    public async Task<IActionResult> ModelPublish([FromBody] JsonElement body)
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
                logger.LogWarning("Received model-publish webhook without a hookId");
                return Ok();
            }

            var task = await taskService.GetTaskByWebhookHookIdAsync(hookId);
            if (task == null)
            {
                logger.LogWarning("No tracked task found for webhook hook {HookId}", hookId);
                return Ok();
            }

            var succeeded = !(body.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("status", out var statusProp) &&
                string.Equals(statusProp.GetString(), "failed", StringComparison.OrdinalIgnoreCase));

            task.Status = succeeded ? AgentTaskStatus.Completed : AgentTaskStatus.Failed;
            task.CompletedAt = DateTime.UtcNow;
            if (succeeded)
            {
                task.ProgressDetail = "Model published successfully.";
            }
            else
            {
                task.ErrorDetail = "Model publish failed.";
            }

            await taskService.UpdateTaskAsync(task);

            await conversationService.AddMessageAsync(task.ConversationId, new ConversationMessage
            {
                Role = "assistant",
                Content = succeeded
                    ? $"✅ **{task.Name}** — the model was published successfully."
                    : $"⚠️ **{task.Name}** — the model publish failed.",
                Timestamp = DateTime.UtcNow
            });

            logger.LogInformation("Recorded model-publish webhook for task {TaskId}, succeeded={Succeeded}", task.TaskId, succeeded);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling model-publish webhook");
        }

        return Ok();
    }
}
