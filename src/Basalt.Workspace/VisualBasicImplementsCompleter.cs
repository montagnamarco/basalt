using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>
/// Writes the members an Implements or Inherits line obliges a type to have,
/// when Enter finishes that line: "Implements IDisposable" followed by Enter
/// writes Dispose, as Visual Basic in Visual Studio does.
/// </summary>
/// <remarks>
/// The members are written by Roslyn's own "implement interface" and
/// "implement abstract class" fixes, not generated here, so signatures,
/// Implements clauses and bodies are the ones Visual Studio writes. The fixes
/// are chosen by equivalence key rather than by title: the titles are
/// localised, and on an Italian machine read "Implementa l'interfaccia".
/// </remarks>
internal static class VisualBasicImplementsCompleter
{
    /// <summary>A class does not implement a member of an interface it names.</summary>
    private const string MissingInterfaceMember = "BC30149";

    /// <summary>A class does not override a MustOverride member it inherits.</summary>
    private const string MissingMustOverride = "BC30610";

    /// <summary>
    /// One fix per interface, and a line names a handful at most. The limit
    /// only stops a fix that leaves its diagnostic in place from looping.
    /// </summary>
    private const int MaximumFixes = 8;

    /// <summary>
    /// The edits that write the members, against the document's text, or an
    /// empty list when the line is no Implements or Inherits statement or
    /// nothing is missing.
    /// </summary>
    /// <remarks>
    /// Edits rather than a new text: a fix can also add an Imports at the top
    /// of the file, and the editor must apply the two where they belong, not
    /// replace everything between them.
    /// </remarks>
    public static async Task<IReadOnlyList<TextChange>> ImplementAsync(
        Document document, int lineIndex, IReadOnlyList<CodeFixProvider> providers, CancellationToken ct)
    {
        var original = await document.GetTextAsync(ct).ConfigureAwait(false);
        if (lineIndex < 0 || lineIndex >= original.Lines.Count) return [];

        var changed = document;
        var changedText = original;

        // Tracked as a position: an Imports added above moves the line.
        var lineStart = original.Lines[lineIndex].Start;
        var fixedAlready = new HashSet<string>(StringComparer.Ordinal);

        for (var attempt = 0; attempt < MaximumFixes; attempt++)
        {
            var line = changedText.Lines.GetLineFromPosition(lineStart).LineNumber;
            var diagnostic = await FindMissingMembersAsync(changed, line, ct).ConfigureAwait(false);
            if (diagnostic is null) break;

            // A fix that leaves its own diagnostic behind would be applied
            // again and again, writing the members twice.
            if (!fixedAlready.Add(diagnostic.Id + diagnostic.GetMessage())) break;

            var fixedDocument = await ApplyFixAsync(changed, diagnostic, providers, ct).ConfigureAwait(false);
            if (fixedDocument is null) break;

            var fixedText = await fixedDocument.GetTextAsync(ct).ConfigureAwait(false);

            // Asked of the documents, not the texts: two texts from different
            // snapshots compare as one change covering the whole file.
            var stepChanges = await fixedDocument.GetTextChangesAsync(changed, ct).ConfigureAwait(false);

            // Every change is compared with where the line was before this
            // step, and the line moves once, by all of them together.
            lineStart += stepChanges
                .Where(change => change.Span.End <= lineStart)
                .Sum(change => (change.NewText?.Length ?? 0) - change.Span.Length);

            changed = fixedDocument;
            changedText = fixedText;
        }

        if (ReferenceEquals(changed, document)) return [];

        var changes = await changed.GetTextChangesAsync(document, ct).ConfigureAwait(false);

        // Roslyn writes CRLF on Windows whatever the file uses; what it
        // writes takes the line breaks of the line Enter finished.
        var finished = original.Lines[lineIndex];
        var newline = finished.EndIncludingLineBreak > finished.End
            ? original.ToString(TextSpan.FromBounds(finished.End, finished.EndIncludingLineBreak))
            : "\n";

        return [.. changes.Select(change => WithNewlines(change, original, newline))];
    }

    /// <summary>
    /// A change whose line breaks are the file's.
    /// </summary>
    /// <remarks>
    /// The comparison that produced the change may split a CRLF: a "\r"
    /// ending the new text before the file's own "\n", or a "\n" starting it
    /// after the file's own "\r". Those halves complete a break already there
    /// and are kept or dropped as that break needs, not doubled.
    /// </remarks>
    private static TextChange WithNewlines(TextChange change, SourceText original, string newline)
    {
        var text = change.NewText ?? "";

        var completesFollowing = text.EndsWith('\r') &&
            change.Span.End < original.Length && original[change.Span.End] == '\n';
        var completesPreceding = text.StartsWith('\n') &&
            change.Span.Start > 0 && original[change.Span.Start - 1] == '\r';

        var start = completesPreceding ? 1 : 0;
        var end = text.Length - (completesFollowing ? 1 : 0);
        var core = start < end ? text[start..end] : "";

        core = core.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (newline != "\n") core = core.Replace("\n", newline, StringComparison.Ordinal);

        var written = (completesPreceding ? "\n" : "") + core +
                      (completesFollowing && newline == "\r\n" ? "\r" : "");

        return new TextChange(change.Span, written);
    }

