using System.Collections.Concurrent;
using System.Text.Json;
using ApsSamples.Models;

namespace ApsSamples.Services;

public class AgentRegistryService : IAgentRegistryService
{
    private readonly ConcurrentDictionary<string, AgentDefinition> _cache = new();
    private readonly ILogger<AgentRegistryService> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public AgentRegistryService(ILogger<AgentRegistryService> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "agents.json");

        LoadFromFile();
    }

    public async Task<AgentDefinition> CreateAgentAsync(AgentDefinition agent)
    {
        if (string.IsNullOrWhiteSpace(agent.AgentId))
        {
            agent.AgentId = Guid.NewGuid().ToString();
        }

        _cache[agent.AgentId] = agent;
        await SaveToFileAsync();

        _logger.LogInformation("Created agent {AgentId} ({AgentName}) for project {ProjectId}",
            agent.AgentId, agent.AgentName, agent.ProjectId);

        return agent;
    }

    public Task<IReadOnlyList<AgentDefinition>> GetAgentsByProjectAsync(string projectId)
    {
        IReadOnlyList<AgentDefinition> result = _cache.Values
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<AgentDefinition?> GetAgentAsync(string agentId)
    {
        _cache.TryGetValue(agentId, out var agent);
        return Task.FromResult(agent);
    }

    public async Task UpdateAgentAsync(AgentDefinition agent)
    {
        if (!_cache.ContainsKey(agent.AgentId))
        {
            _logger.LogWarning("Agent {AgentId} not found for UpdateAgentAsync", agent.AgentId);
            return;
        }

        _cache[agent.AgentId] = agent;
        await SaveToFileAsync();
    }

    public async Task DeleteAgentAsync(string agentId)
    {
        if (!_cache.TryRemove(agentId, out _))
        {
            _logger.LogWarning("Agent {AgentId} not found for DeleteAgentAsync", agentId);
            return;
        }

        await SaveToFileAsync();
        _logger.LogInformation("Deleted agent {AgentId}", agentId);
    }

    public int GetAgentCount() => _cache.Count;

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

            var json = JsonSerializer.Serialize(_cache.Values.ToList(), options);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving agents to file");
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
                _logger.LogInformation("No existing agents file found at {Path}", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var loaded = JsonSerializer.Deserialize<List<AgentDefinition>>(json, options);
            if (loaded != null)
            {
                foreach (var item in loaded)
                {
                    _cache.TryAdd(item.AgentId, item);
                }
                _logger.LogInformation("Loaded {Count} agents from {Path}", loaded.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading agents from file");
        }
    }
}
