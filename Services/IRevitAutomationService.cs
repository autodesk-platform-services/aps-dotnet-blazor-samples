namespace ApsSamples.Services;

public interface IRevitAutomationService
{
    Task<WorkItemSubmissionResult> SubmitWorkItemAsync(
        ModelConfiguration modelConfig,
        Dictionary<string, object> toolInputs);

    Task<WorkItemStatus> GetWorkItemStatusAsync(string workItemId);
}
