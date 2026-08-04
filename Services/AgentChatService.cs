using ApsSamples.Models;
using ApsSamples.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace ApsSamples.Services;

public class AgentChatService : IAgentChatService
{
    private readonly IChatClient _chatClient;
    private readonly IAgentRegistryService _agentRegistry;
    private readonly IToolCatalogService _toolCatalog;
    private readonly BimManagerAssistantTools _bimManagerAssistantTools;
    private AIAgent? _agent;

    public AgentChatService(
        IChatClient chatClient,
        IAgentRegistryService agentRegistry,
        IToolCatalogService toolCatalog,
        BimManagerAssistantTools bimManagerAssistantTools)
    {
        _chatClient = chatClient;
        _agentRegistry = agentRegistry;
        _toolCatalog = toolCatalog;
        _bimManagerAssistantTools = bimManagerAssistantTools;
    }

    public void SetHubContext(string hubId)
    {
        _bimManagerAssistantTools.HubId = hubId;
    }

    public void SetProjectContext(string projectId)
    {
        _bimManagerAssistantTools.ProjectId = projectId;
    }

    public void SetConversationContext(string conversationId)
    {
        _bimManagerAssistantTools.ConversationId = conversationId;
    }

    public async Task SetAgentContextAsync(string agentId)
    {
        var def = await _agentRegistry.GetAgentAsync(agentId)
            ?? throw new InvalidOperationException($"Agent '{agentId}' not found.");

        _bimManagerAssistantTools.AgentId = agentId;

        _agent = _chatClient.AsAIAgent(
            instructions: def.InstructionsText,
            name: def.AgentName,
            tools: _toolCatalog.GetAIFunctions(def.EnabledToolIds, _bimManagerAssistantTools).Cast<AITool>().ToArray());
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
            IList<ConversationMessage> history,
            [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_agent is null)
        {
            throw new InvalidOperationException("Agent context not set. Call SetAgentContextAsync before streaming.");
        }

        var messages = history.Select(Map).ToList();

        await foreach (var update in _agent.RunStreamingAsync(messages, cancellationToken: ct))
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
