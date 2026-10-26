using ApsSamples.Services;

namespace ApsSamples.Tools;

// Scoped, per-circuit context shared by every agent tool class. AgentChatService writes the
// hub/project/conversation/agent the user is chatting in, and tools read them from here instead
// of taking hubId/projectId parameters, so the agent can't scope a call to the wrong project.
// Tokens are issued here too, so every tool acts as the agent's service account (SSA).
public class AgentToolContext(ISsaService ssaService, IAPSAuthenticationService apsAuth)
{
    // Scopes for the agent's SSA token: Data Management (incl. folder creation), Admin project
    // users, Revit Cloud Models and Issues.
    public static readonly List<string> SsaScopes =
    [
        "data:read",
        "data:write",
        "data:create",
        "account:read",
        "account:write",
    ];

    public string? HubId { get; set; }
    public string? ProjectId { get; set; }
    public string? ConversationId { get; set; }
    public string? AgentId { get; set; }
    public string? SsaId { get; set; }

    // Set only while a delegate runs a delegated task, so it can't delegate further (recursion guard).
    public string? DelegatedTaskId { get; set; }

    // Account Admin / Issues / RCM style IDs drop the "b." prefix used by the Data Management API.
    public string AccountProjectId => StripHubPrefix(RequireProjectId());
    public string AccountId => StripHubPrefix(RequireHubId());

    public string RequireHubId()
    {
        if (string.IsNullOrEmpty(HubId))
        {
            throw new InvalidOperationException("No active hub is set for this chat session.");
        }

        return HubId;
    }

    public string RequireProjectId()
    {
        if (string.IsNullOrEmpty(ProjectId))
        {
            throw new InvalidOperationException("No active project is set for this chat session.");
        }

        return ProjectId;
    }

    public string RequireConversationId()
    {
        if (string.IsNullOrEmpty(ConversationId))
        {
            throw new InvalidOperationException("No active conversation is set for this chat session.");
        }

        return ConversationId;
    }

    public string RequireSsaId()
    {
        if (string.IsNullOrEmpty(SsaId))
        {
            throw new InvalidOperationException("This agent has no service account configured.");
        }

        return SsaId;
    }

    // Token for every agent action: the agent's own service account.
    public async Task<string> GetAccessTokenAsync() =>
        await ssaService.GetSsaTokenAsync(RequireSsaId(), SsaScopes);

    // App-level 2-legged token with account:read, for hq/v1 hub user search, which doesn't
    // accept SSA tokens.
    public Task<string> GetAccountTwoLeggedTokenAsync() =>
        apsAuth.GetAccountTwoLeggedTokenAsync();

    private static string StripHubPrefix(string id) =>
        id.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? id[2..] : id;
}
