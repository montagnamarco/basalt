using Microsoft.CodeAnalysis.VisualBasic;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LanguageDocument = Basalt.Extensibility.LanguageDocument;
using ProtocolRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using SourcePosition = Basalt.Extensibility.SourcePosition;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Renaming a name a view declares for itself: a variable of a code block,
/// a member of its @Functions.
/// </summary>
/// <remarks>
/// Every use is found through references, which carry each one back to the
/// template through the verified line mapping, and renamed on the text the
/// editor holds. A use is renamed only where the old name really stands,
/// in any case, as Visual Basic reads names; if one cannot be confirmed the
/// rename is refused whole rather than done by half.
///
/// Refused for now, by design:
/// - Names declared in the solution's Visual Basic, a model property say.
///   Renaming those rewrites other files, and the server cannot see what an
///   editor holds unsaved for them. Visual Studio and Rider do not even send
///   it the .vb files, so its copy can be stale. An edit computed from disk
///   and applied to a buffer that differs from it corrupts the file.
/// - Components (.vbrazor). What they declare is public: other components
///   set their parameters, and a rename here would leave those behind.
/// - A view the parser reports a problem in, even an unclosed tag. It is not
///   compiled while it has one, so there is nothing to check a rename
///   against.
/// </remarks>
internal sealed class ViewRenamer(DocumentStore documents, ProjectCompilation compilation)
{
    /// <summary>The name under the caret, when it can be renamed.</summary>
    public async Task<(string Name, ProtocolRange Range)?> PrepareAsync(DocumentUri uri, Position position, CancellationToken ct)
    {
        if (await UsesAsync(uri, position, ct).ConfigureAwait(false) is not { } plan) return null;

        return (plan.Word.Name, RangeOf(plan.Text, plan.Word.Start, plan.Word.Name.Length));
    }

