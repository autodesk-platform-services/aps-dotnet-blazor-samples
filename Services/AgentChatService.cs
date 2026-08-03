using ApsSamples.Models;
using ApsSamples.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ApsSamples.Services;

public class AgentChatService : IAgentChatService
{
    private readonly AIAgent _chatClient;
    private readonly BimManagerAssistantTools _bimManagerAssistantTools;
    private readonly IAgentConversationService _conversationService;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private string? _currentConversationId;

    public AgentChatService(
        IChatClient chatClient,
        IOptions<AgentOptions> options,
        BimManagerAssistantTools bimManagerAssistantTools,
        IAgentConversationService conversationService)
    {
        _bimManagerAssistantTools = bimManagerAssistantTools;
        _conversationService = conversationService;
        _chatClient = chatClient.AsAIAgent(
            instructions: "You are a BIM Manager assistant scoped to a single project the user is already chatting about. " +
                "Keep your answers brief. You can list the Revit models in that project, check whether a model has " +
                "ever been published (or list all models that haven't been), and publish a Revit " +
                "cloud-worksharing model - publishing automatically tracks a task and tells you here once it " +
                "completes or fails, you don't need to check on it separately. The project is already known to " +
                "your tools; never ask the user for a project ID. " +
                "Whenever you list one or more Revit models, always show each model's item ID next to its name, " +
                "on a numbered list, formatted exactly as \"Model: <name> - Id: <itemId>\" - the ID isn't shown " +
                "anywhere else, so if you omit it you won't be able to look it up again on a later turn (e.g. to " +
                "publish or check one of the models you just listed).",
            name: "BimManagerAssistant",
            tools:
            [
                AIFunctionFactory.Create(bimManagerAssistantTools.ListRevitModelsAsync),
                AIFunctionFactory.Create(bimManagerAssistantTools.PublishRevitModelAsync),
                AIFunctionFactory.Create(bimManagerAssistantTools.CheckModelPublishEligibilityAsync),
                AIFunctionFactory.Create(bimManagerAssistantTools.ListUnpublishedModelsAsync)
            ]);
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

    public async IAsyncEnumerable<string> StreamResponseAsync(
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_currentConversationId))
        {
            throw new InvalidOperationException("SetConversationContext must be called before StreamResponseAsync.");
        }

        var conversationId = _currentConversationId;
        var session = await GetOrLoadSessionAsync(conversationId, ct);

        try
        {
            await foreach (var update in _chatClient.RunStreamingAsync(userMessage, session, cancellationToken: ct))
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
            var serialized = await _chatClient.SerializeSessionAsync(session, cancellationToken: CancellationToken.None);
            await _conversationService.SaveSerializedSessionAsync(conversationId, serialized);
        }
    }

    public async Task ResetSessionAsync(string conversationId, CancellationToken ct = default)
    {
        var session = await _chatClient.CreateSessionAsync(ct);
        _sessions[conversationId] = session;
        var serialized = await _chatClient.SerializeSessionAsync(session, cancellationToken: ct);
        await _conversationService.SaveSerializedSessionAsync(conversationId, serialized);
    }

    private async Task<AgentSession> GetOrLoadSessionAsync(string conversationId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(conversationId, out var existing))
        {
            return existing;
        }

        var persisted = await _conversationService.GetSerializedSessionAsync(conversationId);
        AgentSession session = persisted.HasValue
            ? await _chatClient.DeserializeSessionAsync(persisted.Value, cancellationToken: ct)
            : await _chatClient.CreateSessionAsync(ct);

        _sessions[conversationId] = session;
        return session;
    }
}
