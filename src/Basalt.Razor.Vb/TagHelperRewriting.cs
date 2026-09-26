using System.Text;

namespace Basalt.Razor.Vb;

/// <summary>
/// Turns the tag helper attributes a Razor author writes into the pieces that
/// produce the same markup.
/// </summary>
/// <remarks>
/// ASP.NET Core's tag helpers work by parsing the markup into a tree and
/// running a class over each element. Basalt's parser keeps markup as text,
/// so the whole mechanism is not available; what is available is the handful
/// of helpers that carry real weight in an ordinary site, rewritten in place.
///
/// The alternative was leaving them alone, which is worse than not supporting
/// them: <c>asp-action</c> reaches the browser as an unknown attribute, the
/// anchor has no href, and the link silently goes nowhere.
/// </remarks>
public static class TagHelperRewriting
{
    /// <summary>
    /// One piece of a rewritten run of markup: literal text, or an expression
    /// whose value is written out.
    /// </summary>
    public sealed record Piece(string Text, bool IsExpression);

    /// <summary>
    /// What a rewrite produces: an attribute and the expression for its value,
    /// or, for a textarea, the element's content.
    /// </summary>
    private readonly record struct Rewritten(string Attribute, string Expression)
    {
        public bool IsContent => Attribute.Length == 0;
    }

    /// <summary>
    /// A tag's attributes as read: in order, with a null value for one written
    /// without any, and where reading stopped.
    /// </summary>
    /// <remarks>
    /// <see cref="Rest"/> is what could not be read — the end of a tag the
    /// parser cut at an "@" — and is passed through untouched.
    /// </remarks>
    private sealed record Attributes(Dictionary<string, string?> Values, string Rest)
    {
        public bool TryGetValue(string name, out string value)
        {
            value = "";

            if (!Values.TryGetValue(name, out var found) || found is null) return false;

            value = found;
            return true;
        }

        public bool ContainsKey(string name) => Values.ContainsKey(name);
    }

    /// <summary>
    /// Splits a run of markup into the pieces that produce it.
    /// </summary>
    /// <remarks>
    /// A run with no tag helpers comes back as one literal piece, so the
    /// caller needs no special case for the ordinary path.
    /// </remarks>
    public static IReadOnlyList<Piece> Rewrite(string markup)
    {
        if (markup.IndexOf("asp-", StringComparison.OrdinalIgnoreCase) < 0)
            return [new Piece(markup, IsExpression: false)];

        var pieces = new List<Piece>();
        var literal = new StringBuilder();
        var index = 0;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;

            pieces.Add(new Piece(literal.ToString(), IsExpression: false));
            literal.Clear();
        }

        void Add(IEnumerable<Piece> rewritten)
        {
            foreach (var piece in rewritten)
            {
                if (piece.IsExpression)
                {
                    FlushLiteral();
                    pieces.Add(piece);
                }
                else
                {
                    literal.Append(piece.Text);
                }
            }
        }

        while (index < markup.Length)
        {
            var tagStart = markup.IndexOf('<', index);
            var tagEnd = tagStart < 0 ? -1 : markup.IndexOf('>', tagStart);

            if (tagStart < 0)
            {
                literal.Append(markup, index, markup.Length - index);
                break;
            }

            literal.Append(markup, index, tagStart - index);

            if (tagEnd < 0)
            {
                // A tag the run ends inside: the parser cut the markup at an
                // "@", as in <a asp-action="Edit" class="@css">. Its asp-*
                // attributes are still rewritten; the rest goes through as
                // written and the next node finishes the tag.
                Add(RewriteTag(markup.Substring(tagStart), isWhole: false));
                break;
            }

            Add(RewriteTag(markup.Substring(tagStart, tagEnd - tagStart + 1), isWhole: true));

            index = tagEnd + 1;
        }

        FlushLiteral();

