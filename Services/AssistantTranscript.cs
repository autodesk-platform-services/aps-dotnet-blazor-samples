using ApsSamples.Models;
using Microsoft.Extensions.AI;

namespace ApsSamples.Services;

/// <summary>
/// Builds the input for an assistant turn in a conversation that several assistants share.
/// Each assistant keeps its own session, so the input contains only what the answering
/// assistant has not seen yet, followed by the new user message.
/// </summary>
public static class AssistantTranscript
{
    /// <param name="history">Conversation messages, possibly including the current user message and an empty streaming placeholder.</param>
    /// <param name="agentId">The assistant answering this turn.</param>
    /// <param name="primaryAgentId">The conversation's original assistant, used for messages without an author.</param>
    /// <param name="resolveName">Resolves an assistant id to its display name.</param>
    /// <param name="userMessage">The new user message.</param>
    public static IReadOnlyList<ChatMessage> BuildInput(
        IReadOnlyList<ConversationMessage> history,
        string agentId,
        string primaryAgentId,
        Func<string?, string> resolveName,
        string userMessage)
    {
        var messages = history.Where(m => !string.IsNullOrWhiteSpace(m.Content)).ToList();

        // The caller usually persists the user message before streaming; don't send it twice.
        var lastUserIndex = messages.FindLastIndex(IsUser);
        if (lastUserIndex >= 0 && messages[lastUserIndex].Content.Trim() == userMessage.Trim())
        {
            messages.RemoveAt(lastUserIndex);
        }

        // The answering assistant's session already holds everything up to its last own reply.
        var lastOwnReplyIndex = messages.FindLastIndex(m =>
            !IsUser(m) && !IsNotice(m) && EffectiveAuthor(m, primaryAgentId) == agentId);

        var input = new List<ChatMessage>();
        foreach (var message in messages.Skip(lastOwnReplyIndex + 1))
        {
            var text = IsUser(message)
                ? message.Content
                : $"[{resolveName(EffectiveAuthor(message, primaryAgentId))}]: {message.Content}";
            input.Add(new ChatMessage(ChatRole.User, text));
        }

        input.Add(new ChatMessage(ChatRole.User, userMessage));
        return input;
    }

    private static bool IsUser(ConversationMessage message) =>
        string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);

    private static bool IsNotice(ConversationMessage message) =>
        message.Content.StartsWith("✅", StringComparison.Ordinal)
        || message.Content.StartsWith("⚠️", StringComparison.Ordinal);

    private static string EffectiveAuthor(ConversationMessage message, string primaryAgentId) =>
        string.IsNullOrEmpty(message.AgentId) ? primaryAgentId : message.AgentId;
}
