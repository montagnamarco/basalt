using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Web;
using Basalt.Workspace.Web;

namespace Basalt.Workspace.Completion;

/// <summary>
/// Where the name being written at the caret begins: the text a completion
/// list filters on, and the text its entry replaces.
/// </summary>
/// <remarks>
/// The characters a name is made of depend on the language. Visual Basic
/// names are letters, digits and underscores; a minus sign there is
/// subtraction. Style rules and markup names carry dashes ("background-color",
/// "aria-label", "asp-action"), and a Razor directive attribute carries its
/// "@" ("@onclick"). The list and the entry must agree on the whole name, or
/// Enter writes "background-background-color".
/// </remarks>
public static class CompletionWord
{
    /// <summary>The offset at which the word ending at <paramref name="caret"/> begins; the caret itself when no word does.</summary>
    public static int StartOf(string filePath, string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ".css") return StyleNameStart(text, caret);

        if (extension is ".html" or ".htm") return MarkupNameStart(text, caret, HtmlContextReader.At(text, caret).Kind);

        if (extension is ".vbhtml" or ".vbrazor") return TemplateNameStart(text, caret);

        if (extension is ".vbpage")
            return TemplateGeneration.IsInCode(filePath, text, caret)
                ? NameStart(text, caret, dashes: false)
                : MarkupNameStart(text, caret, HtmlContextReader.At(text, caret).Kind);

        return NameStart(text, caret, dashes: false);
    }

    /// <summary>
    /// A name in a Razor template. The markup is read with the Visual Basic blanked out, so that a comparison
    /// "a &lt; b" in code is not taken for the start of a tag. In code a name is a Visual Basic one ("a-b" is
    /// a subtraction), except a directive attribute: "@onclick" inside a tag parses as an expression, but it is
    /// markup, and its "@" belongs to the name.
    /// </summary>
    private static int TemplateNameStart(string text, int caret)
    {
        var spans = VbHtmlCodeRegions.CodeSpans(text);
        var markup = WithCodeBlanked(text, spans);

        foreach (var (start, length) in spans)
        {
            if (caret <= start || caret > start + length) continue;

            var directiveAttribute = text[start] == '@'
                && HtmlContextReader.At(markup, start).Kind == HtmlContextKind.AttributeName;

            return directiveAttribute
                ? MarkupNameStart(text, caret, HtmlContextKind.AttributeName)
                : NameStart(text, caret, dashes: false);
        }

        return MarkupNameStart(text, caret, HtmlContextReader.At(markup, caret).Kind);
    }

    /// <summary>The text with every code span turned to spaces, line breaks kept.</summary>
    private static string WithCodeBlanked(string text, IReadOnlyList<(int Start, int Length)> spans)
    {
        var characters = text.ToCharArray();

        foreach (var (start, length) in spans)
        {
            for (var i = start; i < Math.Min(start + length, characters.Length); i++)
                if (characters[i] is not ('\r' or '\n')) characters[i] = ' ';
        }

        return new string(characters);
    }

    /// <summary>A property or at-rule: letters, digits, underscores, dashes, and an "@" in front of an at-rule.</summary>
    private static int StyleNameStart(string text, int caret)
    {
        var start = NameStart(text, caret, dashes: true);

        return start > 0 && text[start - 1] == '@' ? start - 1 : start;
    }

    /// <summary>
    /// An element name, attribute name or attribute value in markup. Outside a
    /// tag the word is the ordinary one, so "well-known" in running text is not
    /// a name.
    /// </summary>
    private static int MarkupNameStart(string text, int caret, HtmlContextKind context)
    {
        if (context is not (HtmlContextKind.ElementName or HtmlContextKind.AttributeName or HtmlContextKind.AttributeValue))
            return NameStart(text, caret, dashes: false);

        var start = NameStart(text, caret, dashes: true);

        // The "@" of a Blazor or Razor directive attribute, "@onclick", is part
        // of the name the list offers and of the one it replaces.
        return context == HtmlContextKind.AttributeName && start > 0 && text[start - 1] == '@'
            ? start - 1
            : start;
    }

    private static int NameStart(string text, int caret, bool dashes)
    {
        var start = caret;

        while (start > 0 && (IsNameCharacter(text[start - 1]) || dashes && text[start - 1] == '-'))
            start--;

        // A dash is a separator, not the start of a name: "x -c" is "c".
        while (start < caret && text[start] == '-') start++;

        return start;
    }

    private static bool IsNameCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';
}
