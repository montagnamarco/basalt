namespace Basalt.Razor.Vb.Web;

/// <summary>Where in the markup the caret is.</summary>
public enum HtmlContextKind
{
    /// <summary>In text between tags, where a "&lt;" would start an element.</summary>
    Content,

    /// <summary>Typing an element name, just after "&lt;".</summary>
    ElementName,

    /// <summary>Inside a tag where an attribute name would go.</summary>
    AttributeName,

    /// <summary>Inside the quotes of an attribute value.</summary>
    AttributeValue,

    /// <summary>Inside a comment, where nothing should be offered.</summary>
    Comment
}

/// <summary>What the caret is in, and what it belongs to.</summary>
public sealed record HtmlContext(
    HtmlContextKind Kind,
    string Element,
    string Attribute,
    string Prefix);

/// <summary>
/// Reads what the caret is in, scanning backwards from it.
///
/// Scanning backwards rather than parsing the document keeps this working on
/// markup that is half-typed, which is the only state it is ever asked about.
/// </summary>
public static class HtmlContextReader
{
    public static HtmlContext At(string text, int caret)
    {
        caret = Basalt.Razor.Vb.Portable.Clamp(caret, 0, text.Length);

        if (IsInsideComment(text, caret))
            return new HtmlContext(HtmlContextKind.Comment, "", "", "");

        var tagStart = FindOpenTagStart(text, caret);

        if (tagStart < 0)
            return new HtmlContext(HtmlContextKind.Content, "", "", WordBefore(text, caret));

        var afterAngle = tagStart + 1;

        // Still on the element name if no whitespace separates it from "<".
        var nameEnd = afterAngle;
        while (nameEnd < caret && !char.IsWhiteSpace(text[nameEnd])) nameEnd++;

        var element = text.Substring(afterAngle, nameEnd - afterAngle).TrimStart('/');

        if (nameEnd >= caret)
            return new HtmlContext(HtmlContextKind.ElementName, element, "", element);

        if (QuotedValueAt(text, tagStart, caret) is { } value)
        {
            return new HtmlContext(
                HtmlContextKind.AttributeValue, element, value.Attribute, value.Prefix);
        }

        return new HtmlContext(
            HtmlContextKind.AttributeName, element, "", WordBefore(text, caret));
    }

    /// <summary>
    /// The "&lt;" of the tag the caret is inside, or -1 when it is not in one.
    ///
    /// A "&gt;" met first means the tag before the caret is already closed, so
    /// the caret is in content rather than in a tag.
    /// </summary>
    private static int FindOpenTagStart(string text, int caret)
    {
        for (var i = caret - 1; i >= 0; i--)
        {
            if (text[i] == '>') return -1;
            if (text[i] == '<') return i;
        }

        return -1;
    }

    /// <summary>
    /// The attribute whose quotes the caret is between, if any.
    ///
    /// Quotes are counted from the start of the tag: an odd number before the
    /// caret means it sits inside a value.
    /// </summary>
    private static (string Attribute, string Prefix)? QuotedValueAt(
        string text, int tagStart, int caret)
    {
        var quote = '\0';
        var valueStart = -1;
        var attributeStart = -1;
        var attributeEnd = -1;

        for (var i = tagStart + 1; i < caret; i++)
        {
            var c = text[i];

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                valueStart = i + 1;
                continue;
            }

            if (c == '=')
            {
                // The word before "=" names the attribute.
                attributeEnd = i;
                while (attributeEnd > tagStart && char.IsWhiteSpace(text[attributeEnd - 1]))
                    attributeEnd--;

                attributeStart = attributeEnd;
                while (attributeStart > tagStart && IsNameCharacter(text[attributeStart - 1]))
                    attributeStart--;
            }
        }

        if (quote == '\0' || valueStart < 0) return null;

        var attribute = attributeStart >= 0 && attributeEnd > attributeStart
            ? text.Substring(attributeStart, attributeEnd - attributeStart)
            : "";

        return (attribute, text.Substring(valueStart, caret - valueStart));
    }

    private static bool IsInsideComment(string text, int caret)
    {
        var open = text.LastIndexOf("<!--", Math.Max(0, caret - 1), StringComparison.Ordinal);
        if (open < 0) return false;

        var close = text.IndexOf("-->", open, StringComparison.Ordinal);

        return close < 0 || close + 3 > caret;
    }

    private static string WordBefore(string text, int caret)
    {
        var start = caret;
        while (start > 0 && IsNameCharacter(text[start - 1])) start--;

        return text.Substring(start, caret - start);
    }

    private static bool IsNameCharacter(char c) =>
        char.IsLetterOrDigit(c) || c is '-' or '_' or ':';
}
