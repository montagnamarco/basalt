using MediatR;
using Basalt.Razor.Vb;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Keeps the server's copy of each open view, and reports its problems.
///
/// Diagnostics are published on every change rather than on save: a view is
/// wrong while it is being written, and that is when saying so helps.
/// </summary>
public sealed class VbHtmlTextDocumentHandler : TextDocumentSyncHandlerBase
{
    private const string LanguageId = "vbhtml";

    private readonly DocumentStore _documents;
    private readonly ILanguageServerFacade _server;
    private readonly ProjectCompilation _compilation;

    public VbHtmlTextDocumentHandler(
        DocumentStore documents, ILanguageServerFacade server, ProjectCompilation compilation)
    {
        _documents = documents;
        _server = server;
        _compilation = compilation;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, LanguageId);

    public override Task<Unit> Handle(
        DidOpenTextDocumentParams request, CancellationToken ct)
    {
        Store(request.TextDocument.Uri, request.TextDocument.Text, request.TextDocument.Version);

        return Unit.Task;
    }

    public override Task<Unit> Handle(
        DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        var text = _documents.Get(uri)?.Text ?? "";

        // Incremental: only what changed arrives, so a keystroke in a large
        // template no longer sends the whole file. A change with no range is
        // still a whole-document replacement, which is what a client sends
        // when it gives up on tracking.
        foreach (var change in request.ContentChanges)
        {
            ct.ThrowIfCancellationRequested();

            text = change.Range is null
                ? change.Text
                : Apply(text, change.Range, change.Text);
        }

        Store(request.TextDocument.Uri, text, request.TextDocument.Version);

        return Unit.Task;
    }

    /// <summary>
    /// One ranged change applied to the text.
    ///
    /// Out-of-range positions are clamped rather than thrown on: a client and
    /// a server can disagree for a moment about how long a document is, and
    /// dropping the edit would leave the two copies permanently apart.
    /// </summary>
    internal static string Apply(
        string text, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range,
        string replacement)
    {
        var start = OffsetOf(text, range.Start);
        var end = OffsetOf(text, range.End);

        if (end < start) (start, end) = (end, start);

        return text.Substring(0, start) + replacement + text.Substring(end);
    }

    /// <summary>
    /// A line and character as an offset, clamped to the text.
    ///
    /// LSP counts characters in UTF-16 code units, which is what a .NET
    /// string is indexed by, so no conversion is needed.
    /// </summary>
    private static int OffsetOf(string text, Position position)
    {
        if (position.Line < 0) return 0;

        var line = 0;
        var at = 0;

        while (line < position.Line && at < text.Length)
        {
            var next = text.IndexOf('\n', at);

            if (next < 0) return text.Length;

            at = next + 1;
            line++;
        }

        if (line < position.Line) return text.Length;

        var endOfLine = at;

        while (endOfLine < text.Length
               && text[endOfLine] != '\n' && text[endOfLine] != '\r')
            endOfLine++;

        var character = Math.Max(0, position.Character);

        return Math.Min(at + character, endOfLine);
    }

    public override Task<Unit> Handle(
        DidSaveTextDocumentParams request, CancellationToken ct) => Unit.Task;