    /// <summary>The edits renaming the name under the caret, or null when it cannot be renamed.</summary>
    public async Task<WorkspaceEdit?> RenameAsync(DocumentUri uri, Position position, string newName, CancellationToken ct)
    {
        if (!IsNewName(newName)) return null;

        if (await UsesAsync(uri, position, ct).ConfigureAwait(false) is not { } plan) return null;

        if (!await RenamedViewStillMeansTheSameAsync(uri.GetFileSystemPath(), plan, newName, ct).ConfigureAwait(false))
            return null;

        var edits = plan.Uses
            .Select(start => new TextEdit { Range = RangeOf(plan.Text, start, plan.Word.Name.Length), NewText = newName })
            .ToList();

        return new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(
                new WorkspaceEditDocumentChange(new TextDocumentEdit
                {
                    // Versioned: an editor whose buffer moved on since refuses
                    // the edit instead of applying it to the wrong text.
                    TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri, Version = plan.Version },
                    Edits = new TextEditContainer(edits)
                }))
        };
    }

    private sealed record Plan((string Name, int Start) Word, IReadOnlyList<int> Uses, string Text, int Version);

    /// <summary>Where the name under the caret is used, when it is a name this view declares.</summary>
    private async Task<Plan?> UsesAsync(DocumentUri uri, Position position, CancellationToken ct)
    {
        if (documents.Get(uri.ToString()) is not { } document) return null;
        if (compilation.Provider.Navigation is not { } navigation) return null;

        var path = uri.GetFileSystemPath();
        if (!path.EndsWith(".vbhtml", StringComparison.OrdinalIgnoreCase)) return null;

        var offset = VbHtmlCompletionHandler.OffsetOf(document.Text, position);
        if (WordAt(document.Text, offset) is not { } word) return null;

        var asked = new LanguageDocument(path, document.Text);

        var definition = await navigation.GoToDefinitionAsync(asked, offset, ct).ConfigureAwait(false);
        if (definition is null || !SamePath(definition.FilePath, path)) return null;

        var found = await navigation.FindReferencesAsync(asked, offset, ct).ConfigureAwait(false);
        var uses = new List<int>();

        foreach (var use in found)
        {
            // Declared here, used here: a use anywhere else means the name is
            // not the view's own after all.
            if (!SamePath(use.FilePath, path)) return null;

            if (OffsetOfLocation(document.Text, use.Range.Start) is not { } start) return null;

            if (WordAt(document.Text, start) is not { } there || there.Start != start ||
                !string.Equals(there.Name, word.Name, StringComparison.OrdinalIgnoreCase))
                return null;

            uses.Add(start);
        }

        if (uses.Count == 0 || !uses.Contains(word.Start)) return null;

        return new Plan(word, [.. uses.Distinct().Order()], document.Text, document.Version);
    }

    /// <summary>
    /// Whether the view, renamed, still parses the same, still uses the name
    /// in the same places, and compiles no worse.
    /// </summary>
    /// <remarks>
    /// A valid identifier can still break a view. "Code" is none of Visual
    /// Basic's keywords, yet "@Code" in markup opens a code block. A method
    /// renamed to "Write" silently takes over every Write(...) the generated
    /// view makes, with only a warning to say so. So the rename is tried on a
    /// copy first, and refused if anything about it changed.
    /// </remarks>
    private async Task<bool> RenamedViewStillMeansTheSameAsync(string path, Plan plan, string newName, CancellationToken ct)
    {
        if (compilation.Provider.Navigation is not { } navigation) return false;


        var growth = newName.Length - plan.Word.Name.Length;
        var expected = plan.Uses.Select((start, index) => start + (index * growth)).ToList();

        var renamed = plan.Text;

        foreach (var start in plan.Uses.OrderDescending())
            renamed = renamed.Remove(start, plan.Word.Name.Length).Insert(start, newName);

        if (VbHtmlParser.Parse(renamed).Diagnostics.Count != VbHtmlParser.Parse(plan.Text).Diagnostics.Count)
            return false;

        var after = new LanguageDocument(path, renamed);
        var found = await navigation.FindReferencesAsync(after, expected[0], ct).ConfigureAwait(false);

        var places = found
            .Where(use => SamePath(use.FilePath, path))
            .Select(use => OffsetOfLocation(renamed, use.Range.Start))
            .ToList();

        if (found.Count != places.Count || places.Any(place => place is null)) return false;
        if (!places.Select(place => place!.Value).Distinct().Order().SequenceEqual(expected)) return false;

        // Warnings included: shadowing the view's own members is only one.
        var before = await compilation.GetCompilerDiagnosticIdsAsync(new LanguageDocument(path, plan.Text), ct).ConfigureAwait(false);
        var now = await compilation.GetCompilerDiagnosticIdsAsync(after, ct).ConfigureAwait(false);

        if (before is null || now is null) return false;

        var counted = before.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());

        return now.GroupBy(id => id).All(group => group.Count() <= counted.GetValueOrDefault(group.Key));
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The identifier an offset touches: inside it, or right after it.</summary>
    internal static (string Name, int Start)? WordAt(string text, int offset)
    {
        var start = Math.Clamp(offset, 0, text.Length);

        while (start > 0 && IsNameCharacter(text[start - 1])) start--;

        var end = Math.Clamp(offset, 0, text.Length);

        while (end < text.Length && IsNameCharacter(text[end])) end++;

        if (end == start || char.IsDigit(text[start])) return null;

        return (text[start..end], start);
    }

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    /// <summary>
    /// A plain Visual Basic identifier that is not a reserved word: "Dim End = 1"
    /// does not compile. Contextual keywords, Text or Key, are ordinary names.
    /// </summary>
    private static bool IsNewName(string name) =>
        SyntaxFacts.IsValidIdentifier(name) && !SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(name));

    /// <summary>A one-based line and column as an offset, or null past the text.</summary>
    private static int? OffsetOfLocation(string text, SourcePosition position)
    {
        var offset = VbHtmlCompletionHandler.OffsetOf(text, new Position(position.Line - 1, position.Column - 1));

        return offset < text.Length ? offset : null;
    }

    private static ProtocolRange RangeOf(string text, int start, int length)
    {
        var (line, character) = VbHtmlSemanticTokensHandler.LineAndCharacterOf(text, start);

        return new ProtocolRange(new Position(line, character), new Position(line, character + length));
    }
}

/// <summary>textDocument/rename in a view.</summary>
public sealed class VbHtmlRenameHandler : RenameHandlerBase
{
    private readonly ViewRenamer _renamer;

    public VbHtmlRenameHandler(DocumentStore documents, ProjectCompilation compilation) =>
        _renamer = new ViewRenamer(documents, compilation);

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct) =>
        _renamer.RenameAsync(request.TextDocument.Uri, request.Position, request.NewName, ct);

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml, PrepareProvider = true };
}

/// <summary>
/// textDocument/prepareRename: the name the rename box starts from, or a
/// refusal before anything is typed for a name that cannot be renamed here.
/// </summary>
public sealed class VbHtmlPrepareRenameHandler : PrepareRenameHandlerBase
{
    private readonly ViewRenamer _renamer;

    public VbHtmlPrepareRenameHandler(DocumentStore documents, ProjectCompilation compilation) =>
        _renamer = new ViewRenamer(documents, compilation);

    public override async Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken ct)
    {
        if (await _renamer.PrepareAsync(request.TextDocument.Uri, request.Position, ct).ConfigureAwait(false)
            is not { } prepared) return null;

        return new RangeOrPlaceholderRange(new PlaceholderRange
        {
            Range = prepared.Range,
            Placeholder = prepared.Name
        });
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml, PrepareProvider = true };
}
