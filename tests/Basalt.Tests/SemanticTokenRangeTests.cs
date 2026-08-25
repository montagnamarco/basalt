using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Basalt.Tests;

/// <summary>
/// Colouring only the lines the editor is showing.
///
/// A whole large template used to be coloured on every edit when the editor
/// only ever displays a screenful.
/// </summary>
public class SemanticTokenRangeTests
{
    private static Range Lines(int from, int to) =>
        new(new Position(from, 0), new Position(to, 0));

    [Fact]
    public void KeepsALineInsideTheRange()
    {
        Assert.True(VbHtmlSemanticTokensHandler.Within(Lines(10, 20), 15));
    }

    [Fact]
    public void KeepsTheFirstAndLastLines()
    {
        // Inclusive at both ends: a token on the first visible line has to be
        // coloured, or the top of the screen loses its highlighting.
        Assert.True(VbHtmlSemanticTokensHandler.Within(Lines(10, 20), 10));
        Assert.True(VbHtmlSemanticTokensHandler.Within(Lines(10, 20), 20));
    }

    [Fact]
    public void DropsLinesOutsideTheRange()
    {
        Assert.False(VbHtmlSemanticTokensHandler.Within(Lines(10, 20), 9));
        Assert.False(VbHtmlSemanticTokensHandler.Within(Lines(10, 20), 21));
    }

    [Fact]
    public void HandlesASingleLineRange()
    {
        Assert.True(VbHtmlSemanticTokensHandler.Within(Lines(5, 5), 5));
        Assert.False(VbHtmlSemanticTokensHandler.Within(Lines(5, 5), 6));
    }
}
