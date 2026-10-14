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
    private readonly IAgentConversationService _conversationService;
    private AIAgent? _agent;
    private string? _currentConversationId;

    public AgentChatService(
        IChatClient chatClient,
        IAgentRegistryService agentRegistry,
        IToolCatalogService toolCatalog,
        BimManagerAssistantTools bimManagerAssistantTools,
        IAgentConversationService conversationService)
    {
        _chatClient = chatClient;
        _agentRegistry = agentRegistry;
        _toolCatalog = toolCatalog;
        _bimManagerAssistantTools = bimManagerAssistantTools;
        _conversationService = conversationService;
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
        _currentConversationId = conversationId;
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
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_currentConversationId))
        {
            throw new InvalidOperationException("SetConversationContext must be called before StreamResponseAsync.");
        }

        if (_agent is null)
        {
            throw new InvalidOperationException("SetAgentContextAsync must be called before StreamResponseAsync.");
        }

        var conversationId = _currentConversationId;
        var session = await _conversationService.GetOrLoadAgentSessionAsync(
            conversationId,
            async innerCt =>
            {
                var persisted = await _conversationService.GetSerializedSessionAsync(conversationId);
                return persisted.HasValue
                    ? await _agent!.DeserializeSessionAsync(persisted.Value, cancellationToken: innerCt)
                    : await _agent!.CreateSessionAsync(innerCt);
            },
            ct);

        try
        {
            await foreach (var update in _agent.RunStreamingAsync(userMessage, session, cancellationToken: ct))
            {
                var text = update.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
        finally
        {
            var serialized = await _agent.SerializeSessionAsync(session, cancellationToken: CancellationToken.None);
            await _conversationService.SaveSerializedSessionAsync(conversationId, serialized);
        }
    }

    public async Task ResetSessionAsync(string conversationId, CancellationToken ct = default)
    {
        if (_agent is null)
        {
            throw new InvalidOperationException("SetAgentContextAsync must be called before ResetSessionAsync.");
        }

        await _conversationService.ClearSerializedSessionAsync(conversationId);
        await _conversationService.RemoveAgentSessionAsync(conversationId);
    }
}