    private static async Task<Document?> ApplyFixAsync(
        Document document, Diagnostic diagnostic, IReadOnlyList<CodeFixProvider> providers, CancellationToken ct)
    {
        var actions = new List<CodeAction>();

        // Other fixes answer the same diagnostics ("make the class
        // MustInherit"), so only the implementing providers are asked.
        foreach (var provider in providers.Where(p =>
                     p.FixableDiagnosticIds.Contains(diagnostic.Id) && IsImplementingProvider(p)))
        {
            try
            {
                var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), ct);

                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A provider that fails leaves the line as typed, which is
                // what happened before members were written at all.
            }
        }

        var chosen = Choose(actions);
        if (chosen is null) return null;

        try
        {
            var operations = await chosen.GetOperationsAsync(ct).ConfigureAwait(false);
            var applied = operations.OfType<ApplyChangesOperation>().FirstOrDefault();

            return applied?.ChangedSolution.GetDocument(document.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As above: an ordinary Enter must not end in an error message.
            return null;
        }
    }

    /// <summary>
    /// The missing-member diagnostic that the statement on this line causes.
    /// </summary>
    private static async Task<Diagnostic?> FindMissingMembersAsync(
        Document document, int lineIndex, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return null;

        // Every Enter in Visual Basic asks, so the syntax answers first and
        // only an Implements or Inherits line costs a binding.
        // The statement Enter finished is the one ending on this line, which
        // may have begun lines above: Implements continues after a comma.
        var line = text.Lines[lineIndex];
        var content = line.ToString().TrimEnd();
        if (content.Trim().Length == 0) return null;

        var statement = root.FindToken(line.Start + content.Length - 1)
            .Parent?
            .AncestorsAndSelf()
            .OfType<InheritsOrImplementsStatementSyntax>()
            .FirstOrDefault();

        // Trailing comments belong to the last token, so a line ending in one
        // still finds the statement; it must end on this line all the same.
        if (statement is null || text.Lines.GetLineFromPosition(statement.Span.End).LineNumber != lineIndex)
            return null;
        if (statement.Parent is not TypeBlockSyntax type) return null;

        // "Implements IA," is still being written: the next line continues it.
        if (statement.GetLastToken(includeZeroWidth: true).IsMissing) return null;

        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model is null) return null;

        var diagnostics = model.GetDiagnostics(type.Span, ct);

        // Implements: the diagnostic sits on the interface's name, so only the
        // interfaces named on this line are implemented. Inherits: it sits on
        // the class's name, the only place Visual Basic reports it.
        return statement switch
        {
            ImplementsStatementSyntax => diagnostics.FirstOrDefault(d =>
                d.Id == MissingInterfaceMember && statement.Span.Contains(d.Location.SourceSpan)),
            InheritsStatementSyntax => diagnostics.FirstOrDefault(d =>
                d.Id == MissingMustOverride && type.BlockStatement.Span.Contains(d.Location.SourceSpan)),
            _ => null
        };
    }

    /// <summary>
    /// Roslyn's Visual Basic "implement interface" and "implement abstract
    /// class" providers, recognised by name because their types are internal.
    /// </summary>
    private static bool IsImplementingProvider(CodeFixProvider provider) =>
        provider.GetType().FullName is
            "Microsoft.CodeAnalysis.VisualBasic.ImplementInterface.VisualBasicImplementInterfaceCodeFixProvider" or
            "Microsoft.CodeAnalysis.VisualBasic.ImplementAbstractClass.VisualBasicImplementAbstractClassCodeFixProvider";

    /// <summary>
    /// Visual Basic writes IDisposable with the Dispose pattern and any other
    /// interface with plain members. The interface fixes also offer explicit,
    /// abstract and delegating variants; an abstract class has a single fix.
    /// </summary>
    private static CodeAction? Choose(IReadOnlyList<CodeAction> actions)
    {
        static bool Is(CodeAction action, string name) =>
            action.EquivalenceKey?.Contains("+" + name + ";", StringComparison.Ordinal) == true;

        var interfaceFix = actions.FirstOrDefault(a => Is(a, "ImplementInterfaceWithDisposePatternCodeAction"))
            ?? actions.FirstOrDefault(a => Is(a, "ImplementInterfaceCodeAction"));

        if (interfaceFix is not null) return interfaceFix;

        return actions.Count == 1 ? actions[0] : null;
    }
}
