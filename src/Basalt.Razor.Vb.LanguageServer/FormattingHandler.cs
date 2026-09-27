using Basalt.Extensibility;
using Basalt.Workspace.Web;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

// Both namespaces define these: ours describes an edit the IDE applies, the
// protocol's describes one sent over the wire.
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextEdit = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextEdit;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Laying out a template, on request and while typing.
///
/// The same formatter the IDE runs, rather than a second one written for the
/// protocol: an editor and the IDE disagreeing about where a line belongs is
/// worse than neither formatting at all, because the file changes shape
/// depending on which one last touched it.
///
/// Only the Visual Basic is laid out. The markup is left exactly as written —
/// where a tag breaks and how attributes wrap are opinions people hold
/// strongly, and a formatter that rearranged a template's HTML is one they
/// switch off, taking the code half with it.
/// </summary>
public sealed class VbHtmlFormattingHandler : DocumentFormattingHandlerBase
{
    private readonly DocumentStore _documents;
    private readonly VbHtmlFormattingProvider _formatter = new();

    public VbHtmlFormattingHandler(DocumentStore documents) => _documents = documents;

    public override async Task<TextEditContainer?> Handle(
        DocumentFormattingParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return null;

        var result = await _formatter
            .FormatDocumentAsync(new LanguageDocument(document.Uri, document.Text), ct)
            .ConfigureAwait(false);

        return Edits.ReplacingWholeDocument(document.Text, result.Text);
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForFormatting };
}

/// <summary>
/// Laying out the lines the editor asks about.
/// </summary>
/// <remarks>
/// Answered by formatting the whole document and returning only the edits that
/// land in the requested range: a code block is the unit that gets indented,
/// and half of one cannot be laid out sensibly — the indentation of a line
/// depends on the blocks above it, which a partial view does not show.
/// </remarks>
public sealed class VbHtmlRangeFormattingHandler : DocumentRangeFormattingHandlerBase
{
    private readonly DocumentStore _documents;
    private readonly VbHtmlFormattingProvider _formatter = new();

    public VbHtmlRangeFormattingHandler(DocumentStore documents) => _documents = documents;

    public override async Task<TextEditContainer> Handle(
        DocumentRangeFormattingParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        // An empty list rather than null: this base class, unlike the other
        // two, declares its result non-nullable, and to the client "no edits"
        // means the same thing either way.
        if (document is null) return new TextEditContainer();

        var start = Offsets.ToOffset(document.Text, request.Range.Start);
        var end = Offsets.ToOffset(document.Text, request.Range.End);

        var result = await _formatter
            .FormatRangeAsync(
                new LanguageDocument(document.Uri, document.Text), start, end - start, ct)
            .ConfigureAwait(false);

        return Edits.ReplacingWholeDocument(document.Text, result.Text) ?? new TextEditContainer();
    }

    protected override DocumentRangeFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentRangeFormattingCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForFormatting };
}

/// <summary>
/// The half that made Visual Basic feel like Visual Basic: the line tidies
/// itself as you leave it.
///
/// Typing <c>end if</c> and pressing Enter leaves <c>End If</c> behind, and
/// <c>if x=1 then</c> becomes <c>If x = 1 Then</c>. Nobody asked for a
/// formatting command — the editor asks on the author's behalf, on the
/// characters named below.
/// </summary>
public sealed class VbHtmlOnTypeFormattingHandler : DocumentOnTypeFormattingHandlerBase
{
    private readonly DocumentStore _documents;
    private readonly VbHtmlFormattingProvider _formatter = new();
    private readonly Func<LanguageDocument, int, CancellationToken, Task<FormattingResult>> _formatLine;

    public VbHtmlOnTypeFormattingHandler(DocumentStore documents)
    {
        _documents = documents;
        _formatLine = _formatter.FormatLineAsync;
    }

    internal VbHtmlOnTypeFormattingHandler(DocumentStore documents,
        Func<LanguageDocument, int, CancellationToken, Task<FormattingResult>> formatLine) : this(documents) =>
        _formatLine = formatLine;

