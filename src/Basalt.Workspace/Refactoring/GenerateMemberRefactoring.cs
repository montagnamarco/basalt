using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Writing the members a class would otherwise be written by hand.
///
/// A constructor taking the fields, and a property in front of one. Roslyn
/// offers neither for Visual Basic without a code fix to hang them on, so
/// they are written here.
/// </summary>
public sealed class GenerateMemberRefactoring
{
    private readonly Solution _solution;

    public GenerateMemberRefactoring(Solution solution) => _solution = solution;

    /// <summary>
    /// Writes a constructor that takes the type's fields.
    ///
    /// The fields as they are declared, in order: a constructor whose
    /// parameters are in a different order from the fields is one whose calls
    /// are easy to get wrong.
    /// </summary>
    public async Task<RefactoringPreview> PreviewConstructorAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        const string title = "Generate Constructor";

        var found = await FindTypeAsync(filePath, position, title, ct).ConfigureAwait(false);

        if (found.Refusal is { } refusal) return refusal;

        var (type, text) = (found.Type!, found.Text!);

        var fields = Fields(type).ToList();

        if (fields.Count == 0)
            return RefactoringPreview.Refused(title, "This type has no fields to take.");

        if (type.Members.OfType<ConstructorBlockSyntax>().Any())
        {
            return RefactoringPreview.Refused(
                title, "This type already has a constructor.");
        }

        var lineEnding = LineEndingOf(text);
        var indent = IndentOf(text, type.SpanStart) + "    ";
        var body = indent + "    ";

        var parameters = string.Join(", ", fields.Select(
            f => $"{ParameterName(f.Name)} As {f.Type}"));

        var written = new System.Text.StringBuilder();

        written.Append(lineEnding);
        written.Append(indent).Append("Public Sub New(").Append(parameters).Append(')')
               .Append(lineEnding);

        foreach (var field in fields)
        {
            written.Append(body)
                   .Append("Me.").Append(field.Name)
                   .Append(" = ").Append(ParameterName(field.Name))
                   .Append(lineEnding);
        }

        written.Append(indent).Append("End Sub").Append(lineEnding);

        // Before End Class, which is where a constructor is looked for.
        var at = type.EndBlockStatement.SpanStart;
        var lineStart = text.Lines.GetLineFromPosition(at).Start;

        return new RefactoringPreview(
            $"Generate a constructor taking {fields.Count} field"
          + $"{(fields.Count == 1 ? "" : "s")}",
            [new FileChangePreview(filePath, text.ToString(),
                text.Replace(new TextSpan(lineStart, 0), written.ToString()).ToString())]);
    }

    /// <summary>
    /// Writes a property in front of the field the caret is on.
    ///
    /// The field keeps its name and stays private; the property takes the
    /// name without its leading underscore, which is what the convention
    /// expects.
    /// </summary>
    public async Task<RefactoringPreview> PreviewPropertyAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        const string title = "Generate Property";

        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                title, "Generating a property is only supported in Visual Basic so far.");
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null) return RefactoringPreview.Refused(title, "The file could not be read.");

        var token = root.FindToken(Math.Clamp(position, 0, Math.Max(0, text.Length - 1)));

        if (token.Parent?.FirstAncestorOrSelf<FieldDeclarationSyntax>() is not { } field)
            return RefactoringPreview.Refused(title, "Put the caret on a field.");

        var declarator = field.Declarators.FirstOrDefault();

        if (declarator is null || declarator.Names.Count != 1)
        {
            return RefactoringPreview.Refused(
                title, "This declaration names more than one field.");
        }

        var name = declarator.Names[0].Identifier.ValueText;
        var typeName = declarator.AsClause?.Type()?.ToString() ?? "Object";

        var propertyName = PropertyName(name);

        if (string.Equals(propertyName, name, StringComparison.Ordinal))
        {
            return RefactoringPreview.Refused(
                title,
                $"'{name}' would give a property of the same name, so one of "
              + "the two would have to be renamed first.");
        }

        var lineEnding = LineEndingOf(text);
        var indent = IndentOf(text, field.SpanStart);

        var written = new System.Text.StringBuilder();

        written.Append(lineEnding);
        written.Append(indent).Append("Public Property ").Append(propertyName)
               .Append(" As ").Append(typeName).Append(lineEnding);
        written.Append(indent).Append("    Get").Append(lineEnding);
        written.Append(indent).Append("        Return ").Append(name).Append(lineEnding);
        written.Append(indent).Append("    End Get").Append(lineEnding);
        written.Append(indent).Append("    Set(value As ").Append(typeName).Append(')')
               .Append(lineEnding);
        written.Append(indent).Append("        ").Append(name).Append(" = value")
               .Append(lineEnding);
        written.Append(indent).Append("    End Set").Append(lineEnding);
        written.Append(indent).Append("End Property").Append(lineEnding);

        var line = text.Lines.GetLineFromPosition(field.Span.End);

        return new RefactoringPreview(
            $"Generate property '{propertyName}' for '{name}'",
            [new FileChangePreview(filePath, text.ToString(),
                text.Replace(new TextSpan(line.EndIncludingLineBreak, 0),
                    written.ToString()).ToString())]);
    }

    /// <summary>The type the caret is in, or why there is none.</summary>
    private async Task<(TypeBlockSyntax? Type, SourceText? Text, RefactoringPreview? Refusal)>
        FindTypeAsync(string filePath, int position, string title, CancellationToken ct)
    {
        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
        {
            return (null, null,
                RefactoringPreview.Refused(title, "That file is not part of the solution."));
        }

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return (null, null, RefactoringPreview.Refused(
                title, "This is only supported in Visual Basic so far."));
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null)
        {
            return (null, null,
                RefactoringPreview.Refused(title, "The file could not be read."));
        }

        var token = root.FindToken(Math.Clamp(position, 0, Math.Max(0, text.Length - 1)));

        if (token.Parent?.FirstAncestorOrSelf<TypeBlockSyntax>() is not { } type)
        {
            return (null, null, RefactoringPreview.Refused(
                title, "Put the caret in a class or a structure."));
        }

        return (type, text, null);
    }

    /// <summary>The fields a type declares, with their names and types.</summary>
    private static IEnumerable<(string Name, string Type)> Fields(TypeBlockSyntax type) =>
        type.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(f => !f.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)
                                           || m.IsKind(SyntaxKind.SharedKeyword)))
            .SelectMany(f => f.Declarators.SelectMany(d => d.Names.Select(n =>
                (n.Identifier.ValueText, d.AsClause?.Type()?.ToString() ?? "Object"))));

    /// <summary>
    /// A field's name as a parameter.
    ///
    /// The leading underscore goes, which is what makes "Me.name = name"
    /// read as an assignment rather than a tautology.
    /// </summary>
    private static string ParameterName(string field)
    {
        var bare = field.TrimStart('_');

        return bare.Length == 0
            ? field
            : char.ToLowerInvariant(bare[0]) + bare[1..];
    }

    /// <summary>A field's name as a property: no underscore, capital first.</summary>
    private static string PropertyName(string field)
    {
        var bare = field.TrimStart('_');

        return bare.Length == 0
            ? field
            : char.ToUpperInvariant(bare[0]) + bare[1..];
    }

    private static string IndentOf(SourceText text, int position)
    {
        var line = text.Lines.GetLineFromPosition(position);

        return new string(' ', position - line.Start);
    }

    private static string LineEndingOf(SourceText text) =>
        text.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
