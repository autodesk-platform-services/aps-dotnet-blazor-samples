using ApsSamples.Services;
using ApsSamples.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class AgentChatServiceTests : IClassFixture<TempWorkingDirectoryFixture>
{
    public AgentChatServiceTests(TempWorkingDirectoryFixture _) { }

    private static BimManagerAssistantTools NewTools() =>
        new(null!, null!, null!, null!, null!, null!, null!);

    private static AgentChatService NewService(IAgentConversationService conversationService) =>
        new(
            chatClient: null!,
            agentRegistry: null!,
            toolCatalog: null!,
            bimManagerAssistantTools: NewTools(),
            conversationService: conversationService);

    [Fact]
    public async Task StreamResponseAsync_ThrowsIfAgentNotSet()
    {
        var conversationService = new AgentConversationService(NullLogger<AgentConversationService>.Instance);
        var conversation = await conversationService.CreateConversationAsync("proj", "user", "agent");
        var chat = NewService(conversationService);
        chat.SetConversationContext(conversation.ConversationId);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in chat.StreamResponseAsync("hello"))
            {
            }
        });
    }
}