    public override Task<Unit> Handle(
        DidCloseTextDocumentParams request, CancellationToken ct)
    {
        _documents.Remove(request.TextDocument.Uri.ToString());

        // The editor stops showing diagnostics for a closed file only if the
        // server says there are none left.
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>()
        });

        return Unit.Task;
    }

    private void Store(DocumentUri uri, string text, int? version)
    {
        var document = _documents.Update(uri.ToString(), text, version ?? 0);

        // The parser's own findings first: they need no compilation and
        // arrive on the keystroke, which is when an unclosed block is worth
        // saying.
        Publish(uri, version, Describe(document.Parsed));

        // Then the compiler's, once it has an answer. Not awaited: asking
        // Roslyn about a whole project takes long enough to be felt, and an
        // editor that pauses on every keystroke is worse than one whose
        // squiggles arrive a moment late.
        _ = PublishSemanticAsync(uri, version, document);
    }

    /// <summary>
    /// Adds what the compiler found to what the parser found.
    /// </summary>
    /// <remarks>
    /// Without this the only errors a view ever showed were the two the
    /// parser can find — an unclosed block and a bad directive. A misspelt
    /// property or a wrong type went unmarked until the build.
    /// </remarks>
    private async Task PublishSemanticAsync(DocumentUri uri, int? version, OpenDocument document)
    {
        if (_compilation.Provider.Diagnostics is not { } diagnostics) return;

        try
        {
            var found = await diagnostics
                .GetDiagnosticsAsync(
                    new Basalt.Extensibility.LanguageDocument(
                        uri.GetFileSystemPath(), document.Text))
                .ConfigureAwait(false);

            // Only if the document is still the one asked about: a keystroke
            // during the wait makes these answers stale, and stale squiggles
            // sit under text that has moved.
            var current = _documents.Get(uri.ToString());

            if (current is null || current.Version != document.Version) return;

            Publish(uri, version, [.. Describe(document.Parsed), .. found.Select(Translate)]);
        }
        catch (Exception)
        {
            // A project that will not load is reported at startup; failing
            // here as well would put the same message under every keystroke.
        }
    }

    private void Publish(DocumentUri uri, int? version, IEnumerable<Diagnostic> diagnostics) =>
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Version = version,
            Diagnostics = new Container<Diagnostic>(diagnostics)
        });

    /// <summary>A compiler diagnostic as the protocol wants it.</summary>
    private static Diagnostic Translate(Basalt.Extensibility.Diagnostic diagnostic) =>
        new()
        {
            Message = diagnostic.Message,
            Code = diagnostic.Id,
            Severity = diagnostic.Severity switch
            {
                Basalt.Extensibility.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
                Basalt.Extensibility.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Information,
            },
            Source = "basalt",
            // One-based in the model, zero-based in the protocol: an
            // off-by-one here puts every squiggle a line above where it
            // belongs, which reads as the wrong line being wrong.
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(
                    Math.Max(0, diagnostic.Range.Start.Line - 1),
                    Math.Max(0, diagnostic.Range.Start.Column - 1)),
                new Position(
                    Math.Max(0, diagnostic.Range.End.Line - 1),
                    Math.Max(0, diagnostic.Range.End.Column - 1))),
        };

    /// <summary>Turns the parser's findings into what the editor understands.</summary>
    internal static IEnumerable<Diagnostic> Describe(VbHtmlDocument parsed) =>
        parsed.Diagnostics.Select(d => new Diagnostic
        {
            Code = d.Id,
            Message = d.Message,
            Severity = DiagnosticSeverity.Error,
            Source = "vbhtml",

            // The protocol counts from zero; the parser counts from one, as
            // the compiler does.
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(Math.Max(0, d.Line - 1), Math.Max(0, d.Column - 1)),
                new Position(Math.Max(0, d.Line - 1), Math.Max(0, d.Column)))
        });

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = Selector.ForVbHtml,
            Change = TextDocumentSyncKind.Incremental
        };
}

/// <summary>Which files this server speaks for.</summary>
internal static class Selector
{
    /// <summary>
    /// Both file types the server understands.
    /// </summary>
    /// <remarks>
    /// A .vbp page is the same Visual Basic in a different wrapper: the
    /// delimiters are &lt;% %&gt; rather than @, and everything the editor
    /// asks about — a name, a member, an error — has the same answer. Left
    /// out, a page got no colouring and no completion at all.
    /// </remarks>
    public static TextDocumentSelector ForVbHtml { get; } =
        new(
            TextDocumentFilter.ForPattern("**/*.vbhtml"),
            TextDocumentFilter.ForPattern("**/*.vbp"));

    /// <summary>
    /// The templates and plain Visual Basic, for formatting only.
    /// </summary>
    /// <remarks>
    /// A .vb file is not offered completion, diagnostics or navigation here:
    /// Rider answers those through ReSharper, and a second opinion competing
    /// with it would be worse than none. What ReSharper does not do is the
    /// typing behaviour Visual Basic has always had — "end if" becoming
    /// "End If", a block closing itself, a new line landing at the right
    /// depth — so that is all this claims.
    /// </remarks>
    public static TextDocumentSelector ForFormatting { get; } =
        new(
            TextDocumentFilter.ForPattern("**/*.vbhtml"),
            TextDocumentFilter.ForPattern("**/*.vbp"),
            TextDocumentFilter.ForPattern("**/*.vb"));
}
