using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApsSamples.Services;

public interface ILinkingJobStatusService
{
    void StoreLinkingJobDetails(string workItemId, string projectId, string projectName, string userName, List<LinkChangeDetail> changes);
    void UpdateStatus(string workItemId, string status, string? reportUrl = null, string? modelName = null);
    void StorePublishInfo(string workItemId, string projectId, string itemId, string accessToken);
    LinkingJobStatusInfo? GetStatus(string workItemId);
    List<LinkingJobStatusInfo> GetJobsByProject(string projectId);
    List<LinkingJobStatusInfo> GetAllJobs();
    List<LinkingJobStatusInfo> GetFailedJobs();
}

public class LinkingJobStatusService : ILinkingJobStatusService
{
    private readonly ConcurrentDictionary<string, LinkingJobStatusInfo> _jobsCache = new();
    private readonly ILogger<LinkingJobStatusService> _logger;
    private readonly string _filePath;
    private readonly object _fileLock = new();

    public LinkingJobStatusService(ILogger<LinkingJobStatusService> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "linking-jobs.json");

        LoadFromFile();
    }

    public void StoreLinkingJobDetails(string workItemId, string projectId, string projectName, string userName, List<LinkChangeDetail> changes)
    {
        _jobsCache.AddOrUpdate(workItemId,
            key => new LinkingJobStatusInfo
            {
                WorkItemId = workItemId,
                ProjectId = projectId,
                ProjectName = projectName,
                UserName = userName,
                Changes = changes,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.ProjectName = projectName;
                existing.UserName = userName;
                existing.Changes = changes;
                return existing;
            });

        _logger.LogInformation("Stored linking job details for WorkItem {WorkItemId}", workItemId);
        SaveToFile();
    }

    public void UpdateStatus(string workItemId, string status, string? reportUrl = null, string? modelName = null)
    {
        _jobsCache.AddOrUpdate(workItemId,
            key => new LinkingJobStatusInfo
            {
                WorkItemId = workItemId,
                Status = status,
                ReportUrl = reportUrl,
                ModelName = modelName,
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.Status = status;
                existing.LastUpdated = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(reportUrl))
                {
                    existing.ReportUrl = reportUrl;
                }

                if (!string.IsNullOrEmpty(modelName))
                {
                    existing.ModelName = modelName;
                }

                return existing;
            });

        _logger.LogInformation("Updated status for Linking WorkItem {WorkItemId}: {Status}", workItemId, status);
        SaveToFile();
    }

    public void StorePublishInfo(string workItemId, string projectId, string itemId, string accessToken)
    {
        _jobsCache.AddOrUpdate(workItemId,
            key => new LinkingJobStatusInfo
            {
                WorkItemId = workItemId,
                ProjectId = projectId,
                ItemId = itemId,
                AccessToken = accessToken,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.ProjectId = projectId;
                existing.ItemId = itemId;
                existing.AccessToken = accessToken;
                return existing;
            });

        _logger.LogInformation("Stored publish info for Linking WorkItem {WorkItemId}", workItemId);
        SaveToFile();
    }

    public LinkingJobStatusInfo? GetStatus(string workItemId)
    {
        _jobsCache.TryGetValue(workItemId, out var job);
        return job;
    }

    public List<LinkingJobStatusInfo> GetJobsByProject(string projectId)
    {
        return _jobsCache.Values
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.LastUpdated)
            .ToList();
    }

    public List<LinkingJobStatusInfo> GetAllJobs()
    {
        return _jobsCache.Values
            .OrderByDescending(j => j.LastUpdated)
            .ToList();
    }

    public List<LinkingJobStatusInfo> GetFailedJobs()
    {
        return _jobsCache.Values
            .Where(j => j.Status.Contains("failed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(j => j.LastUpdated)
            .ToList();
    }

    private void SaveToFile()
    {
        try
        {
            lock (_fileLock)
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var json = JsonSerializer.Serialize(_jobsCache.Values.ToList(), options);
                File.WriteAllText(_filePath, json);

                _logger.LogInformation("Saved {Count} linking jobs to {Path} (AccessToken excluded)",
                    _jobsCache.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving linking jobs to file");
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No existing linking jobs file found at {Path}", _filePath);
                return;
            }

            lock (_fileLock)
            {
                var json = File.ReadAllText(_filePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var loadedData = JsonSerializer.Deserialize<List<LinkingJobStatusInfo>>(json, options);

                if (loadedData != null)
                {
                    foreach (var item in loadedData)
                    {
                        _jobsCache.TryAdd(item.WorkItemId, item);
                    }

                    _logger.LogInformation("Loaded {Count} linking jobs from {Path}", loadedData.Count, _filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading linking jobs from file");
        }
    }
}

public class LinkingJobStatusInfo
{
    public string WorkItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ReportUrl { get; set; }
    public string? ModelName { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? ItemId { get; set; }

    [JsonIgnore] // Never serialize AccessToken to JSON
    public string? AccessToken { get; set; }

    public string? UserName { get; set; }
    public List<LinkChangeDetail> Changes { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class LinkChangeDetail
{
    public string Action { get; set; } = string.Empty; // "Add" or "Remove"
    public string LinkedModelName { get; set; } = string.Empty;
    public string? LinkedModelGuid { get; set; }
}
