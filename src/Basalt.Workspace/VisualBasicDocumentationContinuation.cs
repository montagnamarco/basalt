using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Workspace;

/// <summary>
/// Carries a <c>'''</c> documentation comment onto the next line when Enter is
/// pressed inside it, as Visual Studio does.
/// </summary>
public static class VisualBasicDocumentationContinuation
{
    /// <summary>
    /// The text that begins the line after the caret's line: its indentation
    /// followed by <c>''' </c>. Null when the caret's line is not part of a
    /// documentation comment.
    /// </summary>
    /// <remarks>
    /// The text decides only where to look: the line must start with <c>'''</c>.
    /// Whether that comment is documentation is the parser's answer. With the
    /// default options a <c>'''</c> line is an ordinary comment, so the parse
    /// asks for documentation mode, the same one the documentation generator uses.
    /// </remarks>
    public static string? PrefixFor(string text, int caret)
    {
        var source = SourceText.From(text);
        var position = Math.Clamp(caret, 0, text.Length);
        var line = source.Lines.GetLineFromPosition(position);
        var lineText = source.ToString(line.Span);

        var trimmed = lineText.TrimStart(' ', '\t');
        if (!trimmed.StartsWith("'''", StringComparison.Ordinal)) return null;

        // Enter before the marker (Home, then Enter) splits the line rather than
        // continuing the comment: the marker belongs to the text after the caret.
        var indentation = lineText[..(lineText.Length - trimmed.Length)];
        if (position < line.Start + indentation.Length + 3) return null;

        var root = VisualBasicSyntaxTree.ParseText(text,
            new VisualBasicParseOptions(documentationMode: DocumentationMode.Parse)).GetRoot();
        var documented = root.DescendantTrivia(line.Span, descendIntoTrivia: false)
            .Any(trivia => trivia.IsKind(SyntaxKind.DocumentationCommentTrivia));
        if (!documented) return null;

        return indentation + "''' ";
    }
}
