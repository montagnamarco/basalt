using System.Collections.Concurrent;
using Basalt.Razor.Vb;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>A view the editor has open, and what the parser made of it.</summary>
public sealed record OpenDocument(string Uri, string Text, int Version)
{
    /// <summary>
    /// Whether this is a .vbpage page rather than a Razor view.
    /// </summary>
    /// <remarks>
    /// The two hold the same Visual Basic behind different delimiters — @ in
    /// a view, &lt;% %&gt; in a page — so they need different parsers and the
    /// same everything else.
    /// </remarks>
    public bool IsPage { get; } =
        Uri.EndsWith(".vbpage", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The parsed view, computed once and kept.
    ///
    /// Completion, diagnostics and navigation all want it, and parsing three
    /// times per keystroke would be felt.
    /// </summary>
    public VbHtmlDocument Parsed { get; } =
        Uri.EndsWith(".vbpage", StringComparison.OrdinalIgnoreCase)
            ? AsView(Basalt.Razor.Vb.Classic.VbPageParser.Parse(Text))
            : VbHtmlParser.Parse(Text);

    /// <summary>
    /// A parsed page, described with the view's own node kinds.
    /// </summary>
    /// <remarks>
    /// So that everything downstream — colouring, the outline, folding —
    /// works on one shape rather than two. The delimiters differ; what a
    /// block of code or a written value *is* does not.
    /// </remarks>
    private static VbHtmlDocument AsView(Basalt.Razor.Vb.Classic.VbPageParser.Page page)
    {
        var document = new VbHtmlDocument();

        foreach (var part in page.Parts)
        {
            switch (part)
            {
                case Basalt.Razor.Vb.Classic.VbPageParser.Markup markup:
                    document.Nodes.Add(new HtmlNode(markup.Text, markup.Position, markup.Line));
                    break;

                case Basalt.Razor.Vb.Classic.VbPageParser.Expression expression:
                    document.Nodes.Add(new ExpressionNode(
                        expression.Text, expression.Raw, expression.Position, expression.Line));
                    break;

                case Basalt.Razor.Vb.Classic.VbPageParser.Code code:
                    document.Nodes.Add(new StatementNode(
                        code.Text, code.Position, code.Line,
                        bodyLine: code.Line, bodyPosition: code.Position));
                    break;
            }
        }

        foreach (var diagnostic in page.Diagnostics) document.Diagnostics.Add(diagnostic);

        return document;
    }
}

/// <summary>
/// The views the editor currently has open.
///
/// The editor's copy is the truth while a file is open: what is on disk may be
/// older, and answering from disk would describe a file the user is not
/// looking at.
/// </summary>
public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<string, OpenDocument> _documents = new();

    public OpenDocument? Get(string uri) =>
        _documents.TryGetValue(uri, out var document) ? document : null;

    public OpenDocument Update(string uri, string text, int version)
    {
        var document = new OpenDocument(uri, text, version);

        _documents[uri] = document;

        return document;
    }

    public void Remove(string uri) => _documents.TryRemove(uri, out _);

    public IReadOnlyCollection<OpenDocument> All => [.. _documents.Values];
}
