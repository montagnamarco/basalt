namespace Basalt.Razor.Vb.Web;

/// <summary>
/// What HTML offers: the elements, and the attributes each takes.
///
/// A working subset rather than the whole specification — the elements and
/// attributes actually written in a Razor view, plus the global attributes
/// that apply everywhere.
/// Lives in the shared library rather than in the IDE: the standalone
/// language server has to offer the same vocabulary, and a second list would
/// drift — a tag completed in one editor and not the other.
/// </summary>
public static class HtmlLanguage
{
    /// <summary>
    /// Elements that never have a closing tag.
    ///
    /// ISet rather than IReadOnlySet: this assembly targets netstandard2.0,
    /// where the read-only interface does not exist.
    /// </summary>
    public static ISet<string> VoidElements { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input",
            "link", "meta", "param", "source", "track", "wbr"
        };

    public static IReadOnlyList<string> Elements { get; } =
    [
        "a", "abbr", "address", "article", "aside", "audio", "b", "base", "blockquote",
        "body", "br", "button", "canvas", "caption", "cite", "code", "col", "colgroup",
        "data", "datalist", "dd", "del", "details", "dfn", "dialog", "div", "dl", "dt",
        "em", "embed", "fieldset", "figcaption", "figure", "footer", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hr", "html",
        "i", "iframe", "img", "input", "ins", "kbd", "label", "legend", "li", "link",
        "main", "map", "mark", "meta", "meter", "nav", "noscript", "object", "ol",
        "optgroup", "option", "output", "p", "param", "picture", "pre", "progress",
        "q", "s", "samp", "script", "section", "select", "small", "source", "span",
        "strong", "style", "sub", "summary", "sup", "table", "tbody", "td", "template",
        "textarea", "tfoot", "th", "thead", "time", "title", "tr", "track",
        "u", "ul", "var", "video", "wbr"
    ];

    /// <summary>Attributes that can appear on any element.</summary>
    public static IReadOnlyList<string> GlobalAttributes { get; } =
    [
        "accesskey", "autocapitalize", "class", "contenteditable", "dir", "draggable",
        "enterkeyhint", "hidden", "id", "inputmode", "lang", "role", "slot", "spellcheck",
        "style", "tabindex", "title", "translate"
    ];

    private static readonly Dictionary<string, string[]> Specific =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = ["href", "target", "rel", "download", "hreflang", "type"],
            ["img"] = ["src", "alt", "width", "height", "loading", "srcset", "sizes"],
            ["input"] = ["type", "name", "value", "placeholder", "required", "disabled",
                         "readonly", "checked", "min", "max", "step", "pattern", "autocomplete"],
            ["form"] = ["action", "method", "enctype", "novalidate", "target", "autocomplete"],
            ["button"] = ["type", "name", "value", "disabled", "form"],
            ["select"] = ["name", "multiple", "required", "disabled", "size"],
            ["option"] = ["value", "selected", "disabled", "label"],
            ["textarea"] = ["name", "rows", "cols", "placeholder", "required", "disabled",
                            "readonly", "maxlength", "wrap"],
            ["label"] = ["for"],
            ["link"] = ["rel", "href", "type", "media", "sizes", "as", "crossorigin"],
            ["script"] = ["src", "type", "async", "defer", "crossorigin", "integrity"],
            ["meta"] = ["name", "content", "charset", "http-equiv", "property"],
            ["table"] = ["summary"],
            ["td"] = ["colspan", "rowspan", "headers"],
            ["th"] = ["colspan", "rowspan", "headers", "scope", "abbr"],
            ["video"] = ["src", "controls", "autoplay", "loop", "muted", "poster",
                         "width", "height", "preload"],
            ["audio"] = ["src", "controls", "autoplay", "loop", "muted", "preload"],
            ["iframe"] = ["src", "width", "height", "title", "loading", "sandbox", "allow"],
            ["source"] = ["src", "srcset", "type", "media", "sizes"],
            ["ol"] = ["start", "reversed", "type"],
            ["progress"] = ["value", "max"],
            ["meter"] = ["value", "min", "max", "low", "high", "optimum"]
        };

    /// <summary>
    /// The attributes an element takes, its own and the global ones.
    ///
    /// An unknown element still gets the global attributes: a custom element
    /// or a typo should not leave the user with no completion at all.
    /// </summary>
    public static IReadOnlyList<string> AttributesFor(string element)
    {
        var specific = Specific.TryGetValue(element, out var own) ? own : [];

        return [.. specific.Concat(GlobalAttributes).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Values worth offering for an attribute, where the set is small.</summary>
    public static IReadOnlyList<string> ValuesFor(string element, string attribute)
    {
        if (attribute.Equals("type", StringComparison.OrdinalIgnoreCase) &&
            element.Equals("input", StringComparison.OrdinalIgnoreCase))
        {
            return ["text", "password", "email", "number", "tel", "url", "search",
                    "date", "time", "datetime-local", "month", "week", "color",
                    "checkbox", "radio", "file", "range", "hidden", "submit", "reset", "button"];
        }

        if (attribute.Equals("type", StringComparison.OrdinalIgnoreCase) &&
            element.Equals("button", StringComparison.OrdinalIgnoreCase))
        {
            return ["submit", "reset", "button"];
        }

        if (attribute.Equals("method", StringComparison.OrdinalIgnoreCase))
            return ["get", "post"];

        if (attribute.Equals("target", StringComparison.OrdinalIgnoreCase))
            return ["_self", "_blank", "_parent", "_top"];

        if (attribute.Equals("rel", StringComparison.OrdinalIgnoreCase))
            return ["stylesheet", "icon", "canonical", "noopener", "noreferrer", "preload"];

        if (attribute.Equals("loading", StringComparison.OrdinalIgnoreCase))
            return ["lazy", "eager"];

        return [];
    }
}
