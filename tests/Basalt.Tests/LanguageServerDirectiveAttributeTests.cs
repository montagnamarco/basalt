using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// A directive attribute written through the language server replaces the whole "@" name, as Basalt's editor does.
/// </summary>
/// <remarks>
/// Without a range VS Code replaces its own idea of the word, which leaves the "@" out, so "@oncl" became
/// "@@onclick".
/// </remarks>
public sealed class LanguageServerDirectiveAttributeTests
{
    [Fact]
    public void AnAtNameReplacesTheWholeNameItsAtIncluded()
    {
        const string text = "<button @oncl>+</button>\n";
        var offset = text.IndexOf("oncl", StringComparison.Ordinal) + 4;
        var caret = new Position(0, offset);

        var items = VbHtmlCompletionHandler.WithAtNamesReplaced(
            [new CompletionItem { Label = "@onclick", InsertText = "@onclick", FilterText = "onclick" }],
            "/work/Counter.vbrazor", text, offset, caret);

        var edit = items.Single().TextEdit?.TextEdit;

        Assert.NotNull(edit);
        Assert.Equal(new Position(0, text.IndexOf('@', StringComparison.Ordinal)), edit.Range.Start);
        Assert.Equal(caret, edit.Range.End);
        Assert.Equal("@onclick", edit.NewText);

        // The editor matches the filter text against the text in the range, "@oncl": without the "@" it hid the item.
        Assert.Equal("@onclick", items.Single().FilterText);
    }

    [Fact]
    public void AnItemWithoutAnAtIsLeftToTheEditor()
    {
        const string text = "<button cla>+</button>\n";
        var offset = text.IndexOf("cla", StringComparison.Ordinal) + 3;

        var items = VbHtmlCompletionHandler.WithAtNamesReplaced(
            [new CompletionItem { Label = "class", InsertText = "class" }],
            "/work/Counter.vbrazor", text, offset, new Position(0, offset));

        Assert.Null(items.Single().TextEdit);
    }
}
