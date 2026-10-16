using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace ApsSamples.Tests;

public class AssistantTranscriptTests
{
    private const string Primary = "primary";
    private const string Other = "other";

    private static string ResolveName(string? agentId) => agentId switch
    {
        Primary => "Primary Assistant",
        Other => "Other Assistant",
        _ => "Assistant"
    };

    private static ConversationMessage User(string content) => new() { Role = "user", Content = content };

    private static ConversationMessage Assistant(string content, string? agentId = null) =>
        new() { Role = "assistant", Content = content, AgentId = agentId };

    private static IReadOnlyList<ChatMessage> Build(IReadOnlyList<ConversationMessage> history, string agentId, string userMessage) =>
        AssistantTranscript.BuildInput(history, agentId, Primary, ResolveName, userMessage);

    [Fact]
    public void NoHistory_ReturnsOnlyUserMessage()
    {
        var input = Build(Array.Empty<ConversationMessage>(), Primary, "hello");

        var message = Assert.Single(input);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("hello", message.Text);
    }

    [Fact]
    public void OwnPriorReply_IncludesOnlyMessagesAfterIt()
    {
        var history = new[]
        {
            User("first"),
            Assistant("first reply", Primary),
            User("@otherassistant second"),
            Assistant("second reply", Other),
        };

        var input = Build(history, Primary, "third");

        Assert.Equal(
            new[] { "@otherassistant second", "[Other Assistant]: second reply", "third" },
            input.Select(m => m.Text));
        Assert.All(input, m => Assert.Equal(ChatRole.User, m.Role));
    }

    [Fact]
    public void OtherAssistantMessages_AreRelabelledAsUserMessages()
    {
        var history = new[]
        {
            User("first"),
            Assistant("primary reply", Primary),
        };

        var input = Build(history, Other, "@otherassistant your view?");

        Assert.Equal(
            new[] { "first", "[Primary Assistant]: primary reply", "@otherassistant your view?" },
            input.Select(m => m.Text));
        Assert.All(input, m => Assert.Equal(ChatRole.User, m.Role));
    }

    [Fact]
    public void CurrentUserMessageAndEmptyPlaceholder_AreNotDuplicated()
    {
        var history = new[]
        {
            User("first"),
            Assistant("first reply", Primary),
            User("second "),
            Assistant(string.Empty, Primary),
        };

        var input = Build(history, Primary, "second");

        var message = Assert.Single(input);
        Assert.Equal("second", message.Text);
    }

    [Fact]
    public void LegacyMessagesWithoutAuthor_AreAttributedToPrimary()
    {
        var history = new[]
        {
            User("first"),
            Assistant("legacy reply"),
        };

        var primaryInput = Build(history, Primary, "next");
        Assert.Equal(new[] { "next" }, primaryInput.Select(m => m.Text));

        var otherInput = Build(history, Other, "next");
        Assert.Equal(
            new[] { "first", "[Primary Assistant]: legacy reply", "next" },
            otherInput.Select(m => m.Text));
    }

    [Fact]
    public void OwnNotices_AreIncludedAsUnseen()
    {
        var history = new[]
        {
            User("publish it"),
            Assistant("Publishing started.", Primary),
            Assistant("✅ **Publish** — the model was published successfully.", Primary),
            Assistant("⚠️ **Other** — timed out waiting for the publish to complete."),
        };

        var input = Build(history, Primary, "thanks");

        Assert.Equal(
            new[]
            {
                "[Primary Assistant]: ✅ **Publish** — the model was published successfully.",
                "[Primary Assistant]: ⚠️ **Other** — timed out waiting for the publish to complete.",
                "thanks"
            },
            input.Select(m => m.Text));
    }
}
