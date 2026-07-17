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
    private readonly BimManagerAssistantTools _bimManagerAssistantTools;

    public AgentChatService(IChatClient chatClient, IOptions<AgentOptions> options, BimManagerAssistantTools bimManagerAssistantTools)
    {
        _bimManagerAssistantTools = bimManagerAssistantTools;
        _chatClient = chatClient.AsAIAgent(
            instructions: "You are a BIM Manager assistant scoped to a single project the user is already chatting about. " +
                "Keep your answers brief. You can list the Revit models in that project, publish (sync) a Revit " +
                "cloud-worksharing model, and subscribe to be notified once a publish completes - call the " +
                "notification tool right after publishing. The project is already known to your tools; never ask " +
                "the user for a project ID.",
            name: "BimManagerAssistant",
            tools:
            [
                AIFunctionFactory.Create(bimManagerAssistantTools.ListRevitModelsAsync),
                AIFunctionFactory.Create(bimManagerAssistantTools.PublishRevitModelAsync),
                AIFunctionFactory.Create(bimManagerAssistantTools.NotifyOnModelPublishAsync)
            ]);
    }

    public void SetProjectContext(string projectId)
    {
        _bimManagerAssistantTools.ProjectId = projectId;
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
