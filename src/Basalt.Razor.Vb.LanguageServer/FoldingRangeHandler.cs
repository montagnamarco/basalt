using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// What can be collapsed in a template.
///
/// A long @Code block or a section is exactly what a reader wants out of the
/// way while looking at the markup around it. Like the outline, this is a
/// question about the file rather than about the types it mentions, so it
/// needs no compilation.
/// </summary>
public sealed class VbHtmlFoldingRangeHandler : FoldingRangeHandlerBase
{
    private readonly DocumentStore _documents;

    public VbHtmlFoldingRangeHandler(DocumentStore documents) => _documents = documents;

    public override Task<Container<FoldingRange>?> Handle(
        FoldingRangeRequestParam request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return Task.FromResult<Container<FoldingRange>?>(null);

        return Task.FromResult<Container<FoldingRange>?>(
            new Container<FoldingRange>(RangesIn(document.Text)));
    }

    /// <summary>What can be folded, for tests and the handler.</summary>
    internal static IReadOnlyList<FoldingRange> RangesIn(string text)
    {
        var parsed = VbHtmlParser.Parse(text);
        var ranges = new List<FoldingRange>();

        // Where each node ends is found from the text, since a node records
        // where it began and how long its own content is, not where the
        // construct closed.
        foreach (var node in parsed.Nodes)
        {
            switch (node)
            {
                case StatementNode statement:
                    Add(statement.Line, EndLine(text, statement.Position, "End Code"));
                    break;

                case SectionNode section:
                    Add(section.Line, EndLine(text, section.Position, "End Section"));
                    break;

                case FunctionsNode functions:
                    Add(functions.Line, EndLine(text, functions.Position, "End Functions"));
                    break;

                case BlockNode block:
                    Add(block.Line, EndLine(text, block.Position, block.Closing));
                    break;
            }
        }

        return ranges;

        void Add(int startLine, int endLine)
        {
            // A construct that opens and closes on one line has nothing to
            // fold, and offering it would put an arrow beside every line.
            if (endLine <= startLine) return;

            ranges.Add(new FoldingRange
            {
                StartLine = startLine - 1,
                EndLine = endLine - 1,
                Kind = FoldingRangeKind.Region
            });
        }
    }

    /// <summary>The line a construct closes on, counted from one.</summary>
    private static int EndLine(string text, int from, string closing)
    {
        var at = text.IndexOf(closing, Math.Min(from, text.Length), StringComparison.OrdinalIgnoreCase);

        if (at < 0) return -1;

        var line = 1;

        for (var i = 0; i < at; i++)
            if (text[i] == '\n') line++;

        return line;
    }

    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml };
}
