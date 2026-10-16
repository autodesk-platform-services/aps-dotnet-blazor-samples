using ApsSamples.Tools;
using Xunit;

namespace ApsSamples.Tests;

public class FolderPathValidatorTests
{
    [Theory]
    [InlineData("Architecture")]
    [InlineData("01 WIP")]
    [InlineData("  Padded  ")]
    [InlineData("Phase-2_(draft) & notes.v1")]
    public void ValidateName_AcceptsValidNames(string name)
    {
        Assert.Null(FolderPathValidator.ValidateName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateName_RejectsEmptyNames(string? name)
    {
        Assert.NotNull(FolderPathValidator.ValidateName(name));
    }

    [Theory]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a:b")]
    [InlineData("a\"b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a|b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a'b")]
    [InlineData("a¢b")]
    [InlineData("a™b")]
    [InlineData("a$b")]
    [InlineData("a®b")]
    [InlineData("a\tb")]
    [InlineData("a\u0001b")]
    public void ValidateName_RejectsReservedCharacters(string name)
    {
        Assert.NotNull(FolderPathValidator.ValidateName(name));
    }

    [Fact]
    public void ValidateName_LimitsLength()
    {
        Assert.Null(FolderPathValidator.ValidateName(new string('a', FolderPathValidator.MaxNameLength)));
        Assert.NotNull(FolderPathValidator.ValidateName(new string('a', FolderPathValidator.MaxNameLength + 1)));
    }

    [Fact]
    public void ValidateName_ChecksTheTrimmedName()
    {
        var padded = "  " + new string('a', FolderPathValidator.MaxNameLength) + "  ";
        Assert.Null(FolderPathValidator.ValidateName(padded));
    }

    [Theory]
    [InlineData("01 WIP/Architecture", new[] { "01 WIP", "Architecture" })]
    [InlineData(" 01 WIP / Architecture ", new[] { "01 WIP", "Architecture" })]
    [InlineData("/01 WIP//Architecture/", new[] { "01 WIP", "Architecture" })]
    [InlineData("Single", new[] { "Single" })]
    [InlineData("a/ /b", new[] { "a", "b" })]
    public void SplitPath_TrimsAndIgnoresEmptySegments(string path, string[] expected)
    {
        Assert.Equal(expected, FolderPathValidator.SplitPath(path));
    }

    [Fact]
    public void TryParsePath_ReturnsSegmentsForValidPath()
    {
        Assert.True(FolderPathValidator.TryParsePath("01 WIP/Architecture/Models", out var segments, out var error));
        Assert.Null(error);
        Assert.Equal(["01 WIP", "Architecture", "Models"], segments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("///")]
    [InlineData(" / ")]
    public void TryParsePath_RejectsEmptyPaths(string? path)
    {
        Assert.False(FolderPathValidator.TryParsePath(path, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParsePath_RejectsInvalidSegment()
    {
        Assert.False(FolderPathValidator.TryParsePath("01 WIP/Arch*itecture", out _, out var error));
        Assert.Contains("Arch*itecture", error);
    }

    [Fact]
    public void TryParsePath_LimitsDepth()
    {
        var maxDepth = string.Join('/', Enumerable.Range(1, FolderPathValidator.MaxDepth).Select(i => $"L{i}"));
        var tooDeep = maxDepth + "/extra";

        Assert.True(FolderPathValidator.TryParsePath(maxDepth, out _, out _));
        Assert.False(FolderPathValidator.TryParsePath(tooDeep, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(FolderPathValidator.MaxPathsPerCall)]
    public void ValidatePathCount_AcceptsUpToTheLimit(int count)
    {
        Assert.Null(FolderPathValidator.ValidatePathCount(count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(FolderPathValidator.MaxPathsPerCall + 1)]
    public void ValidatePathCount_RejectsNoneOrTooMany(int count)
    {
        Assert.NotNull(FolderPathValidator.ValidatePathCount(count));
    }
}
