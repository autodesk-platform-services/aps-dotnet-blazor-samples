using System.Text.RegularExpressions;
using ApsSamples.Models;

namespace ApsSamples.Services;

/// <summary>
/// Parses <c>@handle</c> mentions that route a chat turn to another assistant.
/// </summary>
public static class AssistantMentions
{
    // An '@' not preceded by a word character or another '@' (so e-mail addresses are ignored).
    private static readonly Regex MentionRegex = new(@"(?<![\w@])@([A-Za-z0-9]+)", RegexOptions.Compiled);

    /// <summary>
    /// Derives the mention handle from an assistant name: lowercase, letters and digits only
    /// (e.g. "BIM Manager Assistant" becomes "bimmanagerassistant").
    /// </summary>
    public static string ToHandle(string agentName) =>
        new(agentName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Returns the candidate whose handle matches the first mention in <paramref name="text"/>
    /// that refers to a known candidate, or null when no mention matches.
    /// </summary>
    public static AgentDefinition? FindMentioned(string text, IEnumerable<AgentDefinition> candidates)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var byHandle = new Dictionary<string, AgentDefinition>();
        foreach (var candidate in candidates)
        {
            var handle = ToHandle(candidate.AgentName);
            if (handle.Length > 0)
            {
                byHandle.TryAdd(handle, candidate);
            }
        }

        foreach (Match match in MentionRegex.Matches(text))
        {
            if (byHandle.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out var mentioned))
            {
                return mentioned;
            }
        }

        return null;
    }
}
