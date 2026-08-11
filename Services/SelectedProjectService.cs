using System.Collections.Concurrent;
using System.Text.Json;

namespace ApsSamples.Services;

public class SelectedProjectService : ISelectedProjectService
{
    private readonly ConcurrentDictionary<string, SelectedProjectRecord> _projects = new();
    private readonly ILogger<SelectedProjectService> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SelectedProjectService(ILogger<SelectedProjectService> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "selected-projects.json");

        LoadFromFile();
    }

    public Task<SelectedProjectRecord?> GetSelectedProjectAsync(string userId)
    {
        _projects.TryGetValue(userId, out var record);
        return Task.FromResult<SelectedProjectRecord?>(record);
    }

    public async Task SetSelectedProjectAsync(string userId, SelectedProjectRecord record)
    {
        _projects[userId] = record;
        await SaveToFileAsync();

        _logger.LogInformation("Set selected project {ProjectId} for user {UserId}", record.ProjectId, userId);
    }

    private async Task SaveToFileAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var snapshot = _projects.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var json = JsonSerializer.Serialize(snapshot, options);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving selected projects to file");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No existing selected projects file found at {Path}", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var loaded = JsonSerializer.Deserialize<Dictionary<string, SelectedProjectRecord>>(json, options);
            if (loaded != null)
            {
                foreach (var kvp in loaded)
                {
                    _projects.TryAdd(kvp.Key, kvp.Value);
                }
                _logger.LogInformation("Loaded {Count} selected projects from {Path}", loaded.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading selected projects from file");
        }
    }
}
