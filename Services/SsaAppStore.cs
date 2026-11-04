using System.Collections.Concurrent;
using System.Text.Json;
using ApsSamples.Models;

namespace ApsSamples.Services;

// Persists each project's SSA apps to Data/ssa-apps.json, like RevitWebhookStore. Secrets are
// stored in plaintext, like ssa-keys.json.
public class SsaAppStore : ISsaAppStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<string, SsaAppCredential> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SsaAppStore> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SsaAppStore(ILogger<SsaAppStore> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "ssa-apps.json");

        LoadFromFile();
    }

    public IReadOnlyList<SsaAppCredential> GetByProject(string projectId)
    {
        return _apps.Values
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.CreatedAt)
            .ToList();
    }

    public SsaAppCredential? Get(string clientId)
    {
        return _apps.TryGetValue(clientId, out var app) ? app : null;
    }

    public async Task<bool> TryAddAsync(SsaAppCredential app)
    {
        ArgumentException.ThrowIfNullOrEmpty(app.ClientId);

        if (!_apps.TryAdd(app.ClientId, app))
        {
            return false;
        }

        await SaveAsync();
        return true;
    }

    public async Task<bool> UpdateSecretAsync(string clientId, string newSecret)
    {
        if (!_apps.TryGetValue(clientId, out var app))
        {
            return false;
        }

        app.ClientSecret = newSecret;
        await SaveAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(string clientId)
    {
        if (!_apps.TryRemove(clientId, out _))
        {
            return false;
        }

        await SaveAsync();
        return true;
    }

    private async Task SaveAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_apps.Values.ToList(), WriteOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving SSA apps to file");
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
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<List<SsaAppCredential>>(json, ReadOptions);
                foreach (var item in loaded?.Where(a => !string.IsNullOrEmpty(a.ClientId)) ?? [])
                {
                    _apps[item.ClientId] = item;
                }
                _logger.LogInformation("Loaded {Count} SSA apps from {Path}", _apps.Count, _filePath);
            }
            else
            {
                _logger.LogInformation("No existing SSA apps file found at {Path}", _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading SSA apps from file");
        }
    }
}
