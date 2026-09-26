using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Basalt.Razor.Vb;

/// <summary>
/// An element a tag helper takes, with its attributes and content structured.
/// </summary>
/// <remarks>
/// The parser keeps markup as text; the tag helper runtime needs an element:
/// a name, attributes with their values, the content to run as a child, and
/// whether the tag closed itself. The binder builds this node from the text
/// and the nodes around it, and only for elements a tag helper matches.
/// </remarks>
public sealed class TagHelperElementNode : VbHtmlNode
{
    public TagHelperElementNode(
        string name,
        IReadOnlyList<TagHelperAttributeSyntax> attributes,
        TagHelperElementMode mode,
        List<VbHtmlNode> children,
        IReadOnlyList<TagHelperDescriptor> descriptors,
        int position,
        int line)
        : base(position, line)
    {
        Name = name;
        Attributes = attributes;
        Mode = mode;
        Children = children;
        Descriptors = descriptors;
    }

    /// <summary>The element name, without any @tagHelperPrefix.</summary>
    public string Name { get; }

    public IReadOnlyList<TagHelperAttributeSyntax> Attributes { get; }

    public TagHelperElementMode Mode { get; }

    /// <summary>The content between the tags, itself bound.</summary>
    public List<VbHtmlNode> Children { get; }

    /// <summary>The tag helpers that run on the element, in the order found.</summary>
    public IReadOnlyList<TagHelperDescriptor> Descriptors { get; }
}

/// <summary>
/// An attribute as written on a tag helper element.
/// </summary>
/// <param name="Value">
/// Literal text as HtmlNodes and @expressions as ExpressionNodes, in order;
/// empty for an attribute written without a value.
/// </param>
public sealed record TagHelperAttributeSyntax(
    string Name,
    IReadOnlyList<VbHtmlNode> Value,
    TagHelperQuoteStyle Style)
{
    /// <summary>Whether the value is text only, with no expression in it.</summary>
    public bool IsLiteral => Value.All(part => part is HtmlNode);

    /// <summary>The value's text, for a literal value.</summary>
    public string Text => string.Concat(Value.OfType<HtmlNode>().Select(part => part.Text));
}

/// <summary>How an attribute value was quoted, as the runtime is told.</summary>
public enum TagHelperQuoteStyle { DoubleQuotes, SingleQuotes, NoQuotes, Minimized }

/// <summary>How the element was written, as the runtime's TagMode names it.</summary>
public enum TagHelperElementMode { StartTagAndEndTag, SelfClosing, StartTagOnly }

/// <summary>
/// Finds the elements tag helpers take and replaces them with
/// <see cref="TagHelperElementNode"/>s.
/// </summary>
/// <remarks>
/// It works on one level of nodes at a time: an element and its closing tag
/// must sit at the same level, which C# Razor requires too — a tag opened
/// inside an @If and closed after it is not one element. An element no tag
/// helper matches is left exactly as it was.
/// </remarks>
public static class TagHelperBinder
{
    /// <summary>The elements HTML defines without a closing tag.</summary>
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>Binds a document's nodes in place, and the bodies of its blocks and sections.</summary>
    public static void Bind(VbHtmlDocument document, TagHelperCatalog catalog)
    {
        if (catalog.Descriptors.Count == 0) return;

        Replace(document.Nodes, Bind(document.Nodes, catalog, parent: null));
    }

    /// <summary>Whether a tree holds any element bound to a tag helper.</summary>
    public static bool UsesTagHelpers(IEnumerable<VbHtmlNode> nodes) => ElementsIn(nodes).Any();