    public override async Task<TextEditContainer?> Handle(
        DocumentOnTypeFormattingParams request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return null;

        var enter = request.Character == "\n";
        var snapshot = SourceText.From(document.Text);
        var finishedLine = enter ? request.Position.Line - 1 : request.Position.Line;
        if (finishedLine < 0 || finishedLine >= snapshot.Lines.Count) return null;

        // LSP reports the position after the trigger was inserted. The shared
        // formatter takes the line the author finished, before its delimiter.
        var caret = enter
            ? snapshot.Lines[finishedLine].End
            : Offsets.ToOffset(document.Text, request.Position);
        var languageDocument = new LanguageDocument(document.Uri, document.Text);

        var result = await _formatLine(languageDocument, caret, ct)
            .ConfigureAwait(false);

        // Then the block, if this line opened one. Enter after "If x Then"
        // puts "End If" below with the body indented between them, which is
        // what Visual Basic has always done and what a template never did:
        // blocks inside @Code had to be closed by hand while the same block
        // in a .vb file closed itself.
        //
        // Only on Enter. The trigger list also carries a space, and closing a
        // block halfway through typing its condition would be maddening.
        var text = enter
            ? await ClosedAsync(languageDocument with { Text = result.Text }, finishedLine, ct)
                .ConfigureAwait(false)
            : result.Text;

        // Nothing to say rather than an edit that changes nothing: an editor
        // that receives an identical replacement still moves the caret and
        // still marks the file dirty.
        ct.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_documents.Get(document.Uri), document)) return null;
        return text == document.Text
            ? null
            : Edits.ReplacingWholeDocument(document.Text, text);
    }

    /// <summary>
    /// The document with the block this line opened closed below it.
    /// </summary>
    private async Task<string> ClosedAsync(
        LanguageDocument document, int finishedLine, CancellationToken ct)
    {
        var closing = await _formatter
            .GetBlockClosingAsync(document, finishedLine, ct)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(closing)) return document.Text;

        var text = document.Text;
        var lines = SourceText.From(text).Lines;
        if (finishedLine + 1 >= lines.Count) return text;
        var header = lines[finishedLine];
        var current = lines[finishedLine + 1];
        var line = text[header.Start..header.End];
        var indent = line[..(line.Length - line.TrimStart().Length)];

        // The closing keyword lines up with what it opened, and the body sits
        // one level in from both — the shape a reader expects, and the same
        // one the document formatter produces.
        var body = indent + new string(' ', 4);
        var newline = text[header.End..header.EndIncludingLineBreak];
        if (newline.Length == 0) return text;
        var inserted = body + newline + indent + closing;
        var currentText = text[current.Start..current.End];
        // Reuse the blank line Enter already inserted. If Enter split in
        // front of existing code, leave that code and its delimiter intact.
        return string.IsNullOrWhiteSpace(currentText)
            ? text[..current.Start] + inserted + text[current.End..]
            : text[..current.Start] + inserted + newline + text[current.Start..];
    }

    protected override DocumentOnTypeFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentOnTypeFormattingCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = Selector.ForFormatting,

            // Enter is the moment the line is finished, and the protocol takes
            // one trigger plus a list of extras. A space is what completes a
            // keyword mid-line, which is how "end if" turns into "End If"
            // before the line is even left.
            FirstTriggerCharacter = "\n",
            MoreTriggerCharacter = new Container<string>(" ")
        };
}

/// <summary>
/// Turning a document rewrite into the edit the protocol asks for.
/// </summary>
internal static class Edits
{
    /// <summary>
    /// One edit spanning the whole file.
    /// </summary>
    /// <remarks>
    /// The formatter answers with the finished text rather than with a list of
    /// changes, and reconstructing a minimal diff from that would be a second
    /// implementation of something the formatter already knows. Editors
    /// collapse a whole-document replacement into their own undo entry, so the
    /// cost is one undo step, not a visibly rewritten file.
    /// </remarks>
    public static TextEditContainer? ReplacingWholeDocument(string original, string formatted)
    {
        if (original == formatted) return null;

        return new TextEditContainer(new TextEdit
        {
            Range = new Range(new Position(0, 0), Offsets.EndOf(original)),
            NewText = formatted
        });
    }
}

/// <summary>
/// Between the protocol's line-and-column positions and string offsets.
/// </summary>
internal static class Offsets
{
    public static int ToOffset(string text, Position position)
    {
        var line = 0;
        var offset = 0;

        while (line < position.Line && offset < text.Length)
        {
            var next = text.IndexOf('\n', offset);
            if (next < 0) return text.Length;

            offset = next + 1;
            line++;
        }

        // Clamped rather than trusted: a position past the end of its line is
        // what arrives when the editor's idea of the document is one keystroke
        // ahead of ours, and an unclamped offset would land on the next line.
        var lineEnd = text.IndexOf('\n', offset);
        if (lineEnd < 0) lineEnd = text.Length;

        return Math.Min(offset + position.Character, lineEnd);
    }

    public static Position EndOf(string text)
    {
        var line = 0;
        var lastBreak = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;

            line++;
            lastBreak = i;
        }

        return new Position(line, text.Length - lastBreak - 1);
    }
}
