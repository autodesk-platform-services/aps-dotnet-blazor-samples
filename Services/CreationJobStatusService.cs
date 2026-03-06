using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApsSamples.Services;

public interface ICreationJobStatusService
{
    void StoreCreationJobDetails(string workItemId, string projectId, string projectName, string userName, string modelName);
    void UpdateStatus(string workItemId, string status, string? reportUrl = null, string? modelName = null);
    void UpdateProgress(string workItemId, int completedModels, int failedModels);
    void StorePublishInfo(string workItemId, string projectId, string itemId, string accessToken);
    CreationJobStatusInfo? GetStatus(string workItemId);
    List<CreationJobStatusInfo> GetJobsByProject(string projectId);
    List<CreationJobStatusInfo> GetAllJobs();
    List<CreationJobStatusInfo> GetFailedJobs();
}

public class CreationJobStatusService : ICreationJobStatusService
{
    private readonly ConcurrentDictionary<string, CreationJobStatusInfo> _jobsCache = new();
    private readonly ILogger<CreationJobStatusService> _logger;
    private readonly string _filePath;
    private readonly object _fileLock = new();

    public CreationJobStatusService(ILogger<CreationJobStatusService> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "creation-jobs.json");

        LoadFromFile();
    }

    public void StoreCreationJobDetails(string workItemId, string projectId, string projectName, string userName, string modelName)
    {
        _jobsCache.AddOrUpdate(workItemId,
            key => new CreationJobStatusInfo
            {
                WorkItemId = workItemId,
                ProjectId = projectId,
                ProjectName = projectName,
                UserName = userName,
                Status = "pending",
                ModelName = modelName,
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.ProjectName = projectName;
                existing.UserName = userName;
                existing.ModelName = modelName;
                return existing;
            });

        _logger.LogInformation("Stored creation job details for WorkItem {WorkItemId}: {ModelName}",
            workItemId, modelName);

        SaveToFile();
    }

    public void UpdateStatus(string workItemId, string status, string? reportUrl = null, string? modelName = null)
    {
        if (_jobsCache.TryGetValue(workItemId, out var job))
        {
            job.Status = status;
            job.LastUpdated = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(reportUrl))
            {
                job.ReportUrl = reportUrl;
            }

            if (!string.IsNullOrEmpty(modelName))
            {
                job.ModelName = modelName;
            }

            _logger.LogInformation("Updated status for Creation WorkItem {WorkItemId}: {Status}", workItemId, status);
            SaveToFile();
        }
    }

    public void UpdateProgress(string workItemId, int completedModels, int failedModels)
    {
        if (_jobsCache.TryGetValue(workItemId, out var job))
        {
            job.CompletedModels = completedModels;
            job.FailedModels = failedModels;
            job.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Updated creation job progress for {WorkItemId}: {Completed} completed, {Failed} failed",
                workItemId, completedModels, failedModels);

            SaveToFile();
        }
    }

    public void StorePublishInfo(string workItemId, string projectId, string itemId, string accessToken)
    {
        if (_jobsCache.TryGetValue(workItemId, out var job))
        {
            job.ProjectId = projectId;
            job.ItemId = itemId;
            job.AccessToken = accessToken;

            _logger.LogInformation("Stored publish info for Creation WorkItem {WorkItemId}", workItemId);
            SaveToFile();
        }
    }

    public CreationJobStatusInfo? GetStatus(string workItemId)
    {
        _jobsCache.TryGetValue(workItemId, out var job);
        return job;
    }

    public List<CreationJobStatusInfo> GetJobsByProject(string projectId)
    {
        return _jobsCache.Values
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.LastUpdated)
            .ToList();
    }

    public List<CreationJobStatusInfo> GetAllJobs()
    {
        return _jobsCache.Values
            .OrderByDescending(j => j.LastUpdated)
            .ToList();
    }

    public List<CreationJobStatusInfo> GetFailedJobs()
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

                _logger.LogInformation("Saved {Count} creation jobs to {Path} (AccessToken excluded)",
                    _jobsCache.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving creation jobs to file");
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No existing creation jobs file found at {Path}", _filePath);
                return;
            }

            lock (_fileLock)
            {
                var json = File.ReadAllText(_filePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var loadedData = JsonSerializer.Deserialize<List<CreationJobStatusInfo>>(json, options);

                if (loadedData != null)
                {
                    foreach (var item in loadedData)
                    {
                        _jobsCache.TryAdd(item.WorkItemId, item);
                    }

                    _logger.LogInformation("Loaded {Count} creation jobs from {Path}", loadedData.Count, _filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading creation jobs from file");
        }
    }
}

public class CreationJobStatusInfo
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
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public int CompletedModels { get; set; }
    public int FailedModels { get; set; }
}
