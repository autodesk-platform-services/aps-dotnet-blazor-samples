using ApsSamples.Models;
using ApsSamples.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace ApsSamples.Services;

public class AgentChatService : IAgentChatService
{
    private readonly AIAgent _chatClient;

    public AgentChatService(IChatClient chatClient, IOptions<AgentOptions> options, BimManagerAssistantTools bimManagerAssistantTools)
    {
        _chatClient = chatClient.AsAIAgent(
            instructions: "You are a BIM Manager assistant. Keep your answers brief. " +
                "You can list the Revit models in a project and publish (sync) a Revit cloud-worksharing model.",
            name: "BimManagerAssistant",
            tools:
            [
                AIFunctionFactory.Create(bimManagerAssistantTools.ListRevitModelsAsync),
                AIFunctionFactory.Create(bimManagerAssistantTools.PublishRevitModelAsync)
            ]);
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
            IList<ConversationMessage> history,
            [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = history.Select(Map).ToList();

        await foreach (var update in _chatClient.RunStreamingAsync(messages, cancellationToken: ct))
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
