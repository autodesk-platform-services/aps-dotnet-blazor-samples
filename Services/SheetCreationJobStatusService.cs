using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApsSamples.Services;

public interface ISheetCreationJobStatusService
{
    void StoreSheetCreationJobDetails(string workItemId, string projectId, string projectName, string userName, string modelName, List<SheetCreationDetail> sheets, string? titleBlockName);
    void UpdateStatus(string workItemId, string status, string? reportUrl = null);
    SheetCreationJobStatusInfo? GetStatus(string workItemId);
    List<SheetCreationJobStatusInfo> GetJobsByProject(string projectId);
    List<SheetCreationJobStatusInfo> GetAllJobs();
    List<SheetCreationJobStatusInfo> GetFailedJobs();
}

public class SheetCreationJobStatusService : ISheetCreationJobStatusService
{
    private readonly ConcurrentDictionary<string, SheetCreationJobStatusInfo> _jobsCache = new();
    private readonly ILogger<SheetCreationJobStatusService> _logger;
    private readonly string _filePath;
    private readonly object _fileLock = new();

    public SheetCreationJobStatusService(ILogger<SheetCreationJobStatusService> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "sheet-creation-jobs.json");

        LoadFromFile();
    }

    public void StoreSheetCreationJobDetails(string workItemId, string projectId, string projectName, string userName, string modelName, List<SheetCreationDetail> sheets, string? titleBlockName)
    {
        _jobsCache.AddOrUpdate(workItemId,
            key => new SheetCreationJobStatusInfo
            {
                WorkItemId = workItemId,
                ProjectId = projectId,
                ProjectName = projectName,
                UserName = userName,
                ModelName = modelName,
                Sheets = sheets,
                TitleBlockName = titleBlockName,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                SubmittedAt = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.ProjectName = projectName;
                existing.UserName = userName;
                existing.ModelName = modelName;
                existing.Sheets = sheets;
                existing.TitleBlockName = titleBlockName;
                return existing;
            });

        _logger.LogInformation("Stored sheet creation job details for WorkItem {WorkItemId}: {SheetCount} sheets in {ModelName}",
            workItemId, sheets.Count, modelName);

        SaveToFile();
    }

    public void UpdateStatus(string workItemId, string status, string? reportUrl = null)
    {
        if (_jobsCache.TryGetValue(workItemId, out var job))
        {
            job.Status = status;
            job.LastUpdated = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(reportUrl))
                job.ReportUrl = reportUrl;

            _logger.LogInformation("Updated status for Sheet Creation WorkItem {WorkItemId}: {Status}", workItemId, status);
            SaveToFile();
        }
    }

    public SheetCreationJobStatusInfo? GetStatus(string workItemId)
    {
        _jobsCache.TryGetValue(workItemId, out var job);
        return job;
    }

    public List<SheetCreationJobStatusInfo> GetJobsByProject(string projectId)
    {
        return _jobsCache.Values
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.LastUpdated)
            .ToList();
    }

    public List<SheetCreationJobStatusInfo> GetAllJobs()
    {
        return _jobsCache.Values
            .OrderByDescending(j => j.LastUpdated)
            .ToList();
    }

    public List<SheetCreationJobStatusInfo> GetFailedJobs()
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

                _logger.LogInformation("Saved {Count} sheet creation jobs to {Path}", _jobsCache.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving sheet creation jobs to file");
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No existing sheet creation jobs file found at {Path}", _filePath);
                return;
            }

            lock (_fileLock)
            {
                var json = File.ReadAllText(_filePath);

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var loadedData = JsonSerializer.Deserialize<List<SheetCreationJobStatusInfo>>(json, options);

                if (loadedData != null)
                {
                    foreach (var item in loadedData)
                    {
                        _jobsCache.TryAdd(item.WorkItemId, item);
                    }

                    _logger.LogInformation("Loaded {Count} sheet creation jobs from {Path}", loadedData.Count, _filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading sheet creation jobs from file");
        }
    }
}

public class SheetCreationJobStatusInfo
{
    public string WorkItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ReportUrl { get; set; }
    public string? ModelName { get; set; }
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? UserName { get; set; }
    public string? TitleBlockName { get; set; }
    public List<SheetCreationDetail> Sheets { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class SheetCreationDetail
{
    public string SheetNumber { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
}
