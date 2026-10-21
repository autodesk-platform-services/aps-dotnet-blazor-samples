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
    private readonly AgentToolContext _toolContext;
    private readonly IServiceProvider _services;
    private readonly IAgentConversationService _conversationService;
    private readonly Dictionary<string, AIAgent> _agents = new();
    private string? _currentConversationId;

    public AgentChatService(
        IChatClient chatClient,
        IAgentRegistryService agentRegistry,
        IToolCatalogService toolCatalog,
        AgentToolContext toolContext,
        IServiceProvider services,
        IAgentConversationService conversationService)
    {
        _chatClient = chatClient;
        _agentRegistry = agentRegistry;
        _toolCatalog = toolCatalog;
        _toolContext = toolContext;
        _services = services;
        _conversationService = conversationService;
    }

    public void SetHubContext(string hubId)
    {
        _toolContext.HubId = hubId;
    }

    public void SetProjectContext(string projectId)
    {
        _toolContext.ProjectId = projectId;
    }

    public void SetConversationContext(string conversationId)
    {
        _toolContext.ConversationId = conversationId;
        _currentConversationId = conversationId;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
            string agentId,
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_currentConversationId))
        {
            throw new InvalidOperationException("SetConversationContext must be called before StreamResponseAsync.");
        }

        var conversationId = _currentConversationId;
        var conversation = await _conversationService.GetConversationAsync(conversationId)
            ?? throw new InvalidOperationException($"Conversation '{conversationId}' not found.");

        var def = await _agentRegistry.GetAgentAsync(agentId)
            ?? throw new InvalidOperationException($"Assistant '{agentId}' not found.");

        // Tools act on behalf of whichever assistant answers this turn.
        _toolContext.AgentId = agentId;
        _toolContext.SsaId = def.SsaId;
        var agent = GetOrCreateAgent(def);

        var names = new Dictionary<string, string>();
        foreach (var projectAgent in await _agentRegistry.GetAgentsByProjectAsync(conversation.ProjectId))
        {
            names[projectAgent.AgentId] = projectAgent.AgentName;
        }
        names[def.AgentId] = def.AgentName;

        string ResolveName(string? id) =>
            id != null && names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Assistant";

        var inputMessages = AssistantTranscript.BuildInput(
            conversation.Messages.ToList(),
            agentId,
            conversation.AgentId,
            ResolveName,
            userMessage);

        var session = await _conversationService.GetOrLoadAgentSessionAsync(
            conversationId,
            agentId,
            async innerCt =>
            {
                var persisted = await _conversationService.GetSerializedSessionAsync(conversationId, agentId);
                return persisted.HasValue
                    ? await agent.DeserializeSessionAsync(persisted.Value, cancellationToken: innerCt)
                    : await agent.CreateSessionAsync(innerCt);
            },
            ct);

        try
        {
            await foreach (var update in agent.RunStreamingAsync(inputMessages, session, cancellationToken: ct))
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
            var serialized = await agent.SerializeSessionAsync(session, cancellationToken: CancellationToken.None);
            await _conversationService.SaveSerializedSessionAsync(conversationId, agentId, serialized);
        }
    }

    public async Task ResetSessionAsync(string conversationId, CancellationToken ct = default)
    {
        await _conversationService.ClearSerializedSessionAsync(conversationId);
        await _conversationService.RemoveAgentSessionAsync(conversationId);
    }

    private AIAgent GetOrCreateAgent(AgentDefinition def)
    {
        if (!_agents.TryGetValue(def.AgentId, out var agent))
        {
            agent = _chatClient.AsAIAgent(
                instructions: def.InstructionsText,
                name: def.AgentName,
                tools: _toolCatalog.GetAIFunctions(def.EnabledToolIds, _services).Cast<AITool>().ToArray());
            _agents[def.AgentId] = agent;
        }

        return agent;
    }
}
