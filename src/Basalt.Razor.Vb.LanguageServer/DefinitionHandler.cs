using Basalt.Razor.Vb;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Jumping from a name in a template to where it is declared.
///
/// Within the view: a name used in an expression is usually assigned in a
/// Code block above it, and that is the definition worth reaching. The model
/// type leads to its declaration on the ModelType line.
/// </summary>
public sealed class VbHtmlDefinitionHandler : DefinitionHandlerBase
{
    private readonly DocumentStore _documents;

    private readonly ProjectCompilation? _compilation;

    public VbHtmlDefinitionHandler(DocumentStore documents, ProjectCompilation? compilation = null)
    {
        _documents = documents;
        _compilation = compilation;
    }

    public override async Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return null;

        var offset = VbHtmlCompletionHandler.OffsetOf(document.Text, request.Position);

        // A name the view declares itself first: found exactly in the
        // template, where Roslyn's answer would have to travel back through
        // the generated code's indentation and land a few characters off.
        var name = WordAt(document.Text, offset);

        var declared = name.Length > 0 ? FindDeclaration(document, name) : null;

        // Then Roslyn, once the solution is loaded: the model class, a helper
        // module, anything in the solution.
        if (declared is null && _compilation?.Provider.Navigation is { } navigation)
        {
            var found = await navigation
                .GoToDefinitionAsync(
                    new Basalt.Extensibility.LanguageDocument(
                        request.TextDocument.Uri.GetFileSystemPath(), document.Text),
                    offset, ct)
                .ConfigureAwait(false);

            if (found is not null) return new LocationOrLocationLinks(VbHtmlReferencesHandler.Translate(found));
        }

        if (declared is not { } position) return null;

        var (line, character) = VbHtmlSemanticTokensHandler.LineAndCharacterOf(
            document.Text, position);

        var location = new Location
        {
            Uri = request.TextDocument.Uri,
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(line, character),
                new Position(line, character + name.Length))
        };

        return new LocationOrLocationLinks(location);
    }

    /// <summary>
    /// Where a name is declared in the view, or null when it is not.
    ///
    /// The declaration is looked for in the Code blocks, which is where a view
    /// puts the things its markup then uses.
    /// </summary>
    internal static int? FindDeclaration(OpenDocument document, string name)
    {
        int? found = null;

        void Walk(IEnumerable<VbHtmlNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (found is not null) return;

                switch (node)
                {
                    case StatementNode statement:
                        var index = IndexOfDeclaration(statement.Code, name);

                        // From where the code starts: Position is the "@Code"
                        // keyword (or a later run's own start), and Code is
                        // the body, so an index into it is an offset from there.
                        if (index >= 0) found = statement.BodyPosition + index;
                        break;

                    case DirectiveNode directive
                        when directive.Name.Equals("ModelType", StringComparison.OrdinalIgnoreCase)
                          && directive.Value.Contains(name, StringComparison.Ordinal):
                        found = directive.Position;
                        break;

                    case BlockNode block:
                        Walk(block.Body);
                        foreach (var clause in block.Clauses) Walk(clause.Body);
                        break;
                }
            }
        }

        Walk(document.Parsed.Nodes);

        return found;
    }

    /// <summary>
    /// Where a Dim declares a name, or -1.
    ///
    /// Matched on the whole word so that "total" is not found inside
    /// "totalCount".
    /// </summary>
    private static int IndexOfDeclaration(string code, string name)
    {
        var search = 0;

        while (true)
        {
            var index = code.IndexOf(name, search, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return -1;

            var beforeIsBoundary = index == 0 || !IsWordCharacter(code[index - 1]);
            var after = index + name.Length;
            var afterIsBoundary = after >= code.Length || !IsWordCharacter(code[after]);

            if (beforeIsBoundary && afterIsBoundary)
            {
                var before = code[..index];

                // Only a declaration counts, not every mention of the name.
                if (before.TrimEnd().EndsWith("Dim", StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            search = index + 1;
        }
    }

    internal static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    internal static string WordAt(string text, int offset)
    {
        var caret = Math.Clamp(offset, 0, text.Length);

        var start = caret;
        while (start > 0 && IsWordCharacter(text[start - 1])) start--;

        var end = caret;
        while (end < text.Length && IsWordCharacter(text[end])) end++;

        return text[start..end];
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml };
}
