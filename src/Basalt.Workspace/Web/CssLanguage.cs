namespace Basalt.Workspace.Web;

/// <summary>Where in a stylesheet the caret is.</summary>
public enum CssContextKind
{
    /// <summary>Outside any rule, where a selector or at-rule would go.</summary>
    Selector,

    /// <summary>Inside a rule, where a property name would go.</summary>
    PropertyName,

    /// <summary>After a colon, where a value would go.</summary>
    PropertyValue,

    /// <summary>Inside a comment.</summary>
    Comment
}

/// <summary>What the caret is in, and which property it belongs to.</summary>
public sealed record CssContext(CssContextKind Kind, string Property, string Prefix);

/// <summary>
/// The properties and values a stylesheet can use.
///
/// A working subset: the properties written by hand often enough to be worth
/// completing, with their values where the set is closed.
/// </summary>
public static class CssLanguage
{
    public static IReadOnlyList<string> Properties { get; } =
    [
        "align-content", "align-items", "align-self", "animation", "background",
        "background-color", "background-image", "background-position", "background-repeat",
        "background-size", "border", "border-bottom", "border-collapse", "border-color",
        "border-left", "border-radius", "border-right", "border-style", "border-top",
        "border-width", "bottom", "box-shadow", "box-sizing", "clear", "color", "columns",
        "content", "cursor", "display", "flex", "flex-basis", "flex-direction", "flex-grow",
        "flex-shrink", "flex-wrap", "float", "font", "font-family", "font-size",
        "font-style", "font-weight", "gap", "grid", "grid-area", "grid-column",
        "grid-row", "grid-template-columns", "grid-template-rows", "height",
        "justify-content", "justify-items", "justify-self", "left", "letter-spacing",
        "line-height", "list-style", "margin", "margin-bottom", "margin-left",
        "margin-right", "margin-top", "max-height", "max-width", "min-height",
        "min-width", "object-fit", "opacity", "order", "outline", "overflow",
        "overflow-x", "overflow-y", "padding", "padding-bottom", "padding-left",
        "padding-right", "padding-top", "position", "right", "row-gap", "text-align",
        "text-decoration", "text-overflow", "text-transform", "top", "transform",
        "transition", "user-select", "vertical-align", "visibility", "white-space",
        "width", "word-break", "z-index"
    ];

    private static readonly Dictionary<string, string[]> Values =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["display"] = ["block", "inline", "inline-block", "flex", "inline-flex",
                           "grid", "inline-grid", "none", "contents", "table"],
            ["position"] = ["static", "relative", "absolute", "fixed", "sticky"],
            ["text-align"] = ["left", "right", "center", "justify", "start", "end"],
            ["font-weight"] = ["normal", "bold", "bolder", "lighter",
                               "100", "200", "300", "400", "500", "600", "700", "800", "900"],
            ["font-style"] = ["normal", "italic", "oblique"],
            ["overflow"] = ["visible", "hidden", "scroll", "auto", "clip"],
            ["overflow-x"] = ["visible", "hidden", "scroll", "auto", "clip"],
            ["overflow-y"] = ["visible", "hidden", "scroll", "auto", "clip"],
            ["flex-direction"] = ["row", "row-reverse", "column", "column-reverse"],
            ["flex-wrap"] = ["nowrap", "wrap", "wrap-reverse"],
            ["justify-content"] = ["flex-start", "flex-end", "center", "space-between",
                                   "space-around", "space-evenly", "start", "end"],
            ["align-items"] = ["stretch", "flex-start", "flex-end", "center", "baseline"],
            ["align-content"] = ["stretch", "flex-start", "flex-end", "center",
                                 "space-between", "space-around"],
            ["cursor"] = ["auto", "default", "pointer", "text", "move", "not-allowed",
                          "grab", "grabbing", "wait", "help", "crosshair"],
            ["visibility"] = ["visible", "hidden", "collapse"],
            ["white-space"] = ["normal", "nowrap", "pre", "pre-wrap", "pre-line", "break-spaces"],
            ["text-transform"] = ["none", "capitalize", "uppercase", "lowercase"],
            ["text-decoration"] = ["none", "underline", "overline", "line-through"],
            ["box-sizing"] = ["content-box", "border-box"],
            ["float"] = ["left", "right", "none"],
            ["clear"] = ["left", "right", "both", "none"],
            ["object-fit"] = ["fill", "contain", "cover", "none", "scale-down"],
            ["border-style"] = ["none", "solid", "dashed", "dotted", "double", "groove"],
            ["user-select"] = ["auto", "none", "text", "all"],
            ["word-break"] = ["normal", "break-all", "keep-all", "break-word"]
        };

    /// <summary>The values a property takes, empty where the set is open.</summary>
    public static IReadOnlyList<string> ValuesFor(string property) =>
        Values.TryGetValue(property, out var values) ? values : [];

    /// <summary>The at-rules a stylesheet can use.</summary>
    public static IReadOnlyList<string> AtRules { get; } =
    [
        "@media", "@import", "@charset", "@font-face", "@keyframes",
        "@supports", "@page", "@layer", "@container"
    ];
}

/// <summary>
/// Reads what the caret is in, scanning backwards.
///
/// As with the markup reader, the stylesheet is normally half-written, so
/// nothing here assumes it parses.
/// </summary>
public static class CssContextReader
{
    public static CssContext At(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);

        if (IsInsideComment(text, caret))
            return new CssContext(CssContextKind.Comment, "", "");

        var depth = 0;
        var colon = -1;
        var semicolon = -1;

        for (var i = caret - 1; i >= 0; i--)
        {
            var c = text[i];

            if (c == '}') { depth--; continue; }

            if (c == '{')
            {
                depth++;

                if (depth <= 0) continue;

                // Inside a rule: a colon after the last semicolon means a value
                // is being written.
                return colon > semicolon && colon > i
                    ? new CssContext(
                        CssContextKind.PropertyValue,
                        PropertyBefore(text, colon),
                        text[(colon + 1)..caret].TrimStart())
                    : new CssContext(CssContextKind.PropertyName, "", WordBefore(text, caret));
            }

            if (depth != 0) continue;

            if (c == ';' && semicolon < 0) semicolon = i;
            if (c == ':' && colon < 0) colon = i;
        }

        return new CssContext(CssContextKind.Selector, "", WordBefore(text, caret));
    }

    private static string PropertyBefore(string text, int colon)
    {
        var end = colon;
        while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;

        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '-'))
            start--;

        return text[start..end];
    }

    private static bool IsInsideComment(string text, int caret)
    {
        var open = text.LastIndexOf("/*", Math.Max(0, caret - 1), StringComparison.Ordinal);
        if (open < 0) return false;

        var close = text.IndexOf("*/", open, StringComparison.Ordinal);

        return close < 0 || close + 2 > caret;
    }

    private static string WordBefore(string text, int caret)
    {
        var start = caret;

        while (start > 0 && (char.IsLetterOrDigit(text[start - 1])
                          || text[start - 1] is '-' or '@' or '.' or '#'))
            start--;

        return text[start..caret];
    }
}
