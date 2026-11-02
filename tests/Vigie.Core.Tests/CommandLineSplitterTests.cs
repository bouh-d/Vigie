using Vigie.Core.ClaudeCode;
using Xunit;

namespace Vigie.Core.Tests;

public class CommandLineSplitterTests
{
    [Fact]
    public void SplitsOnWhitespace()
    {
        Assert.True(CommandLineSplitter.TrySplit("--continue --max-turns 5", out IReadOnlyList<string> args));
        Assert.Equal(["--continue", "--max-turns", "5"], args);
    }

    [Fact]
    public void KeepsQuotedGroupsTogether()
    {
        Assert.True(CommandLineSplitter.TrySplit("--add-dir \"C:\\mes projets\\api\"", out IReadOnlyList<string> args));
        Assert.Equal(["--add-dir", @"C:\mes projets\api"], args);
    }

    [Fact]
    public void DoubledQuoteInsideQuotesIsALiteralQuote()
    {
        Assert.True(CommandLineSplitter.TrySplit("\"a \"\"b\"\" c\"", out IReadOnlyList<string> args));
        Assert.Equal(["a \"b\" c"], args);
    }

    [Fact]
    public void BackslashesAreLiteral()
    {
        Assert.True(CommandLineSplitter.TrySplit(@"C:\tools\node.exe", out IReadOnlyList<string> args));
        Assert.Equal([@"C:\tools\node.exe"], args);
    }

    [Fact]
    public void UnterminatedQuoteFails()
    {
        Assert.False(CommandLineSplitter.TrySplit("--flag \"open", out IReadOnlyList<string> args));
        Assert.Empty(args);
    }

    [Fact]
    public void EmptyInputYieldsNoArguments()
    {
        Assert.True(CommandLineSplitter.TrySplit("   ", out IReadOnlyList<string> args));
        Assert.Empty(args);
    }
}
