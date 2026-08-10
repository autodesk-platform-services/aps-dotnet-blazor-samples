namespace ApsSamples.Services;

public record SelectedProjectRecord(
    string ProjectId,
    string ProjectName,
    string HubId,
    string HubName,
    string Region);

public interface ISelectedProjectService
{
    Task<SelectedProjectRecord?> GetSelectedProjectAsync(string userId);
    Task SetSelectedProjectAsync(string userId, SelectedProjectRecord record);
}
