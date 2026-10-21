using System.Collections.Concurrent;
using System.Text.Json;
using ApsSamples.Models;

namespace ApsSamples.Services;

// Persists Revit webhook subscriptions and their event/delivery log to Data/*.json, like
// AgentTaskService. Subscriptions are shared mutable objects: callers change them and then call
// UpsertAsync to persist.
public class RevitWebhookStore : IRevitWebhookStore
{
    public const int MaxEvents = 200;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<string, RevitWebhookSubscription> _subscriptions = new();
    // Newest last; trimmed from the front once it exceeds MaxEvents.
    private readonly List<RevitWebhookEvent> _events = new();
    private readonly object _eventsLock = new();
    private readonly ILogger<RevitWebhookStore> _logger;
    private readonly string _subscriptionsFilePath;
    private readonly string _eventsFilePath;
    private readonly SemaphoreSlim _subscriptionsFileLock = new(1, 1);
    private readonly SemaphoreSlim _eventsFileLock = new(1, 1);

    public RevitWebhookStore(ILogger<RevitWebhookStore> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _subscriptionsFilePath = Path.Combine(dataDirectory, "revit-webhook-subscriptions.json");
        _eventsFilePath = Path.Combine(dataDirectory, "revit-webhook-events.json");

        LoadFromFiles();
    }

    public IReadOnlyList<RevitWebhookSubscription> GetSubscriptions(string? projectId = null)
    {
        return _subscriptions.Values
            .Where(s => projectId == null || s.ProjectId == projectId)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    public IReadOnlyList<RevitWebhookSubscription> GetByHookId(string hookId)
    {
        return _subscriptions.Values
            .Where(s => string.Equals(s.HookId, hookId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public RevitWebhookSubscription? Get(string id)
    {
        return _subscriptions.TryGetValue(id, out var subscription) ? subscription : null;
    }

    public async Task UpsertAsync(RevitWebhookSubscription subscription)
    {
        ArgumentException.ThrowIfNullOrEmpty(subscription.Id);

        _subscriptions[subscription.Id] = subscription;
        await SaveSubscriptionsAsync();
    }

    public async Task<bool> RemoveAsync(string id)
    {
        if (!_subscriptions.TryRemove(id, out _))
        {
            return false;
        }

        await SaveSubscriptionsAsync();
        return true;
    }

    public async Task AddEventAsync(RevitWebhookEvent webhookEvent)
    {
        lock (_eventsLock)
        {
            _events.Add(webhookEvent);
            if (_events.Count > MaxEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }

        await SaveEventsAsync();
    }

    public IReadOnlyList<RevitWebhookEvent> GetEvents(string? subscriptionId = null)
    {
        lock (_eventsLock)
        {
            return _events
                .Where(e => subscriptionId == null || e.SubscriptionId == subscriptionId)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }

    private async Task SaveSubscriptionsAsync()
    {
        await _subscriptionsFileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_subscriptions.Values.ToList(), WriteOptions);
            await File.WriteAllTextAsync(_subscriptionsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Revit webhook subscriptions to file");
        }
        finally
        {
            _subscriptionsFileLock.Release();
        }
    }

    private async Task SaveEventsAsync()
    {
        await _eventsFileLock.WaitAsync();
        try
        {
            List<RevitWebhookEvent> snapshot;
            lock (_eventsLock)
            {
                snapshot = _events.ToList();
            }

            var json = JsonSerializer.Serialize(snapshot, WriteOptions);
            await File.WriteAllTextAsync(_eventsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Revit webhook events to file");
        }
        finally
        {
            _eventsFileLock.Release();
        }
    }

    private void LoadFromFiles()
    {
        try
        {
            if (File.Exists(_subscriptionsFilePath))
            {
                var json = File.ReadAllText(_subscriptionsFilePath);
                var loaded = JsonSerializer.Deserialize<List<RevitWebhookSubscription>>(json, ReadOptions);
                foreach (var item in loaded?.Where(s => !string.IsNullOrEmpty(s.Id)) ?? [])
                {
                    _subscriptions[item.Id] = item;
                }
                _logger.LogInformation("Loaded {Count} Revit webhook subscriptions from {Path}",
                    _subscriptions.Count, _subscriptionsFilePath);
            }
            else
            {
                _logger.LogInformation("No existing Revit webhook subscriptions file found at {Path}", _subscriptionsFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Revit webhook subscriptions from file");
        }

        try
        {
            if (File.Exists(_eventsFilePath))
            {
                var json = File.ReadAllText(_eventsFilePath);
                var loaded = JsonSerializer.Deserialize<List<RevitWebhookEvent>>(json, ReadOptions);
                if (loaded != null)
                {
                    _events.AddRange(loaded.OrderBy(e => e.Timestamp).TakeLast(MaxEvents));
                }
                _logger.LogInformation("Loaded {Count} Revit webhook events from {Path}", _events.Count, _eventsFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Revit webhook events from file");
        }
    }
}