        return pieces;
    }

    private static IReadOnlyList<Piece> RewriteTag(string tag, bool isWhole)
    {
        if (tag.IndexOf("asp-", StringComparison.OrdinalIgnoreCase) < 0)
            return [new Piece(tag, IsExpression: false)];

        var attributes = ReadAttributes(tag, isWhole);

        if (attributes is null || attributes.Values.Count == 0)
            return [new Piece(tag, IsExpression: false)];

        var name = TagName(tag);

        var rewritten = name.ToLowerInvariant() switch
        {
            "a" => RewriteAnchor(attributes),
            "form" => RewriteForm(attributes),
            "input" => RewriteInput(attributes),
            "label" => RewriteLabel(attributes),
            "textarea" => RewriteTextArea(attributes),
            _ => null,
        };

        // A textarea's value is its content, which comes after the tag; a tag
        // the run ends inside has no end to put it after.
        if (rewritten is { IsContent: true } && !isWhole) rewritten = null;

        if (rewritten is null)
            return [new Piece(RemoveAspAttributes(tag, name, attributes, isWhole), IsExpression: false)];

        return Rebuild(tag, name, attributes, rewritten.Value, isWhole);
    }

    /// <summary>The href an anchor's routing attributes ask for.</summary>
    private static Rewritten? RewriteAnchor(Attributes attributes)
    {
        if (attributes.TryGetValue("asp-page", out var page))
            return new("href", $"Url.Page({Literal(page)})");

        var hasAction = attributes.TryGetValue("asp-action", out var action);
        var hasController = attributes.TryGetValue("asp-controller", out var controller);

        if (!hasAction && !hasController) return null;

        // Either may be left out: an omitted controller means the current one,
        // which is what Url.Action does with Nothing.
        var actionArgument = hasAction ? Literal(action) : "Nothing";
        var controllerArgument = hasController ? Literal(controller) : "Nothing";

        return new("href", $"Url.Action({actionArgument}, {controllerArgument})");
    }

    /// <summary>The action a form's routing attributes ask for.</summary>
    private static Rewritten? RewriteForm(Attributes attributes)
    {
        if (attributes.TryGetValue("asp-page", out var page))
            return new("action", $"Url.Page({Literal(page)})");

        var hasAction = attributes.TryGetValue("asp-action", out var action);
        var hasController = attributes.TryGetValue("asp-controller", out var controller);

        if (!hasAction && !hasController) return null;

        return new("action", $"Url.Action(" +
            $"{(hasAction ? Literal(action) : "Nothing")}, " +
            $"{(hasController ? Literal(controller) : "Nothing")})");
    }

    /// <summary>
    /// What <c>asp-for</c> on an input asks for.
    /// </summary>
    /// <remarks>
    /// The framework's own helper writes a name, a value, an id and the
    /// validation attributes for the property's type. This writes the value,
    /// which is what makes a form show what is already there — the half a
    /// page is visibly wrong without.
    ///
    /// The name is written too, because a field that posts back under no name
    /// is a field the model binder never sees.
    /// </remarks>
    private static Rewritten? RewriteInput(Attributes attributes)
    {
        if (!attributes.TryGetValue("asp-for", out var expression)) return null;

        return new("value", $"Model.{expression}");
    }

    /// <summary>The field a label points at, by the id that field is given.</summary>
    private static Rewritten? RewriteLabel(Attributes attributes)
    {
        if (!attributes.TryGetValue("asp-for", out var expression)) return null;

        // The property's own name: the framework reads a DisplayName
        // attribute where there is one, which needs the compilation this
        // rewriting deliberately does without.
        return new("for", Literal(SanitizedId(expression)));
    }

    /// <summary>
    /// A textarea shows its value as content, between the tags.
    /// </summary>
    /// <remarks>
    /// It was written as a value attribute, which a textarea does not have:
    /// the browser ignored it and every edit form opened with its text areas
    /// empty.
    /// </remarks>
    private static Rewritten? RewriteTextArea(Attributes attributes)
    {
        if (!attributes.TryGetValue("asp-for", out var expression)) return null;

        return new("", $"Model.{expression}");
    }

    /// <summary>
    /// An element id made from a property path, the way ASP.NET Core makes it.
    /// </summary>
    /// <remarks>
    /// Address.City becomes Address_City: a dot in an id is legal HTML but a
    /// class selector in CSS and jQuery, so the framework replaces it, and a
    /// label written for the field must name the same id.
    /// </remarks>
    private static string SanitizedId(string path)
    {
        var id = new StringBuilder(path.Length);

        foreach (var c in path)
            id.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or ':' ? c : '_');

        return id.ToString();
    }

    /// <summary>
    /// The tag with its asp-* attributes replaced by what they produce.
    /// </summary>
    private static IReadOnlyList<Piece> Rebuild(
        string tag, string name, Attributes attributes, Rewritten rewritten, bool isWhole)
    {
        var before = new StringBuilder("<").Append(name);

        // A field bound with asp-for needs a name to post back under, and an
        // id for a label to point at. Both are literal text — only the value
        // has to be evaluated.
        if (attributes.TryGetValue("asp-for", out var bound) &&
            name is "input" or "textarea" or "select")
        {
            if (!attributes.ContainsKey("name"))
                before.Append(" name=\"").Append(bound).Append('"');

            if (!attributes.ContainsKey("id"))
                before.Append(" id=\"").Append(SanitizedId(bound)).Append('"');
        }

        foreach (var pair in attributes.Values)
        {
            // The rewritten attribute replaces any written by hand: an anchor
            // with both asp-action and href had two, and the browser used the
            // first.
            if (pair.Key.StartsWith("asp-", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(pair.Key, rewritten.Attribute, StringComparison.OrdinalIgnoreCase))
                continue;

            AppendAttribute(before, pair.Key, pair.Value);
        }

        if (rewritten.IsContent)
        {
            before.Append(tag.EndsWith("/>", StringComparison.Ordinal) ? " />" : ">");

            return
            [
                new Piece(before.ToString(), IsExpression: false),
                new Piece(rewritten.Expression, IsExpression: true),
            ];
        }

        before.Append(' ').Append(rewritten.Attribute).Append("=\"");

        var after = !isWhole
            ? "\"" + attributes.Rest
            : tag.EndsWith("/>", StringComparison.Ordinal) ? "\" />" : "\">";

        return
        [
            new Piece(before.ToString(), IsExpression: false),
            new Piece(rewritten.Expression, IsExpression: true),
            new Piece(after, IsExpression: false),
        ];
    }

    /// <summary>
    /// The tag with its asp-* attributes dropped.
    /// </summary>
    /// <remarks>
    /// For the helpers not implemented. Dropping them is not support, but it
    /// keeps invalid attributes out of the page, and the markup stays valid.
    /// </remarks>
    private static string RemoveAspAttributes(
        string tag, string name, Attributes attributes, bool isWhole)
    {
        if (!attributes.Values.Keys.Any(k => k.StartsWith("asp-", StringComparison.OrdinalIgnoreCase)))
            return tag;

        var builder = new StringBuilder("<").Append(name);

        foreach (var pair in attributes.Values)
        {
            if (pair.Key.StartsWith("asp-", StringComparison.OrdinalIgnoreCase)) continue;

            AppendAttribute(builder, pair.Key, pair.Value);
        }

        if (!isWhole)
            builder.Append(attributes.Rest);
        else
            builder.Append(tag.EndsWith("/>", StringComparison.Ordinal) ? " />" : ">");

        return builder.ToString();
    }

    /// <summary>An attribute as markup, bare when it was written without a value.</summary>
    private static void AppendAttribute(StringBuilder builder, string name, string? value)
    {
        builder.Append(' ').Append(name);

        if (value is null) return;

        // Single quotes around a value that holds a double one, which is how
        // it can only have been written.
        var quote = value.IndexOf('"') >= 0 ? '\'' : '"';

        builder.Append('=').Append(quote).Append(value).Append(quote);
    }

    private static string TagName(string tag)
    {
        var start = 1;

        if (start < tag.Length && tag[start] == '/') start++;

        var end = start;

        while (end < tag.Length && (char.IsLetterOrDigit(tag[end]) || tag[end] == '-')) end++;

        return tag.Substring(start, end - start);
    }

    /// <summary>
    /// The tag's attributes, in the order they were written.
    /// </summary>
    /// <remarks>
    /// Quoted with either quote, unquoted, or with no value at all:
    /// <c>&lt;input asp-for="Name" required&gt;</c> used to stop the reading
    /// at "required", and the asp-for went to the browser as it was.
    ///
    /// Null when a whole tag could not be understood — rebuilding a tag only
    /// partly read would lose the rest of it. In a tag the run ends inside,
    /// reading stops at the first attribute that is not complete and the rest
    /// is kept to be written out as it is.
    /// </remarks>
    private static Attributes? ReadAttributes(string tag, bool isWhole)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var index = 1 + TagName(tag).Length;

        while (index < tag.Length)
        {
            var attributeStart = index;

            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            if (index >= tag.Length || tag[index] is '>' or '/') break;

            var nameStart = index;

            while (index < tag.Length &&
                   (char.IsLetterOrDigit(tag[index]) || tag[index] is '-' or '_' or ':' or '.'))
                index++;

            if (index == nameStart) return isWhole ? null : new(values, tag.Substring(attributeStart));

            var name = tag.Substring(nameStart, index - nameStart);

            var afterName = index;

            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            if (index >= tag.Length || tag[index] != '=')
            {
                // No value: a bare attribute such as "required" or "disabled".
                // In a tag the run ends inside, a name at the very end may be
                // the start of one that continues in the next node.
                if (!isWhole && index >= tag.Length)
                    return new(values, tag.Substring(attributeStart));

                values[name] = null;
                index = afterName;
                continue;
            }

            index++;

            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            if (index >= tag.Length) return isWhole ? null : new(values, tag.Substring(attributeStart));

            if (tag[index] is '"' or '\'')
            {
                var quote = tag[index];
                var valueStart = ++index;

                while (index < tag.Length && tag[index] != quote) index++;

                // A value still open where the run ends: the parser cut it at
                // an "@". Everything from its name on is left as written.
                if (index >= tag.Length) return isWhole ? null : new(values, tag.Substring(attributeStart));

                values[name] = tag.Substring(valueStart, index - valueStart);
                index++;
                continue;
            }

            var unquotedStart = index;

            while (index < tag.Length && !char.IsWhiteSpace(tag[index]) && tag[index] is not ('>' or '/'))
                index++;

            values[name] = tag.Substring(unquotedStart, index - unquotedStart);
        }

        return new(values, "");
    }

    /// <summary>A Visual Basic string literal, with quotes doubled.</summary>
    private static string Literal(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
