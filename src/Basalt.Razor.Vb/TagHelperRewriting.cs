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

        while (index < markup.Length)
        {
            var tagStart = markup.IndexOf('<', index);
            var tagEnd = tagStart < 0 ? -1 : markup.IndexOf('>', tagStart);

            if (tagStart < 0 || tagEnd < 0)
            {
                literal.Append(markup, index, markup.Length - index);
                break;
            }

            literal.Append(markup, index, tagStart - index);

            foreach (var piece in RewriteTag(markup.Substring(tagStart, tagEnd - tagStart + 1)))
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

            index = tagEnd + 1;
        }

        FlushLiteral();

        return pieces;
    }

    private static IReadOnlyList<Piece> RewriteTag(string tag)
    {
        if (tag.IndexOf("asp-", StringComparison.OrdinalIgnoreCase) < 0)
            return [new Piece(tag, IsExpression: false)];

        var attributes = ReadAttributes(tag);

        if (attributes.Count == 0) return [new Piece(tag, IsExpression: false)];

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

        if (rewritten is null)
            return [new Piece(RemoveAspAttributes(tag, attributes), IsExpression: false)];

        return Rebuild(tag, name, attributes, rewritten.Value);
    }

    /// <summary>The href an anchor's routing attributes ask for.</summary>
    private static (string Attribute, string Expression)? RewriteAnchor(
        Dictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("asp-page", out var page))
            return ("href", $"Url.Page({Literal(page)})");

        var hasAction = attributes.TryGetValue("asp-action", out var action);
        var hasController = attributes.TryGetValue("asp-controller", out var controller);

        if (!hasAction && !hasController) return null;

        // Either may be left out: an omitted controller means the current one,
        // which is what Url.Action does with Nothing.
        var actionArgument = hasAction ? Literal(action!) : "Nothing";
        var controllerArgument = hasController ? Literal(controller!) : "Nothing";

        return ("href", $"Url.Action({actionArgument}, {controllerArgument})");
    }

    /// <summary>The action a form's routing attributes ask for.</summary>
    private static (string Attribute, string Expression)? RewriteForm(
        Dictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("asp-page", out var page))
            return ("action", $"Url.Page({Literal(page)})");

        var hasAction = attributes.TryGetValue("asp-action", out var action);
        var hasController = attributes.TryGetValue("asp-controller", out var controller);

        if (!hasAction && !hasController) return null;

        return ("action", $"Url.Action(" +
            $"{(hasAction ? Literal(action!) : "Nothing")}, " +
            $"{(hasController ? Literal(controller!) : "Nothing")})");
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
    private static (string Attribute, string Expression)? RewriteInput(
        Dictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("asp-for", out var expression)) return null;

        return ("value", $"Model.{expression}");
    }

    /// <summary>The text a label carries, from the property it points at.</summary>
    private static (string Attribute, string Expression)? RewriteLabel(
        Dictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("asp-for", out var expression)) return null;

        // The property's own name: the framework reads a DisplayName
        // attribute where there is one, which needs the compilation this
        // rewriting deliberately does without.
        return ("for", $"\"{expression}\"");
    }

    /// <summary>A textarea's value comes from the same place an input's does.</summary>
    private static (string Attribute, string Expression)? RewriteTextArea(
        Dictionary<string, string> attributes) => RewriteInput(attributes);

    /// <summary>
    /// The tag with its asp-* attributes replaced by what they produce.
    /// </summary>
    private static IReadOnlyList<Piece> Rebuild(
        string tag, string name, Dictionary<string, string> attributes,
        (string Attribute, string Expression) rewritten)
    {
        var before = new StringBuilder("<").Append(name);

        // A field bound with asp-for needs a name to post back under, and an
        // id for a label to point at. Both are the property's own name, and
        // both are literal text — only the value has to be evaluated.
        if (attributes.TryGetValue("asp-for", out var bound) &&
            name is "input" or "textarea" or "select")
        {
            if (!attributes.ContainsKey("name"))
                before.Append(" name=\"").Append(bound).Append('"');

            if (!attributes.ContainsKey("id"))
                before.Append(" id=\"").Append(bound).Append('"');
        }

        foreach (var pair in attributes)
        {
            // The rewritten attribute replaces any written by hand: an anchor
            // with both asp-action and href had two, and the browser used the
            // first.
            if (pair.Key.StartsWith("asp-", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(pair.Key, rewritten.Attribute, StringComparison.OrdinalIgnoreCase))
                continue;

            before.Append(' ').Append(pair.Key).Append("=\"").Append(pair.Value).Append('"');
        }

        before.Append(' ').Append(rewritten.Attribute).Append("=\"");

        var after = (tag.EndsWith("/>", StringComparison.Ordinal) ? "\" />" : "\">");

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
    private static string RemoveAspAttributes(string tag, Dictionary<string, string> attributes)
    {
        if (!attributes.Keys.Any(k => k.StartsWith("asp-", StringComparison.OrdinalIgnoreCase)))
            return tag;

        var builder = new StringBuilder("<").Append(TagName(tag));

        foreach (var pair in attributes)
        {
            if (pair.Key.StartsWith("asp-", StringComparison.OrdinalIgnoreCase)) continue;

            builder.Append(' ').Append(pair.Key).Append("=\"").Append(pair.Value).Append('"');
        }

        builder.Append(tag.EndsWith("/>", StringComparison.Ordinal) ? " />" : ">");

        return builder.ToString();
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
    /// Only quoted values: an unquoted or valueless attribute is left where it
    /// is rather than guessed at, since rebuilding a tag we did not fully
    /// understand would lose part of it.
    /// </remarks>
    private static Dictionary<string, string> ReadAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var index = 1 + TagName(tag).Length;

        while (index < tag.Length)
        {
            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            if (index >= tag.Length || tag[index] is '>' or '/') break;

            var nameStart = index;

            while (index < tag.Length &&
                   (char.IsLetterOrDigit(tag[index]) || tag[index] is '-' or '_' or ':'))
                index++;

            if (index == nameStart) return [];

            var name = tag.Substring(nameStart, index - nameStart);

            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            if (index >= tag.Length || tag[index] != '=') return [];

            index++;

            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            if (index >= tag.Length || tag[index] != '"') return [];

            index++;

            var valueStart = index;

            while (index < tag.Length && tag[index] != '"') index++;

            if (index >= tag.Length) return [];

            attributes[name] = tag.Substring(valueStart, index - valueStart);
            index++;
        }

        return attributes;
    }

    /// <summary>A Visual Basic string literal, with quotes doubled.</summary>
    private static string Literal(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