    /// <summary>Every bound element in a tree, children included.</summary>
    public static IEnumerable<TagHelperElementNode> ElementsIn(IEnumerable<VbHtmlNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TagHelperElementNode element:
                    yield return element;

                    foreach (var inner in ElementsIn(element.Children)) yield return inner;
                    break;

                case BlockNode block:
                    foreach (var inner in ElementsIn(block.Body)) yield return inner;

                    foreach (var clause in block.Clauses)
                        foreach (var inner in ElementsIn(clause.Body)) yield return inner;
                    break;

                case SectionNode section:
                    foreach (var inner in ElementsIn(section.Body)) yield return inner;
                    break;
            }
        }
    }

    private static void Replace(List<VbHtmlNode> target, List<VbHtmlNode> with)
    {
        target.Clear();
        target.AddRange(with);
    }

    private static List<VbHtmlNode> Bind(List<VbHtmlNode> nodes, TagHelperCatalog catalog, string? parent)
    {
        var output = new List<VbHtmlNode>();
        var index = 0;
        var offset = 0;

        while (index < nodes.Count)
        {
            if (nodes[index] is not HtmlNode html)
            {
                output.Add(BindInside(nodes[index], catalog));
                index++;
                offset = 0;
                continue;
            }

            var open = NextStartTag(html.Text, offset);

            if (open < 0)
            {
                AddText(output, html, offset, html.Text.Length);
                index++;
                offset = 0;
                continue;
            }

            // "<!input …>" opts the element out of tag helpers; the "!" is
            // not written out, as C# Razor drops it.
            if (html.Text[open + 1] == '!' && !IsDeclaration(html.Text, open))
            {
                AddText(output, html, offset, open + 1);
                offset = open + 2;
                continue;
            }

            if (TryBindElement(nodes, index, open, catalog, parent, out var element, out var after))
            {
                AddText(output, html, offset, open);
                output.Add(element);
                (index, offset) = after;
                continue;
            }

            // Not a tag helper: written as text, and the scan carries on
            // past the "<".
            AddText(output, html, offset, open + 1);
            offset = open + 1;
        }

        return Merge(output);
    }

    /// <summary>Binds the bodies of a block or a section, which are levels of their own.</summary>
    private static VbHtmlNode BindInside(VbHtmlNode node, TagHelperCatalog catalog)
    {
        switch (node)
        {
            case BlockNode block:
                Replace(block.Body, Bind(block.Body, catalog, parent: null));

                foreach (var clause in block.Clauses)
                    Replace(clause.Body, Bind(clause.Body, catalog, parent: null));
                break;

            case SectionNode section:
                Replace(section.Body, Bind(section.Body, catalog, parent: null));
                break;
        }

        return node;
    }

    /// <summary>
    /// Tries to read an element from a start tag and bind it; the cursor after
    /// it when it succeeds.
    /// </summary>
    private static bool TryBindElement(
        List<VbHtmlNode> nodes,
        int index,
        int open,
        TagHelperCatalog catalog,
        string? parent,
        out TagHelperElementNode element,
        out (int Index, int Offset) after)
    {
        element = null!;
        after = default;

        var html = (HtmlNode)nodes[index];

        if (!TryReadStartTag(nodes, index, open, out var tag)) return false;

        // With a prefix set, only an element carrying it is a tag helper.
        var name = tag.Name;

        if (catalog.Prefix.Length > 0)
        {
            if (!name.StartsWith(catalog.Prefix, StringComparison.OrdinalIgnoreCase)) return false;

            name = name.Substring(catalog.Prefix.Length);
        }

        var descriptors = catalog.Matching(name, tag.Attributes.Select(a => a.Name).ToList(), parent);

        if (descriptors.Count == 0) return false;

        var position = html.Position + open;
        var line = html.Line + Breaks(html.Text, 0, open);

        // Without an end tag when written so, when HTML has none, or when a
        // tag helper declares none (TagStructure.WithoutEndTag): <partial
        // name="x"> waited for a closing tag that never comes.
        var withoutEndTag = descriptors.Any(d => d.Rules.Any(r => r.WithoutEndTag));

        if (tag.SelfClosing || VoidElements.Contains(name) || withoutEndTag)
        {
            var mode = tag.SelfClosing ? TagHelperElementMode.SelfClosing : TagHelperElementMode.StartTagOnly;

            element = new TagHelperElementNode(name, tag.Attributes, mode, [], descriptors, position, line);
            after = tag.After;
            return true;
        }

        if (!TryFindEndTag(nodes, tag.After, tag.Name, out var endStart, out var endAfter)) return false;

        var children = Slice(nodes, tag.After, endStart);

        element = new TagHelperElementNode(
            name, tag.Attributes, TagHelperElementMode.StartTagAndEndTag,
            Bind(children, catalog, name), descriptors, position, line);

        after = endAfter;
        return true;
    }

    /// <summary>A start tag as read: its name, attributes, and where it ends.</summary>
    private sealed record StartTag(
        string Name,
        List<TagHelperAttributeSyntax> Attributes,
        bool SelfClosing,
        (int Index, int Offset) After);

    /// <summary>
    /// Reads a start tag that may run over several nodes, an @expression in
    /// an attribute value being a node of its own.
    /// </summary>
    private static bool TryReadStartTag(List<VbHtmlNode> nodes, int index, int open, out StartTag tag)
    {
        tag = null!;

        var reader = new Reader(nodes, index, open + 1);
        var name = reader.ReadName();

        if (name.Length == 0) return false;

        var attributes = new List<TagHelperAttributeSyntax>();

        while (true)
        {
            reader.SkipWhitespace();

            if (reader.AtExpression || reader.AtOtherNode || reader.AtEnd) return false;

            var c = reader.Current;

            if (c == '>')
            {
                reader.Advance();
                tag = new StartTag(name, attributes, SelfClosing: false, reader.Cursor);
                return true;
            }

            if (c == '/')
            {
                reader.Advance();

                if (reader.AtEnd || reader.AtExpression || reader.AtOtherNode || reader.Current != '>') return false;

                reader.Advance();
                tag = new StartTag(name, attributes, SelfClosing: true, reader.Cursor);
                return true;
            }

            var attributeName = reader.ReadAttributeName();

            if (attributeName.Length == 0) return false;

            reader.SkipWhitespace();

            if (reader.AtExpression || reader.AtOtherNode || reader.AtEnd || reader.Current != '=')
            {
                attributes.Add(new TagHelperAttributeSyntax(attributeName, [], TagHelperQuoteStyle.Minimized));
                continue;
            }

            reader.Advance();
            reader.SkipWhitespace();

            if (reader.AtEnd || reader.AtOtherNode) return false;

            if (!reader.AtExpression && reader.Current is '"' or '\'')
            {
                var quote = reader.Current;
                reader.Advance();

                if (!reader.ReadValue(quote, out var value)) return false;

                attributes.Add(new TagHelperAttributeSyntax(
                    attributeName, value,
                    quote == '"' ? TagHelperQuoteStyle.DoubleQuotes : TagHelperQuoteStyle.SingleQuotes));
            }
            else
            {
                if (!reader.ReadValue(quote: null, out var value)) return false;

                attributes.Add(new TagHelperAttributeSyntax(attributeName, value, TagHelperQuoteStyle.NoQuotes));
            }
        }
    }

    /// <summary>
    /// Finds the closing tag of an element at the same level, counting nested
    /// elements of the same name.
    /// </summary>
    private static bool TryFindEndTag(
        List<VbHtmlNode> nodes,
        (int Index, int Offset) from,
        string name,
        out (int Index, int Offset) endStart,
        out (int Index, int Offset) endAfter)
    {
        endStart = default;
        endAfter = default;

        var depth = 0;

        for (var index = from.Index; index < nodes.Count; index++)
        {
            if (nodes[index] is not HtmlNode html) continue;

            var text = html.Text;
            var at = index == from.Index ? from.Offset : 0;

            while ((at = text.IndexOf('<', at)) >= 0)
            {
                var closing = at + 1 < text.Length && text[at + 1] == '/';
                var nameAt = closing ? at + 2 : at + 1;

                if (!NameAt(text, nameAt, name))
                {
                    at++;
                    continue;
                }

                var tagEnd = text.IndexOf('>', nameAt);

                if (closing)
                {
                    if (depth == 0)
                    {
                        if (tagEnd < 0) return false;

                        endStart = (index, at);
                        endAfter = (index, tagEnd + 1);
                        return true;
                    }

                    depth--;
                }
                else if (tagEnd < 0 || text[tagEnd - 1] != '/')
                {
                    depth++;
                }

                at = nameAt;
            }
        }

        return false;
    }

    /// <summary>Whether a name, and only that name, starts at an offset.</summary>
    private static bool NameAt(string text, int at, string name)
    {
        if (at + name.Length > text.Length) return false;
        if (string.Compare(text, at, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;

        var next = at + name.Length;

        return next >= text.Length || !IsNameCharacter(text[next]);
    }

    /// <summary>The nodes between two cursors, the HtmlNodes at either end cut to fit.</summary>
    private static List<VbHtmlNode> Slice(List<VbHtmlNode> nodes, (int Index, int Offset) from, (int Index, int Offset) to)
    {
        var slice = new List<VbHtmlNode>();

        for (var index = from.Index; index <= to.Index && index < nodes.Count; index++)
        {
            var node = nodes[index];

            if (node is not HtmlNode html)
            {
                if (index < to.Index) slice.Add(node);
                continue;
            }

            var start = index == from.Index ? from.Offset : 0;
            var end = index == to.Index ? to.Offset : html.Text.Length;

            if (end > start) AddText(slice, html, start, end);
        }

        return slice;
    }

    /// <summary>
    /// Whether "<!" here opens a declaration — "<!DOCTYPE html>" — rather
    /// than opting an element out; its "!" belongs to the page.
    /// </summary>
    private static bool IsDeclaration(string text, int open) =>
        string.Compare(text, open + 2, "DOCTYPE", 0, "DOCTYPE".Length, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>Where the next start tag in a run of text begins, or -1.</summary>
    private static int NextStartTag(string text, int from)
    {
        for (var at = text.IndexOf('<', from); at >= 0 && at + 1 < text.Length; at = text.IndexOf('<', at + 1))
        {
            var next = text[at + 1];

            // Inside an HTML comment nothing is an element: a tag helper
            // there would run, and whatever it wrote would sit in the comment.
            if (string.CompareOrdinal(text, at, "<!--", 0, 4) == 0)
            {
                var close = text.IndexOf("-->", at + 4, StringComparison.Ordinal);

                if (close < 0) return -1;

                at = close + 2;
                continue;
            }

            if (char.IsLetter(next)) return at;
            if (next == '!' && at + 2 < text.Length && char.IsLetter(text[at + 2])) return at;
        }

        return -1;
    }

    /// <summary>Adds part of an HtmlNode, keeping its place in the template.</summary>
    private static void AddText(List<VbHtmlNode> output, HtmlNode html, int start, int end)
    {
        if (end <= start) return;

        var text = html.Text.Substring(start, end - start);

        // "</!div>" closes an element opted out with "<!div>"; the "!" is the
        // template's mark, not the page's, and left in the closing tag no
        // longer closes anything.
        var withoutMarks = StripOptOutMarks(text);

        if (start == 0 && end == html.Text.Length && withoutMarks == text)
        {
            output.Add(html);
            return;
        }

        output.Add(new HtmlNode(withoutMarks, html.Position + start, html.Line + Breaks(html.Text, 0, start)));
    }

    /// <summary>Drops the "!" of "&lt;/!name&gt;", the closing half of an opt-out.</summary>
    private static string StripOptOutMarks(string text)
    {
        if (text.IndexOf("</!", StringComparison.Ordinal) < 0) return text;

        var stripped = new StringBuilder(text.Length);

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '!' && at >= 2 && text[at - 1] == '/' && text[at - 2] == '<' &&
                at + 1 < text.Length && char.IsLetter(text[at + 1]))
                continue;

            stripped.Append(text[at]);
        }

        return stripped.ToString();
    }

    /// <summary>Joins neighbouring text pieces the scan split, so a page is not written a character at a time.</summary>
    private static List<VbHtmlNode> Merge(List<VbHtmlNode> nodes)
    {
        var merged = new List<VbHtmlNode>();

        foreach (var node in nodes)
        {
            if (node is HtmlNode html && merged.Count > 0 && merged[merged.Count - 1] is HtmlNode previous &&
                previous.Position + previous.Text.Length == html.Position)
            {
                merged[merged.Count - 1] = new HtmlNode(previous.Text + html.Text, previous.Position, previous.Line);
                continue;
            }

            merged.Add(node);
        }

        return merged;
    }

    private static int Breaks(string text, int from, int to)
    {
        var breaks = 0;

        for (var at = from; at < to && at < text.Length; at++)
            if (text[at] == '\n') breaks++;

        return breaks;
    }

    private static bool IsNameCharacter(char c) =>
        char.IsLetterOrDigit(c) || c is '-' or '_' or ':' or '.';

    /// <summary>
    /// Reads characters across a list of nodes: the text of HtmlNodes, with an
    /// ExpressionNode standing where the parser cut the markup at an "@".
    /// </summary>
    private sealed class Reader(List<VbHtmlNode> nodes, int index, int offset)
    {
        private int _index = index;
        private int _offset = offset;

        public (int Index, int Offset) Cursor => (_index, _offset);

        private HtmlNode? Html => _index < nodes.Count ? nodes[_index] as HtmlNode : null;

        public bool AtEnd
        {
            get
            {
                Normalise();
                return _index >= nodes.Count;
            }
        }

        public bool AtExpression
        {
            get
            {
                Normalise();
                return _index < nodes.Count && nodes[_index] is ExpressionNode;
            }
        }

        public bool AtOtherNode
        {
            get
            {
                Normalise();
                return _index < nodes.Count && nodes[_index] is not HtmlNode and not ExpressionNode;
            }
        }

        public char Current
        {
            get
            {
                Normalise();
                return Html!.Text[_offset];
            }
        }

        public void Advance() => _offset++;

        /// <summary>Moves off the end of an HtmlNode onto whatever follows it.</summary>
        private void Normalise()
        {
            while (_index < nodes.Count && nodes[_index] is HtmlNode html && _offset >= html.Text.Length)
            {
                _index++;
                _offset = 0;
            }
        }

        public void SkipWhitespace()
        {
            while (!AtEnd && !AtExpression && !AtOtherNode && char.IsWhiteSpace(Current)) Advance();
        }

        public string ReadName()
        {
            var name = new StringBuilder();

            while (!AtEnd && !AtExpression && !AtOtherNode && IsNameCharacter(Current))
            {
                name.Append(Current);
                Advance();
            }

            return name.ToString();
        }

        public string ReadAttributeName()
        {
            var name = new StringBuilder();

            while (!AtEnd && !AtExpression && !AtOtherNode &&
                   !char.IsWhiteSpace(Current) && Current is not ('=' or '>' or '/' or '"' or '\''))
            {
                name.Append(Current);
                Advance();
            }

            return name.ToString();
        }

        /// <summary>
        /// Reads a value up to its closing quote, or to whitespace or the end
        /// of the tag when unquoted, collecting text and expressions in order.
        /// </summary>
        public bool ReadValue(char? quote, out List<VbHtmlNode> value)
        {
            var collected = new List<VbHtmlNode>();
            value = collected;

            var text = new StringBuilder();
            var textStart = -1;
            var textLine = 0;

            void FlushText()
            {
                if (text.Length == 0) return;

                collected.Add(new HtmlNode(text.ToString(), textStart, textLine));
                text.Clear();
            }

            while (true)
            {
                if (AtEnd || AtOtherNode) return false;

                if (AtExpression)
                {
                    FlushText();
                    value.Add(nodes[_index]);
                    _index++;
                    _offset = 0;
                    continue;
                }

                var c = Current;

                if (quote is { } closing ? c == closing : char.IsWhiteSpace(c) || c == '>')
                {
                    FlushText();

                    if (quote is not null) Advance();

                    return true;
                }

                if (text.Length == 0)
                {
                    textStart = Html!.Position + _offset;
                    textLine = Html.Line + Breaks(Html.Text, 0, _offset);
                }

                text.Append(c);
                Advance();
            }
        }
    }
}
