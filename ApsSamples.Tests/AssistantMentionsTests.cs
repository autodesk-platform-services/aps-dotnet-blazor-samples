using ApsSamples.Models;
using ApsSamples.Services;
using Xunit;

namespace ApsSamples.Tests;

public class AssistantMentionsTests
{
    private static readonly AgentDefinition BimManager = new() { AgentId = "bim", AgentName = "BIM Manager Assistant" };
    private static readonly AgentDefinition Designer = new() { AgentId = "design", AgentName = "Design Assistant" };
    private static readonly AgentDefinition[] Candidates = { BimManager, Designer };

    [Fact]
    public void ToHandle_LowercasesAndKeepsOnlyLettersAndDigits()
    {
        Assert.Equal("bimmanagerassistant", AssistantMentions.ToHandle("BIM Manager Assistant"));
        Assert.Equal("qa2assistant", AssistantMentions.ToHandle("QA-2 Assistant!"));
    }

    [Fact]
    public void FindMentioned_MatchesCaseInsensitively()
    {
        Assert.Same(BimManager, AssistantMentions.FindMentioned("@BimManagerAssistant hello", Candidates));
    }

    [Fact]
    public void FindMentioned_MatchesMidSentenceAndBeforePunctuation()
    {
        Assert.Same(Designer, AssistantMentions.FindMentioned("Could you ask @designassistant, please?", Candidates));
        Assert.Same(Designer, AssistantMentions.FindMentioned("What do you think @designassistant?", Candidates));
    }

    [Fact]
    public void FindMentioned_ReturnsNullForUnknownHandle()
    {
        Assert.Null(AssistantMentions.FindMentioned("@someoneelse hello", Candidates));
        Assert.Null(AssistantMentions.FindMentioned("no mentions here", Candidates));
    }

    [Fact]
    public void FindMentioned_IgnoresEmailLikeText()
    {
        Assert.Null(AssistantMentions.FindMentioned("mail a@bimmanagerassistant", Candidates));
    }

    [Fact]
    public void FindMentioned_FirstMatchingMentionWins()
    {
        Assert.Same(Designer, AssistantMentions.FindMentioned(
            "@unknown @designassistant and @bimmanagerassistant", Candidates));
    }
}
