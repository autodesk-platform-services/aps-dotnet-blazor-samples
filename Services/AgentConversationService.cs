using System.Collections.Concurrent;
using System.Text.Json;
using ApsSamples.Models;

namespace ApsSamples.Services;

public class AgentConversationService : IAgentConversationService
{
    private readonly ConcurrentDictionary<string, ConversationSession> _conversationsCache = new();
    private readonly ILogger<AgentConversationService> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public AgentConversationService(ILogger<AgentConversationService> logger)
    {
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "agent-conversations.json");

        LoadFromFile();
    }

    public async Task<ConversationSession> CreateConversationAsync(string projectId, string userId)
    {
        var now = DateTime.UtcNow;
        var session = new ConversationSession
        {
            ConversationId = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            UserId = userId,
            CreatedAt = now,
            LastActivityAt = now,
            Messages = new List<ConversationMessage>()
        };

        _conversationsCache[session.ConversationId] = session;
        await SaveToFileAsync();

        _logger.LogInformation("Created conversation {ConversationId} for project {ProjectId} user {UserId}",
            session.ConversationId, projectId, userId);

        return session;
    }

    public Task<IReadOnlyList<ConversationSession>> GetConversationsAsync(string projectId, string userId)
    {
        IReadOnlyList<ConversationSession> result = _conversationsCache.Values
            .Where(c => c.ProjectId == projectId && c.UserId == userId)
            .OrderByDescending(c => c.LastActivityAt)
            .ToList();

        return Task.FromResult(result);
    }

    public async Task AddMessageAsync(string conversationId, ConversationMessage message)
    {
        if (!_conversationsCache.TryGetValue(conversationId, out var session))
        {
            _logger.LogWarning("Conversation {ConversationId} not found for AddMessageAsync", conversationId);
            return;
        }

        // Callers (e.g. Chat.razor) append to the cached session's Messages list directly for
        // immediate UI feedback before persisting, since callers hand out the same in-memory
        // instance. Guard against adding the same message twice.
        if (!session.Messages.Contains(message))
        {
            session.Messages.Add(message);
        }

        session.LastActivityAt = message.Timestamp;
        await SaveToFileAsync();
    }

    public async Task ClearMessagesAsync(string conversationId)
    {
        if (!_conversationsCache.TryGetValue(conversationId, out var session))
        {
            _logger.LogWarning("Conversation {ConversationId} not found for ClearMessagesAsync", conversationId);
            return;
        }

        session.Messages.Clear();
        session.SerializedAgentSession = null;
        session.LastActivityAt = DateTime.UtcNow;
        await SaveToFileAsync();
    }

    public Task<ConversationSession?> GetConversationAsync(string conversationId)
    {
        _conversationsCache.TryGetValue(conversationId, out var session);
        return Task.FromResult(session);
    }

    public Task<JsonElement?> GetSerializedSessionAsync(string conversationId)
    {
        if (!_conversationsCache.TryGetValue(conversationId, out var session))
        {
            return Task.FromResult<JsonElement?>(null);
        }

        return Task.FromResult(session.SerializedAgentSession);
    }

    public async Task SaveSerializedSessionAsync(string conversationId, JsonElement serializedSession)
    {
        if (!_conversationsCache.TryGetValue(conversationId, out var session))
        {
            _logger.LogWarning("Conversation {ConversationId} not found for SaveSerializedSessionAsync", conversationId);
            return;
        }

        session.SerializedAgentSession = serializedSession;
        await SaveToFileAsync();
    }

    public async Task ClearSerializedSessionAsync(string conversationId)
    {
        if (!_conversationsCache.TryGetValue(conversationId, out var session))
        {
            _logger.LogWarning("Conversation {ConversationId} not found for ClearSerializedSessionAsync", conversationId);
            return;
        }

        session.SerializedAgentSession = null;
        await SaveToFileAsync();
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

            var json = JsonSerializer.Serialize(_conversationsCache.Values.ToList(), options);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving agent conversations to file");
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
                _logger.LogInformation("No existing agent conversations file found at {Path}", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var loaded = JsonSerializer.Deserialize<List<ConversationSession>>(json, options);
            if (loaded != null)
            {
                foreach (var item in loaded)
                {
                    _conversationsCache.TryAdd(item.ConversationId, item);
                }
                _logger.LogInformation("Loaded {Count} agent conversations from {Path}", loaded.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading agent conversations from file");
        }
    }
}
