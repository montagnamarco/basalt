using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// The pair of ranges an editor keeps in step: an opening tag and its
/// closing tag.
///
/// Renaming one and forgetting the other is the commonest way to break
/// markup while editing, and the editor can prevent it entirely once it
/// knows which two names belong together.
/// </summary>
public sealed class VbHtmlLinkedEditingRangeHandler : LinkedEditingRangeHandlerBase
{
    private readonly DocumentStore _documents;

    public VbHtmlLinkedEditingRangeHandler(DocumentStore documents) => _documents = documents;

    public override Task<LinkedEditingRanges> Handle(
        LinkedEditingRangeParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return Task.FromResult<LinkedEditingRanges>(null!);

        var offset = VbHtmlCompletionHandler.OffsetOf(document.Text, request.Position);

        return Task.FromResult(Pair(document.Text, offset)!);
    }

    /// <summary>The opening and closing tag names at a position, if both exist.</summary>
    internal static LinkedEditingRanges? Pair(string text, int offset)
    {
        if (TagNameAt(text, offset) is not { } tag) return null;

        var partner = tag.IsClosing
            ? OpeningFor(text, tag)
            : ClosingFor(text, tag);

        if (partner is not { } other) return null;

        return new LinkedEditingRanges
        {
            Ranges = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Range>(
                RangeOf(text, tag.Start, tag.Name.Length),
                RangeOf(text, other, tag.Name.Length))
        };
    }

    /// <summary>A tag name the caret is inside, with where it starts.</summary>
    private static Tag? TagNameAt(string text, int offset)
    {
        var caret = Math.Clamp(offset, 0, text.Length);

        // Back to the "<" that opens this tag, giving up at anything that
        // means the caret is not in a tag name at all.
        var at = caret;

        while (at > 0 && IsNameCharacter(text[at - 1])) at--;

        var start = at;

        var closing = at > 0 && text[at - 1] == '/';

        if (closing) at--;

        if (at == 0 || text[at - 1] != '<') return null;

        var end = start;

        while (end < text.Length && IsNameCharacter(text[end])) end++;

        if (end == start) return null;

        // Only where the caret really is in the name.
        if (caret < start || caret > end) return null;

        return new Tag(text.Substring(start, end - start), start, closing);
    }

    /// <summary>
    /// Where the closing tag for an opening one starts, accounting for the
    /// same element nested inside itself.
    /// </summary>
    private static int? ClosingFor(string text, Tag tag)
    {
        // An element that never closes has no partner to keep in step.
        if (HtmlLanguageVoid(tag.Name)) return null;

        var depth = 0;
        var at = tag.Start + tag.Name.Length;

        while (at < text.Length)
        {
            var open = text.IndexOf('<', at);

            if (open < 0) return null;

            var closing = open + 1 < text.Length && text[open + 1] == '/';
            var nameAt = open + (closing ? 2 : 1);

            if (!Matches(text, nameAt, tag.Name)) { at = open + 1; continue; }

            if (closing)
            {
                if (depth == 0) return nameAt;

                depth--;
            }
            else if (!SelfClosing(text, open))
            {
                depth++;
            }

            at = open + 1;
        }

        return null;
    }

    /// <summary>Where the opening tag for a closing one starts.</summary>
    private static int? OpeningFor(string text, Tag tag)
    {
        var depth = 0;

        // Before this tag's own "<", which sits two characters back from the
        // name: starting on it would find this same closing tag and count it
        // as one more level of nesting, so the real opening never matched.
        var at = tag.Start - 3;

        while (at >= 0)
        {
            var open = text.LastIndexOf('<', Math.Min(at, text.Length - 1));

            if (open < 0) return null;

            var closing = open + 1 < text.Length && text[open + 1] == '/';
            var nameAt = open + (closing ? 2 : 1);

            if (!Matches(text, nameAt, tag.Name)) { at = open - 1; continue; }

            if (closing) depth++;
            else if (!SelfClosing(text, open))
            {
                if (depth == 0) return nameAt;

                depth--;
            }

            at = open - 1;
        }

        return null;
    }

    /// <summary>Whether the name sits at an offset, as a whole tag name.</summary>
    private static bool Matches(string text, int at, string name)
    {
        if (at + name.Length > text.Length) return false;

        if (string.Compare(text, at, name, 0, name.Length,
                StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var after = at + name.Length;

        return after >= text.Length || !IsNameCharacter(text[after]);
    }

    /// <summary>Whether a tag closes itself, so nothing is nested inside it.</summary>
    private static bool SelfClosing(string text, int open)
    {
        var end = text.IndexOf('>', open);

        return end > open && text[end - 1] == '/';
    }

    private static bool HtmlLanguageVoid(string name) =>
        Web.HtmlLanguage.VoidElements.Contains(name);

    private static bool IsNameCharacter(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':';

    private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range RangeOf(
        string text, int start, int length)
    {
        var (line, character) = VbHtmlSemanticTokensHandler.LineAndCharacterOf(text, start);

        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            new Position(line, character),
            new Position(line, character + length));
    }

    private readonly record struct Tag(string Name, int Start, bool IsClosing);

    protected override LinkedEditingRangeRegistrationOptions CreateRegistrationOptions(
        LinkedEditingRangeClientCapabilities capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml };
}
