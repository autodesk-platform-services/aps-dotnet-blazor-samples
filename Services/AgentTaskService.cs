using System.Collections.Concurrent;
using System.Text.Json;
using ApsSamples.Models;

namespace ApsSamples.Services;

public class AgentTaskService : IAgentTaskService
{
    // If a task is still waiting on its webhook after this long, something went wrong upstream
    // (the publish failed silently, the webhook never fired, etc.) - stop waiting and show it as
    // failed instead of leaving it "in progress" forever.
    private static readonly TimeSpan WebhookTimeout = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, AgentTaskInfo> _tasksCache = new();
    private readonly ConcurrentDictionary<string, byte> _scheduledTimeouts = new();
    private readonly IAgentConversationService _conversationService;
    private readonly ILogger<AgentTaskService> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public AgentTaskService(IAgentConversationService conversationService, ILogger<AgentTaskService> logger)
    {
        _conversationService = conversationService;
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "agent-tasks.json");

        LoadFromFile();
    }

    public async Task<AgentTaskInfo> CreateTaskAsync(string conversationId, string projectId, string name)
    {
        var task = new AgentTaskInfo
        {
            TaskId = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            ProjectId = projectId,
            Name = name,
            Status = AgentTaskStatus.Pending,
            StartedAt = DateTime.UtcNow
        };

        _tasksCache[task.TaskId] = task;
        await SaveToFileAsync();

        _logger.LogInformation("Created agent task {TaskId} for conversation {ConversationId}: {Name}",
            task.TaskId, conversationId, name);

        return task;
    }

    public async Task UpdateTaskAsync(AgentTaskInfo task)
    {
        _tasksCache[task.TaskId] = task;
        await SaveToFileAsync();

        _logger.LogInformation("Updated agent task {TaskId}: {Status}", task.TaskId, task.Status);

        if (task.Status == AgentTaskStatus.Running && !string.IsNullOrEmpty(task.WebhookHookId) &&
            _scheduledTimeouts.TryAdd(task.TaskId, 0))
        {
            ScheduleWebhookTimeout(task.TaskId);
        }
    }

    private void ScheduleWebhookTimeout(string taskId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(WebhookTimeout);

                if (!_tasksCache.TryGetValue(taskId, out var task) || task.Status != AgentTaskStatus.Running)
                {
                    return;
                }

                task.Status = AgentTaskStatus.Failed;
                task.CompletedAt = DateTime.UtcNow;
                task.ErrorDetail = "Timed out waiting for the publish to complete.";
                await UpdateTaskAsync(task);

                _logger.LogWarning("Task {TaskId} timed out waiting for its webhook", taskId);

                await _conversationService.AddMessageAsync(task.ConversationId, new ConversationMessage
                {
                    Role = "assistant",
                    Content = $"⚠️ **{task.Name}** — timed out waiting for the publish to complete.",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in publish webhook timeout for task {TaskId}", taskId);
            }
        });
    }

    public Task<IReadOnlyList<AgentTaskInfo>> GetTasksByConversationAsync(string conversationId)
    {
        IReadOnlyList<AgentTaskInfo> result = _tasksCache.Values
            .Where(t => t.ConversationId == conversationId)
            .OrderBy(t => t.StartedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<AgentTaskInfo>> GetTasksByProjectAsync(string projectId)
    {
        IReadOnlyList<AgentTaskInfo> result = _tasksCache.Values
            .Where(t => t.ProjectId == projectId)
            .OrderByDescending(t => t.StartedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<AgentTaskInfo?> GetTaskByWebhookHookIdAsync(string hookId)
    {
        var task = _tasksCache.Values.FirstOrDefault(t => t.WebhookHookId == hookId);
        return Task.FromResult(task);
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

            var json = JsonSerializer.Serialize(_tasksCache.Values.ToList(), options);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving agent tasks to file");
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
                _logger.LogInformation("No existing agent tasks file found at {Path}", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var loaded = JsonSerializer.Deserialize<List<AgentTaskInfo>>(json, options);
            if (loaded != null)
            {
                foreach (var item in loaded)
                {
                    _tasksCache.TryAdd(item.TaskId, item);
                }
                _logger.LogInformation("Loaded {Count} agent tasks from {Path}", loaded.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading agent tasks from file");
        }
    }
}
