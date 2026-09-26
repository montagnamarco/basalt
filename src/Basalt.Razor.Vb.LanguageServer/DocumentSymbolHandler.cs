using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// The structure of a template, for the outline and the breadcrumb.
///
/// Read from the parse tree alone, so it works without a compilation: what a
/// template holds — its directives, its code blocks, its sections — is a
/// question about the file, not about the types it mentions.
/// </summary>
public sealed class VbHtmlDocumentSymbolHandler : DocumentSymbolHandlerBase
{
    private readonly DocumentStore _documents;

    public VbHtmlDocumentSymbolHandler(DocumentStore documents) => _documents = documents;

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null)
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);

        var symbols = SymbolsIn(document.Text);

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(
                symbols.Select(s => new SymbolInformationOrDocumentSymbol(s))));
    }

    /// <summary>What a template offers the outline, for tests and the handler.</summary>
    internal static IReadOnlyList<DocumentSymbol> SymbolsIn(string text)
    {
        var parsed = VbHtmlParser.Parse(text);
        var symbols = new List<DocumentSymbol>();

        foreach (var node in parsed.Nodes)
        {
            switch (node)
            {
                case DirectiveNode directive:
                    symbols.Add(Symbol(
                        $"@{directive.Name} {directive.Value}".TrimEnd(),
                        SymbolKind.Key, directive.Line));
                    break;

                case SectionNode section:
                    symbols.Add(Symbol($"@Section {section.Name}",
                        SymbolKind.Namespace, section.Line));
                    break;

                case FunctionsNode functions:
                    symbols.Add(Symbol("@Functions", SymbolKind.Module, functions.Line));
                    break;

                case StatementNode { IsContinuation: false } statement:
                    symbols.Add(Symbol("@Code", SymbolKind.Function, statement.Line));
                    break;

                case BlockNode block:
                    // Named by what it opens with, which is what a reader
                    // scanning an outline is looking for.
                    symbols.Add(Symbol(block.Opening, SymbolKind.Object, block.Line));
                    break;
            }
        }

        return symbols;
    }

    private static DocumentSymbol Symbol(string name, SymbolKind kind, int line)
    {
        // Lines are one-based in the parser and zero-based in the protocol.
        var at = new Position(Math.Max(0, line - 1), 0);
        // Qualified: System.Range is in scope too.
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(at, at);

        return new DocumentSymbol
        {
            Name = name,
            Kind = kind,
            Range = range,
            SelectionRange = range
        };
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml };
}
