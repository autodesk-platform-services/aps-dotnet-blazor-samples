using System.Runtime.CompilerServices;
using ApsSamples.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ApsSamples.Services;

public class AgentChatService : IAgentChatService
{
    private readonly IChatClient _chatClient;

    public AgentChatService(IChatClient chatClient, IOptions<AgentOptions> options)
    {
        _chatClient = chatClient;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        IList<ConversationMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = history.Select(Map).ToList();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static ChatMessage Map(ConversationMessage message)
    {
        var role = message.Role?.ToLowerInvariant() switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };
        return new ChatMessage(role, message.Content ?? string.Empty);
    }
}
