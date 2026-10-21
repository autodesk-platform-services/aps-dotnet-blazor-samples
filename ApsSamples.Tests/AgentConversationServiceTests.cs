using System.Text.Json;
using ApsSamples.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class AgentConversationServiceTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private readonly TempWorkingDirectoryFixture _fixture;

    public AgentConversationServiceTests(TempWorkingDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private static AgentConversationService NewService() =>
        new(NullLogger<AgentConversationService>.Instance);

    private static AgentSession NewSession() => new FakeAgentSession();

    private sealed class FakeAgentSession : AgentSession { }

    [Fact]
    public async Task GetOrLoadAgentSessionAsync_CacheMiss_CallsFactory()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent");
        var factoryCalls = 0;

        var session = await svc.GetOrLoadAgentSessionAsync(
            conversation.ConversationId,
            "agent",
            _ => { factoryCalls++; return Task.FromResult(NewSession()); });

        Assert.NotNull(session);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrLoadAgentSessionAsync_CacheHit_DoesNotCallFactory()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent");
        var factoryCalls = 0;
        Func<CancellationToken, Task<AgentSession>> factory = _ =>
        {
            factoryCalls++;
            return Task.FromResult(NewSession());
        };

        var first = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent", factory);
        var second = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent", factory);

        Assert.Same(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrLoadAgentSessionAsync_DifferentAgents_GetDistinctSessions()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent-a");
        var factoryCalls = 0;
        Func<CancellationToken, Task<AgentSession>> factory = _ =>
        {
            factoryCalls++;
            return Task.FromResult(NewSession());
        };

        var a1 = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-a", factory);
        var b1 = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-b", factory);
        var a2 = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-a", factory);
        var b2 = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-b", factory);

        Assert.NotSame(a1, b1);
        Assert.Same(a1, a2);
        Assert.Same(b1, b2);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetConversationsAsync_ReturnsAllAgentsForUserAndProject()
    {
        var svc = NewService();
        var projectId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        var first = await svc.CreateConversationAsync(projectId, userId, "agent-a");
        var second = await svc.CreateConversationAsync(projectId, userId, "agent-b");
        await svc.CreateConversationAsync(projectId, "other-user", "agent-a");
        await svc.CreateConversationAsync("other-project", userId, "agent-a");
        second.LastActivityAt = first.LastActivityAt.AddMinutes(1);

        var result = await svc.GetConversationsAsync(projectId, userId);

        Assert.Equal(
            new[] { second.ConversationId, first.ConversationId },
            result.Select(c => c.ConversationId).ToArray());
    }

    [Fact]
    public async Task SaveSerializedSessionAsync_IsStoredPerAgent()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent-a");
        var serialized = JsonDocument.Parse("{\"foo\":1}").RootElement;

        await svc.SaveSerializedSessionAsync(conversation.ConversationId, "agent-a", serialized);

        Assert.NotNull(await svc.GetSerializedSessionAsync(conversation.ConversationId, "agent-a"));
        Assert.Null(await svc.GetSerializedSessionAsync(conversation.ConversationId, "agent-b"));
    }

    [Fact]
    public async Task ClearMessagesAsync_ClearsAllAgentsSerializedSessions()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent-a");
        var serialized = JsonDocument.Parse("{\"foo\":1}").RootElement;
        await svc.SaveSerializedSessionAsync(conversation.ConversationId, "agent-a", serialized);
        await svc.SaveSerializedSessionAsync(conversation.ConversationId, "agent-b", serialized);

        Assert.NotNull(await svc.GetSerializedSessionAsync(conversation.ConversationId, "agent-a"));
        Assert.NotNull(await svc.GetSerializedSessionAsync(conversation.ConversationId, "agent-b"));

        await svc.ClearMessagesAsync(conversation.ConversationId);

        Assert.Null(await svc.GetSerializedSessionAsync(conversation.ConversationId, "agent-a"));
        Assert.Null(await svc.GetSerializedSessionAsync(conversation.ConversationId, "agent-b"));
    }

    [Fact]
    public async Task ClearMessagesAsync_RemovesAllAgentsCachedSessions()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent-a");
        var factoryCalls = 0;
        Func<CancellationToken, Task<AgentSession>> factory = _ =>
        {
            factoryCalls++;
            return Task.FromResult(NewSession());
        };

        await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-a", factory);
        await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-b", factory);
        Assert.Equal(2, factoryCalls);

        await svc.ClearMessagesAsync(conversation.ConversationId);

        await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-a", factory);
        await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, "agent-b", factory);
        Assert.Equal(4, factoryCalls);
    }

    [Fact]
    public async Task LoadFromFile_MigratesLegacySerializedAgentSession()
    {
        var conversationId = Guid.NewGuid().ToString();
        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        var json = $$"""
            [
              {
                "conversationId": "{{conversationId}}",
                "projectId": "proj",
                "userId": "user",
                "agentId": "legacy-agent",
                "messages": [],
                "serializedAgentSession": { "foo": 1 }
              }
            ]
            """;
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "agent-conversations.json"), json);

        var svc = NewService();

        var migrated = await svc.GetSerializedSessionAsync(conversationId, "legacy-agent");
        Assert.NotNull(migrated);
        Assert.Equal(1, migrated.Value.GetProperty("foo").GetInt32());

        var conversation = await svc.GetConversationAsync(conversationId);
        Assert.NotNull(conversation);
        Assert.Null(conversation.SerializedAgentSession);
    }
}
