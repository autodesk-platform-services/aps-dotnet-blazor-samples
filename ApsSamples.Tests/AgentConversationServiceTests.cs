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

        var first = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, factory);
        var second = await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, factory);

        Assert.Same(first, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task ClearMessagesAsync_NullsSerializedAgentSession()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent");
        var serialized = System.Text.Json.JsonDocument.Parse("{\"foo\":1}").RootElement;
        await svc.SaveSerializedSessionAsync(conversation.ConversationId, serialized);

        var before = await svc.GetSerializedSessionAsync(conversation.ConversationId);
        Assert.NotNull(before);

        await svc.ClearMessagesAsync(conversation.ConversationId);

        var after = await svc.GetSerializedSessionAsync(conversation.ConversationId);
        Assert.Null(after);
    }

    [Fact]
    public async Task ClearMessagesAsync_RemovesAgentSession()
    {
        var svc = NewService();
        var conversation = await svc.CreateConversationAsync("proj", "user", "agent");
        var factoryCalls = 0;
        Func<CancellationToken, Task<AgentSession>> factory = _ =>
        {
            factoryCalls++;
            return Task.FromResult(NewSession());
        };

        await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, factory);
        Assert.Equal(1, factoryCalls);

        await svc.ClearMessagesAsync(conversation.ConversationId);

        await svc.GetOrLoadAgentSessionAsync(conversation.ConversationId, factory);
        Assert.Equal(2, factoryCalls);
    }
}
