using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>
/// Finds where a class, module or structure named like a file is declared
/// in that file's own source — the question Solution Explorer's rename
/// asks before offering to rename the code element along with the file,
/// the way Visual Studio offers to rename "Form1" after "Form1.vb" becomes
/// "Renamed.vb".
///
/// Parsed with Roslyn's own Visual Basic compiler rather than a second
/// opinion about the language: this is ordinary .vb source, the same
/// Roslyn already compiles, not the <c>Basalt.Razor.Vb</c> template
/// grammar.
/// </summary>
public static class FormClassRenameLookup
{
    /// <summary>
    /// The position of the identifier of the first class, module or
    /// structure declared anywhere in <paramref name="source"/> whose name
    /// matches <paramref name="name"/> case-insensitively (Visual Basic
    /// names are), or null when none does or the source fails to parse.
    /// </summary>
    public static int? FindDeclarationPosition(string source, string name)
    {
        var tree = VisualBasicSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        foreach (var node in root.DescendantNodes())
        {
            SyntaxToken? identifier = node switch
            {
                ClassStatementSyntax c => c.Identifier,
                ModuleStatementSyntax m => m.Identifier,
                StructureStatementSyntax s => s.Identifier,
                _ => null
            };

            if (identifier is { } token && token.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase))
                return token.SpanStart;
        }

        return null;
    }
}
