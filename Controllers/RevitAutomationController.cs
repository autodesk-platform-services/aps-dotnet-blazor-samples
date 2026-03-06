using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApsSamples.Services;
using Autodesk.DataManagement;
using Autodesk.DataManagement.Model;

namespace ApsSamples.Controllers;

[ApiController]
[Route("api/da")]
public class RevitAutomationController(
    ILogger<RevitAutomationController> logger,
    ICreationJobStatusService creationJobStatusService,
    ILinkingJobStatusService linkingJobStatusService,
    DataManagementClient dataManagementClient) : ControllerBase
{
    [HttpPost("callback")]
    public async Task<IActionResult> OnWorkItemComplete([FromBody] WorkItemCallbackData callbackData)
    {
        try
        {
            logger.LogInformation("Received workitem callback: {WorkItemId}, Status: {Status}", 
                callbackData.Id, callbackData.Status);

            // Log the full callback data
            var jsonData = JsonSerializer.Serialize(callbackData, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            logger.LogInformation("Callback data: {Data}", jsonData);

            // Update status in cache - try creation jobs first, then linking jobs
            if (!string.IsNullOrEmpty(callbackData.Id))
            {
                var creationJob = creationJobStatusService.GetStatus(callbackData.Id);
                if (creationJob != null)
                {
                    creationJobStatusService.UpdateStatus(
                        callbackData.Id,
                        callbackData.Status ?? "unknown",
                        callbackData.ReportUrl);
                }
                else
                {
                    linkingJobStatusService.UpdateStatus(
                        callbackData.Id,
                        callbackData.Status ?? "unknown",
                        callbackData.ReportUrl);
                }
            }

            // Handle different statuses
            switch (callbackData.Status?.ToLower())
            {
                case "success":
                    logger.LogInformation("WorkItem {WorkItemId} completed successfully", callbackData.Id);
                    
                    // Publish the model to make changes visible
                    await PublishModel(callbackData.Id!);
                    
                    break;

                case "failed":
                case "faileddownload":
                case "failedupload":
                case "failedinstruction":
                    logger.LogError("WorkItem {WorkItemId} failed with status: {Status}. Report: {Report}", 
                        callbackData.Id, callbackData.Status, callbackData.ReportUrl);
                    break;

                case "cancelled":
                    logger.LogWarning("WorkItem {WorkItemId} was cancelled", callbackData.Id);
                    break;

                default:
                    logger.LogWarning("WorkItem {WorkItemId} has unknown status: {Status}", 
                        callbackData.Id, callbackData.Status);
                    break;
            }

            // Return 200 OK to acknowledge receipt
            return Ok(new { received = true, workItemId = callbackData.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing workitem callback");
            // Still return 200 to avoid Automation API retrying
            return Ok(new { received = true, error = ex.Message });
        }
    }

    private async Task PublishModel(string workItemId)
    {
        try
        {
            // Try to get from creation jobs first, then linking jobs
            var creationJob = creationJobStatusService.GetStatus(workItemId);
            LinkingJobStatusInfo? linkingJob = null;
            
            if (creationJob == null)
            {
                linkingJob = linkingJobStatusService.GetStatus(workItemId);
            }
            
            // Check if we found the job in either service
            if (creationJob == null && linkingJob == null)
            {
                logger.LogWarning("Cannot publish model: WorkItem {WorkItemId} not found in cache", workItemId);
                return;
            }

            // Get common properties (both types have these)
            var projectId = creationJob?.ProjectId ?? linkingJob?.ProjectId;
            var itemId = creationJob?.ItemId ?? linkingJob?.ItemId;
            var accessToken = creationJob?.AccessToken ?? linkingJob?.AccessToken;

            if (string.IsNullOrEmpty(projectId) || 
                string.IsNullOrEmpty(itemId) ||
                string.IsNullOrEmpty(accessToken))
            {
                logger.LogWarning("Cannot publish model: Missing publish info for WorkItem {WorkItemId}", workItemId);
                return;
            }

            logger.LogInformation("Publishing model for WorkItem {WorkItemId}, Project: {ProjectId}, Item: {ItemId}", 
                workItemId, projectId, itemId);

            // Create the publish payload with proper structure
            var publishPayload = new PublishModelPayload();
            publishPayload.Type = TypeCommands.Commands;
            publishPayload.Attributes.Extension.Type = TypeCommandtypePublishmodel.CommandsautodeskBim360C4RModelPublish;
            publishPayload.Attributes.Extension.VarVersion = "1.0.0";
            publishPayload.Relationships.Resources.Data =
            [
                new() {
                    Type = TypeItem.Items,
                    Id = itemId
                }
            ];

            // Execute publish using the SDK and await the result
            var publishResult = await dataManagementClient.ExecutePublishModelAsync(
                accessToken,
                publishPayload,
                projectId);

            // Check the result
            if (publishResult != null)
            {
                var commandId = publishResult.Id;
                var commandType = publishResult.Type;
                
                logger.LogInformation("Publish command created for WorkItem {WorkItemId}. CommandId: {CommandId}, Type: {Type}", 
                    workItemId, commandId, commandType);

                // Check if the command has status information
                if (publishResult.Attributes?.Status != null)
                {
                    logger.LogInformation("Publish status for WorkItem {WorkItemId}: {Status}", 
                        workItemId, publishResult.Attributes.Status);
                }

                logger.LogInformation("Successfully submitted publish command for WorkItem {WorkItemId}", workItemId);
            }
            else
            {
                logger.LogWarning("Publish command returned no result for WorkItem {WorkItemId}", workItemId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error publishing model for WorkItem {WorkItemId}", workItemId);
        }
    }

    [HttpGet("status/{workItemId}")]
    public IActionResult GetWorkItemStatus(string workItemId)
    {
        // Try creation jobs first
        var creationJob = creationJobStatusService.GetStatus(workItemId);
        if (creationJob != null)
        {
            return Ok(new
            {
                workItemId = creationJob.WorkItemId,
                status = creationJob.Status,
                reportUrl = creationJob.ReportUrl,
                modelName = creationJob.ModelName,
                projectName = creationJob.ProjectName,
                type = "creation"
            });
        }

        // Then try linking jobs
        var linkingJob = linkingJobStatusService.GetStatus(workItemId);
        if (linkingJob != null)
        {
            return Ok(new
            {
                workItemId = linkingJob.WorkItemId,
                status = linkingJob.Status,
                reportUrl = linkingJob.ReportUrl,
                modelName = linkingJob.ModelName,
                projectName = linkingJob.ProjectName,
                type = "linking"
            });
        }
        
        return NotFound(new { message = "WorkItem status not found" });
    }
}

// DTO for Automation API callback
public class WorkItemCallbackData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("progress")]
    public string? Progress { get; set; }

    [JsonPropertyName("reportUrl")]
    public string? ReportUrl { get; set; }

    [JsonPropertyName("stats")]
    public WorkItemCallbackStats? Stats { get; set; }

    [JsonPropertyName("activityId")]
    public string? ActivityId { get; set; }
}

public class WorkItemCallbackStats
{
    [JsonPropertyName("timeQueued")]
    public string? TimeQueued { get; set; }

    [JsonPropertyName("timeDownloadStarted")]
    public string? TimeDownloadStarted { get; set; }

    [JsonPropertyName("timeInstructionsStarted")]
    public string? TimeInstructionsStarted { get; set; }

    [JsonPropertyName("timeInstructionsEnded")]
    public string? TimeInstructionsEnded { get; set; }

    [JsonPropertyName("timeUploadEnded")]
    public string? TimeUploadEnded { get; set; }
}
