using ApsSamples.Models;
using ApsSamples.Services;
using ApsSamples.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class AgentChatServiceTests : IClassFixture<TempWorkingDirectoryFixture>
{
    public AgentChatServiceTests(TempWorkingDirectoryFixture _) { }

    private static AgentChatService NewService(IAgentConversationService conversationService) =>
        new(
            chatClient: null!,
            agentRegistry: new FakeAgentRegistryService(),
            toolCatalog: null!,
            toolContext: new AgentToolContext(null!, null!),
            services: null!,
            conversationService: conversationService);

    private sealed class FakeAgentRegistryService : IAgentRegistryService
    {
        public Task<AgentDefinition> CreateAgentAsync(AgentDefinition agent) => Task.FromResult(agent);
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsByProjectAsync(string projectId) =>
            Task.FromResult<IReadOnlyList<AgentDefinition>>(Array.Empty<AgentDefinition>());
        public Task<AgentDefinition?> GetAgentAsync(string agentId) => Task.FromResult<AgentDefinition?>(null);
        public Task UpdateAgentAsync(AgentDefinition agent) => Task.CompletedTask;
        public Task DeleteAgentAsync(string agentId) => Task.CompletedTask;
        public int GetAgentCount() => 0;
    }

    [Fact]
    public async Task StreamResponseAsync_ThrowsIfAssistantNotFound()
    {
        var conversationService = new AgentConversationService(NullLogger<AgentConversationService>.Instance);
        var conversation = await conversationService.CreateConversationAsync("proj", "user", "agent");
        var chat = NewService(conversationService);
        chat.SetConversationContext(conversation.ConversationId);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in chat.StreamResponseAsync("agent", "hello"))
            {
            }
        });
    }

    [Fact]
    public async Task StreamResponseAsync_ThrowsIfConversationContextNotSet()
    {
        var conversationService = new AgentConversationService(NullLogger<AgentConversationService>.Instance);
        var chat = NewService(conversationService);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in chat.StreamResponseAsync("agent", "hello"))
            {
            }
        });
    }
}
