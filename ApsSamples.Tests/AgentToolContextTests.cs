using ApsSamples.Tools;
using Xunit;

namespace ApsSamples.Tests;

public class AgentToolContextTests
{
    private static AgentToolContext NewContext() => new(null!, null!);

    [Fact]
    public void RequireHubId_ThrowsWhenUnset()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NewContext().RequireHubId());
        Assert.Equal("No active hub is set for this chat session.", ex.Message);
    }

    [Fact]
    public void RequireProjectId_ThrowsWhenUnset()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NewContext().RequireProjectId());
        Assert.Equal("No active project is set for this chat session.", ex.Message);
    }

    [Fact]
    public void RequireConversationId_ThrowsWhenUnset()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NewContext().RequireConversationId());
        Assert.Equal("No active conversation is set for this chat session.", ex.Message);
    }

    [Fact]
    public void RequireSsaId_ThrowsWhenUnset()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => NewContext().RequireSsaId());
        Assert.Equal("This agent has no service account configured.", ex.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ThrowsWhenSsaUnset()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewContext().GetAccessTokenAsync());
    }

    [Fact]
    public void Require_ReturnsSetValues()
    {
        var context = NewContext();
        context.HubId = "b.hub";
        context.ProjectId = "b.project";
        context.ConversationId = "conv";
        context.SsaId = "ssa";

        Assert.Equal("b.hub", context.RequireHubId());
        Assert.Equal("b.project", context.RequireProjectId());
        Assert.Equal("conv", context.RequireConversationId());
        Assert.Equal("ssa", context.RequireSsaId());
    }

    [Theory]
    [InlineData("b.1234", "1234")]
    [InlineData("B.1234", "1234")]
    [InlineData("1234", "1234")]
    public void AccountProjectId_StripsHubPrefix(string projectId, string expected)
    {
        var context = NewContext();
        context.ProjectId = projectId;

        Assert.Equal(expected, context.AccountProjectId);
    }

    [Theory]
    [InlineData("b.abcd", "abcd")]
    [InlineData("abcd", "abcd")]
    public void AccountId_StripsHubPrefix(string hubId, string expected)
    {
        var context = NewContext();
        context.HubId = hubId;

        Assert.Equal(expected, context.AccountId);
    }

    [Fact]
    public void AccountIds_ThrowWhenUnset()
    {
        var context = NewContext();

        Assert.Throws<InvalidOperationException>(() => context.AccountProjectId);
        Assert.Throws<InvalidOperationException>(() => context.AccountId);
    }
}
