using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using System.Text.Json;

namespace ApsSamples.Services;

public class RevitAutomationService(DesignAutomationClient daClient,
    ILogger<RevitAutomationService> logger,
    IUserSessionService userSessionService,
    IConfiguration configuration) : IRevitAutomationService
{
    public async Task<WorkItemSubmissionResult> SubmitWorkItemAsync(
        ModelConfiguration modelConfig,
        Dictionary<string, object> toolInputs)
    {
        try
        {
            logger.LogInformation("Submitting workitem for Revit automation activity");

            // Get activity alias from configuration
            var activityAlias = configuration["Forge:RevitAutomationActivity"];
            if (string.IsNullOrEmpty(activityAlias))
            {
                return new WorkItemSubmissionResult
                {
                    IsSuccess = false,
                    Message = "RevitAutomationActivity not configured in appsettings.json"
                };
            }

            // Build work item arguments
            var arguments = new Dictionary<string, IArgument>();

            // Create revitmodel.json
            var revitModelJson = JsonSerializer.Serialize(new
            {
                region = modelConfig.Region,
                projectGuid = modelConfig.ProjectGuid,
                modelGuid = modelConfig.ModelGuid,
                toolName = modelConfig.ToolName,
                save = modelConfig.SaveAfter
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            logger.LogInformation("RevitModel: {RevitModelJson}", revitModelJson);

            arguments.Add("revitmodel", new XrefTreeArgument
            {
                Url = "data:application/json," + Uri.EscapeDataString(revitModelJson),
                Verb = Verb.Get
            });

            // Create toolinputs.json
            var toolInputsJson = JsonSerializer.Serialize(toolInputs, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            logger.LogInformation("ToolInputs: {ToolInputsJson}", toolInputsJson);

            arguments.Add("toolinputs", new XrefTreeArgument
            {
                Url = "data:application/json," + Uri.EscapeDataString(toolInputsJson),
                Verb = Verb.Get
            });

            // Add token
            arguments.Add("adsk3LeggedToken", new StringArgument(userSessionService.AccessToken));

            // Create workitem
            var workItem = new WorkItem
            {
                ActivityId = activityAlias,
                Arguments = arguments
            };

            // Submit workitem
            var workItemStatus = await daClient.CreateWorkItemAsync(workItem);

            logger.LogInformation("WorkItem submitted: {WorkItemId}, Status: {Status}",
                workItemStatus.Id, workItemStatus.Status);

            return new WorkItemSubmissionResult
            {
                WorkItemId = workItemStatus.Id,
                Status = workItemStatus.Status.ToString(),
                IsSuccess = true,
                Message = "WorkItem submitted successfully"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error submitting workitem");
            return new WorkItemSubmissionResult
            {
                IsSuccess = false,
                Message = $"Error submitting workitem: {ex.Message}"
            };
        }
    }

    public async Task<WorkItemStatus> GetWorkItemStatusAsync(string workItemId)
    {
        try
        {
            var status = await daClient.GetWorkitemStatusAsync(workItemId);

            return new WorkItemStatus
            {
                WorkItemId = workItemId,
                Status = status.Status.ToString(),
                Progress = status.Progress ?? string.Empty,
                ReportUrl = status.ReportUrl,
                Stats = status.Stats != null ? new WorkItemStats
                {
                    TimeQueued = status.Stats.TimeQueued.ToString(),
                    TimeDownloadStarted = status.Stats.TimeDownloadStarted.ToString(),
                    TimeInstructionsStarted = status.Stats.TimeInstructionsStarted.ToString(),
                    TimeInstructionsEnded = status.Stats.TimeInstructionsEnded.ToString(),
                    TimeUploadEnded = status.Stats.TimeUploadEnded.ToString()
                } : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting workitem status for: {WorkItemId}", workItemId);
            throw;
        }
    }
}

public class WorkItemSubmissionResult
{
    public string? WorkItemId { get; set; }
    public string? Status { get; set; }
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public string? JobToken { get; set; }
}

public class WorkItemStatus
{
    public string WorkItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public string? ReportUrl { get; set; }
    public WorkItemStats? Stats { get; set; }
}

public class WorkItemStats
{
    public string TimeQueued { get; set; } = string.Empty;
    public string TimeDownloadStarted { get; set; } = string.Empty;
    public string TimeInstructionsStarted { get; set; } = string.Empty;
    public string TimeInstructionsEnded { get; set; } = string.Empty;
    public string TimeUploadEnded { get; set; } = string.Empty;
}
