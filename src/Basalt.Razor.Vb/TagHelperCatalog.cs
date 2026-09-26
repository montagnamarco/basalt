using System;
using System.Collections.Generic;
using System.Linq;

namespace Basalt.Razor.Vb;

/// <summary>
/// The tag helpers a view can use, as plain data.
/// </summary>
/// <remarks>
/// Found by the source generator, which has the compilation and can see the
/// ITagHelper classes in the assemblies @addTagHelper names; handed to the
/// writer, which has not and should not. Kept free of Roslyn types so the
/// writer stays usable from the IDE and the language server, which build it
/// from their own compilation.
/// </remarks>
public sealed class TagHelperCatalog
{
    public TagHelperCatalog(IEnumerable<TagHelperDescriptor> descriptors) =>
        Descriptors = descriptors.ToList();

    public static TagHelperCatalog Empty { get; } = new([]);

    public IReadOnlyList<TagHelperDescriptor> Descriptors { get; }

    /// <summary>
    /// The prefix @tagHelperPrefix set, which an element must carry to be a
    /// tag helper ("th:input"); empty when there is none.
    /// </summary>
    public string Prefix { get; private init; } = "";

    /// <summary>
    /// The tag helpers a view's directives bring into scope.
    /// </summary>
    /// <remarks>
    /// "*, Assembly" adds every tag helper in an assembly, "Name.Space.*,
    /// Assembly" those whose name starts so, "Full.Type, Assembly" one;
    /// @removeTagHelper takes the same forms away. Applied in order, shared
    /// files first, as C# Razor applies them.
    /// </remarks>
    public TagHelperCatalog Scoped(IEnumerable<TagHelperDirective> directives)
    {
        var inScope = new List<TagHelperDescriptor>();
        var prefix = "";

        foreach (var directive in directives)
        {
            if (directive.Kind.Equals("tagHelperPrefix", StringComparison.OrdinalIgnoreCase))
            {
                prefix = directive.Value;
                continue;
            }

            var comma = directive.Value.IndexOf(',');

            if (comma < 0) continue;

            var typePattern = directive.Value.Substring(0, comma).Trim();
            var assembly = directive.Value.Substring(comma + 1).Trim();

            var named = Descriptors.Where(d =>
                string.Equals(d.AssemblyName, assembly, StringComparison.OrdinalIgnoreCase) &&
                NameMatches(d.TypeName, typePattern));

            if (directive.Kind.Equals("addTagHelper", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var descriptor in named)
                    if (!inScope.Contains(descriptor)) inScope.Add(descriptor);
            }
            else if (directive.Kind.Equals("removeTagHelper", StringComparison.OrdinalIgnoreCase))
            {
                var removed = named.ToList();
                inScope.RemoveAll(removed.Contains);
            }
        }

        return new TagHelperCatalog(inScope) { Prefix = prefix };
    }

    /// <summary>Whether a type name answers a pattern: "*", "Name.Space.*" or the full name.</summary>
    private static bool NameMatches(string typeName, string pattern)
    {
        var name = typeName.StartsWith("Global.", StringComparison.Ordinal)
            ? typeName.Substring("Global.".Length)
            : typeName;

        if (pattern == "*") return true;

        if (pattern.EndsWith("*", StringComparison.Ordinal))
            return name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal);

        return string.Equals(name, pattern, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tag helpers that apply to an element, in the order they were found.
    /// </summary>
    /// <remarks>
    /// An element is taken by a tag helper when one of its rules names the
    /// element (or "*") and every attribute the rule requires is present, a
    /// name ending in "*" meaning any attribute with that prefix.
    /// </remarks>
    public IReadOnlyList<TagHelperDescriptor> Matching(
        string elementName, IReadOnlyCollection<string> attributeNames, string? parentName = null)
    {
        var found = new List<TagHelperDescriptor>();

        foreach (var descriptor in Descriptors)
        {
            if (descriptor.Rules.Any(rule => rule.Matches(elementName, attributeNames, parentName)))
                found.Add(descriptor);
        }

        return found;
    }
}

/// <summary>One tag helper class and how it binds to markup.</summary>
public sealed record TagHelperDescriptor(
    string TypeName,
    string AssemblyName,
    IReadOnlyList<TagHelperRule> Rules,
    IReadOnlyList<TagHelperProperty> Properties)
{
    /// <summary>A field name for the instance, unique per type, as C# names it.</summary>
    public string FieldName => "__" + TypeName.Replace("Global.", "").Replace('.', '_').Replace('+', '_');

    /// <summary>The property an attribute sets, or null when it sets none.</summary>
    public TagHelperProperty? PropertyFor(string attributeName)
    {
        foreach (var property in Properties)
        {
            if (string.Equals(property.AttributeName, attributeName, StringComparison.OrdinalIgnoreCase))
                return property;
        }

        foreach (var property in Properties)
        {
            if (property.DictionaryPrefix is { Length: > 0 } prefix &&
                attributeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                attributeName.Length > prefix.Length)
                return property;
        }

        return null;
    }
}

/// <summary>
/// One [HtmlTargetElement]: which element, which attributes it needs, under
/// which parent.
/// </summary>
public sealed record TagHelperRule(
    string TagName,
    IReadOnlyList<string> RequiredAttributes,
    string? ParentTag = null,
    bool WithoutEndTag = false)
{
    public bool Matches(string elementName, IReadOnlyCollection<string> attributeNames, string? parentName)
    {
        if (TagName != "*" && !string.Equals(TagName, elementName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (ParentTag is { Length: > 0 } &&
            !string.Equals(ParentTag, parentName, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var required in RequiredAttributes)
        {
            var present = required.EndsWith("*", StringComparison.Ordinal)
                ? attributeNames.Any(a => a.StartsWith(
                    required.Substring(0, required.Length - 1), StringComparison.OrdinalIgnoreCase))
                : attributeNames.Any(a => string.Equals(a, required, StringComparison.OrdinalIgnoreCase));

            if (!present) return false;
        }

        return true;
    }
}

/// <summary>
/// A settable property of a tag helper and the attribute that sets it.
/// </summary>
/// <param name="TypeName">The property's type, Global-qualified Visual Basic.</param>
/// <param name="Kind">How a value written in the markup becomes the property's value.</param>
/// <param name="DictionaryPrefix">
/// For a dictionary property such as RouteValues, the prefix ("asp-route-")
/// that fills one entry per attribute.
/// </param>
public sealed record TagHelperProperty(
    string AttributeName,
    string PropertyName,
    string TypeName,
    TagHelperPropertyKind Kind,
    string? DictionaryPrefix = null);

/// <summary>How an attribute's text becomes a property's value.</summary>
public enum TagHelperPropertyKind
{
    /// <summary>A string: the text as written, with any @expressions in it.</summary>
    String,

    /// <summary>An enum: the text names a member, which C# Razor qualifies with the type.</summary>
    Enum,

    /// <summary>asp-for: the text is a member path on the model.</summary>
    ModelExpression,

    /// <summary>Anything else: the text is a Visual Basic expression, as C# reads it as C#.</summary>
    Code,

    /// <summary>A dictionary of strings filled one prefixed attribute at a time.</summary>
    StringDictionary,

    /// <summary>
    /// A dictionary of other values filled one prefixed attribute at a time,
    /// each value code: param-Count="5" passes the number 5.
    /// </summary>
    CodeDictionary,
}
