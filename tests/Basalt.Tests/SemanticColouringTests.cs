using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// What an editor is told to colour.
/// </summary>
/// <remarks>
/// Seen in Rider before these existed: the code half was coloured and the
/// markup was entirely black, and a block's opening was blue while its "Next"
/// or "End Code" stayed black — which reads as an editor that half-understands
/// the file.
/// </remarks>
public sealed class SemanticColouringTests
{
    private static IReadOnlyList<(int Line, int Character, int Length, SemanticTokenType Type)>
        Tokens(string text, string uri = "file:///v.vbhtml") =>
        VbHtmlSemanticTokensHandler.Tokens(new OpenDocument(uri, text, 1));

    private static string TextOf(
        string source, (int Line, int Character, int Length, SemanticTokenType Type) token)
    {
        var line = source.Split('\n')[token.Line];

        return line.Substring(token.Character, Math.Min(token.Length, line.Length - token.Character));
    }

    [Fact]
    public void TheMarkupIsLeftToTheGrammar()
    {
        // The server colours the Visual Basic, because it is the parser that
        // compiles the view. It says nothing about HTML on purpose: a TextMate
        // grammar already colours that, and a second opinion on the same
        // question is free to disagree with the first.
        //
        // The markup looked black in Rider because Rider had no grammar for
        // .vbhtml — fixed by bundling one with the plugin, not by answering
        // here.
        Assert.Empty(Tokens("<p>just markup</p>"));
    }

    [Fact]
    public void ABlockClosingIsColouredLikeItsOpening()
    {
        // "@For Each" was blue and "Next" was black.
        const string view = "@For Each item In items\n<p>x</p>\nNext\n";

        var keywords = Tokens(view)
            .Where(t => t.Type == SemanticTokenType.Keyword)
            .Select(t => TextOf(view, t))
            .ToList();

        Assert.Contains(keywords, k => k.StartsWith("@For", StringComparison.Ordinal));
        Assert.Contains("Next", keywords);
    }

    [Fact]
    public void ACodeBlockIsColouredWhole()
    {
        const string view = "@Code\n    Dim x = 1\nEnd Code\n";

        var keywords = Tokens(view)
            .Where(t => t.Type == SemanticTokenType.Keyword)
            .Select(t => TextOf(view, t))
            .ToList();

        Assert.NotEmpty(keywords);
    }

    [Fact]
    public void TheCodeInAMixedViewIsStillColoured()
    {
        // Markup around the code must not stop the code being answered for.
        const string view = "<h1>title</h1>\n@Code\n    Dim x = 1\nEnd Code\n<p>@x</p>\n";

        var types = Tokens(view).Select(t => t.Type).Distinct().ToList();

        Assert.Contains(SemanticTokenType.Keyword, types);
        Assert.Contains(SemanticTokenType.Variable, types);
    }

    [Fact]
    public void TokensAreInOrder()
    {
        // The protocol encodes each token as an offset from the one before,
        // so out of order means every colour after it lands somewhere else.
        var tokens = Tokens("<h1>@name</h1>\n<p>text</p>\n@If ok Then\n<b>y</b>\nEnd If\n");

        var ordered = tokens.OrderBy(t => t.Line).ThenBy(t => t.Character).ToList();

        Assert.Equal(ordered, tokens);
    }

    [Fact]
    public void NoTokenRunsPastItsLine()
    {
        // A length reaching beyond the line is what makes a colour bleed over
        // the text after it.
        const string view = "<h1>@name</h1>\n@Code\n    Dim x = 1\nEnd Code\n";

        var lines = view.Split('\n');

        foreach (var token in Tokens(view))
        {
            Assert.True(
                token.Character + token.Length <= lines[token.Line].Length,
                $"a token on line {token.Line} runs {token.Character + token.Length} "
              + $"into a line of {lines[token.Line].Length}");
        }
    }

    [Fact]
    public void StringsAndNumbersAreToldApart()
    {
        // A Code block used to be one solid stripe of "keyword": a string, a
        // number and a comment all read the same, which is no colouring at
        // all.
        const string view = "@Code\n    Dim n = 42\n    Dim s = \"hello\"\nEnd Code\n";

        var types = Tokens(view).Select(t => t.Type).Distinct().ToList();

        Assert.Contains(SemanticTokenType.Number, types);
        Assert.Contains(SemanticTokenType.String, types);
        Assert.Contains(SemanticTokenType.Keyword, types);
    }

    [Fact]
    public void ACommentIsAComment()
    {
        const string view = "@Code\n    ' explains the next line\n    Dim n = 1\nEnd Code\n";

        var comments = Tokens(view)
            .Where(t => t.Type == SemanticTokenType.Comment)
            .Select(t => TextOf(view, t))
            .ToList();

        Assert.Contains(comments, c => c.Contains("explains", StringComparison.Ordinal));
    }

    [Fact]
    public void AnApostropheInsideAStringDoesNotStartAComment()
    {
        // The scan reads left to right, so a quote opens a string and
        // everything up to its close is string — apostrophe included.
        const string view = "@Code\n    Dim s = \"it's fine\"\n    Dim n = 1\nEnd Code\n";

        var numbers = Tokens(view)
            .Where(t => t.Type == SemanticTokenType.Number)
            .ToList();

        // The 1 on the next line is still found, which it would not be if the
        // apostrophe had swallowed the rest of the block as a comment.
        Assert.NotEmpty(numbers);
    }

    [Fact]
    public void MembersAfterADotAreMarked()
    {
        const string view = "@Code\n    Dim n = model.Name\nEnd Code\n";

        var properties = Tokens(view)
            .Where(t => t.Type == SemanticTokenType.Property)
            .Select(t => TextOf(view, t))
            .ToList();

        Assert.Contains("Name", properties);
    }

    [Fact]
    public void NoTwoTokensOverlap()
    {
        // Overlapping tokens are undefined in the protocol: an editor is free
        // to render either, and which one it picks varies between editors.
        const string view =
            "@Code\n    ' a note\n    Dim s = \"x\"\n    Dim n = 42\nEnd Code\n<p>@s</p>\n";

        var tokens = Tokens(view).ToList();

        for (var i = 1; i < tokens.Count; i++)
        {
            var previous = tokens[i - 1];
            var current = tokens[i];

            if (previous.Line != current.Line) continue;

            Assert.True(
                previous.Character + previous.Length <= current.Character,
                $"two tokens overlap on line {current.Line}: "
              + $"{previous.Character}+{previous.Length} then {current.Character}");
        }
    }

    [Fact]
    public void APageIsColouredToo()
    {
        // A .vbp holds the same Visual Basic behind different delimiters. The
        // server ignored the extension entirely, so a page got no colouring
        // and no completion at all.
        const string page = "<h1>hi</h1>\n<% Dim n = 42 %>\n<p><%= n %></p>\n";

        var types = Tokens(page, "file:///p.vbp").Select(t => t.Type).Distinct().ToList();

        Assert.Contains(SemanticTokenType.Number, types);
    }

    [Fact]
    public void APagesTokensLandOnItsOwnText()
    {
        // The page parser records where each part sits; without that every
        // token was reported at position zero and the whole page coloured its
        // first line.
        const string page = "<h1>hi</h1>\n<% Dim n = 42 %>\n";

        var numbers = Tokens(page, "file:///p.vbp")
            .Where(t => t.Type == SemanticTokenType.Number)
            .ToList();

        var number = Assert.Single(numbers);

        // On the second line, where the 42 is.
        Assert.Equal(1, number.Line);
    }
}
