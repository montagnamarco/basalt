using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Basalt.Razor.Vb;
using Microsoft.CodeAnalysis;

namespace Basalt.Razor.Vb.Generator;

/// <summary>
/// Finds the tag helper classes a project can use, and how each binds.
/// </summary>
/// <remarks>
/// Read from the compilation the way the C# Razor compiler reads it: every
/// ITagHelper class in the project and in the assemblies that build on the
/// Razor runtime, with its [HtmlTargetElement] rules and settable
/// properties. Which of them a view sees is decided later, by its
/// @addTagHelper directives.
/// </remarks>
internal static class TagHelperDiscovery
{
    private const string TagHelpersNamespace = "Microsoft.AspNetCore.Razor.TagHelpers";

    public static TagHelperIndex Discover(Compilation compilation, CancellationToken ct)
    {
        var contract = compilation.GetTypeByMetadataName($"{TagHelpersNamespace}.ITagHelper");

        if (contract is null) return TagHelperIndex.Empty;

        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                BuildsOnRazor(assembly))
                assemblies.Add(assembly);
        }

        var descriptors = new List<TagHelperDescriptor>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in TypesIn(assembly.GlobalNamespace))
            {
                ct.ThrowIfCancellationRequested();

                if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType) continue;
                if (type.DeclaredAccessibility != Accessibility.Public) continue;
                if (!type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default)) continue;

                descriptors.Add(Describe(type, assembly.Name));
            }
        }

        return new TagHelperIndex(descriptors);
    }

    /// <summary>
    /// Whether an assembly can hold tag helpers at all: it references the
    /// Razor runtime. Walking every type of every framework assembly on each
    /// compilation would cost far more than it finds.
    /// </summary>
    private static bool BuildsOnRazor(IAssemblySymbol assembly) =>
        assembly.Modules.Any(module => module.ReferencedAssemblies.Any(r =>
            r.Name == "Microsoft.AspNetCore.Razor"));

    private static IEnumerable<INamedTypeSymbol> TypesIn(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;

            foreach (var nested in type.GetTypeMembers())
                yield return nested;
        }

        foreach (var child in ns.GetNamespaceMembers())
            foreach (var type in TypesIn(child))
                yield return type;
    }

    private static TagHelperDescriptor Describe(INamedTypeSymbol type, string assemblyName)
    {
        var rules = new List<TagHelperRule>();

        foreach (var attribute in type.GetAttributes())
        {
            if (!IsRazorAttribute(attribute, "HtmlTargetElementAttribute")) continue;

            var tag = attribute.ConstructorArguments.Length > 0 &&
                      attribute.ConstructorArguments[0].Value is string named && named.Length > 0
                ? named
                : "*";

            var required = new List<string>();
            string? parent = null;
            var withoutEndTag = false;

            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Attributes" && argument.Value.Value is string list)
                    required.AddRange(RequiredAttributes(list));

                if (argument.Key == "ParentTag" && argument.Value.Value is string parentTag)
                    parent = parentTag;

                // TagStructure.WithoutEndTag (2): <partial name="x"> has no
                // closing tag to wait for.
                if (argument.Key == "TagStructure" && argument.Value.Value is int structure)
                    withoutEndTag = structure == 2;
            }

            rules.Add(new TagHelperRule(tag, required, parent, withoutEndTag));
        }

        // Without [HtmlTargetElement] a tag helper targets the element its
        // name spells: EmailTagHelper takes <email>.
        if (rules.Count == 0)
        {
            var name = type.Name.EndsWith("TagHelper", StringComparison.Ordinal)
                ? type.Name.Substring(0, type.Name.Length - "TagHelper".Length)
                : type.Name;

            rules.Add(new TagHelperRule(Kebab(name), []));
        }

        return new TagHelperDescriptor(VisualBasicName(type), assemblyName, rules, Properties(type));
    }

    /// <summary>
    /// The attribute names a rule requires. "[asp-for]" and "[type=text]"
    /// are CSS-selector forms; only presence is kept, the value test is not.
    /// </summary>
    private static IEnumerable<string> RequiredAttributes(string list)
    {
        foreach (var raw in list.Split(','))
        {
            var name = raw.Trim().TrimStart('[').TrimEnd(']');
            var equals = name.IndexOfAny(['=', '^', '$']);

            if (equals >= 0) name = name.Substring(0, equals);

            name = name.Trim();

            if (name.Length > 0) yield return name;
        }
    }

    private static List<TagHelperProperty> Properties(INamedTypeSymbol type)
    {
        var properties = new List<TagHelperProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic || member.IsIndexer) continue;
                if (member.DeclaredAccessibility != Accessibility.Public) continue;
                if (!seen.Add(member.Name)) continue;
                if (member.GetAttributes().Any(a => IsRazorAttribute(a, "HtmlAttributeNotBoundAttribute"))) continue;

                var naming = member.GetAttributes().FirstOrDefault(a => IsRazorAttribute(a, "HtmlAttributeNameAttribute"));

                string? attributeName = Kebab(member.Name);
                string? prefix = null;
                var prefixWritten = false;

                if (naming is not null)
                {
                    attributeName = naming.ConstructorArguments.Length > 0
                        ? naming.ConstructorArguments[0].Value as string
                        : null;

                    foreach (var argument in naming.NamedArguments)
                    {
                        if (argument.Key == "DictionaryAttributePrefix")
                        {
                            prefix = argument.Value.Value as string;
                            prefixWritten = true;
                        }
                    }
                }

                // A dictionary property takes prefixed attributes even when
                // no prefix is written: its own name and a dash, as Razor does.
                var dictionaryValue = DictionaryValueType(member.Type);

                if (dictionaryValue is not null && !prefixWritten && attributeName is { Length: > 0 })
                    prefix = attributeName + "-";

                var settable = member.SetMethod is { DeclaredAccessibility: Accessibility.Public };

                if (settable && attributeName is { Length: > 0 })
                {
                    properties.Add(new TagHelperProperty(
                        attributeName, member.Name, VisualBasicName(member.Type), KindOf(member.Type)));
                }

                // asp-route-id fills RouteValues("id"): one entry per
                // prefixed attribute, the dictionary itself read, not set.
                // The values are strings only when the dictionary holds
                // strings: param-Count="5" on <component> passes the number.
                if (prefix is { Length: > 0 } && dictionaryValue is not null)
                {
                    var kind = dictionaryValue.SpecialType == SpecialType.System_String
                        ? TagHelperPropertyKind.StringDictionary
                        : TagHelperPropertyKind.CodeDictionary;

                    properties.Add(new TagHelperProperty(
                        "", member.Name, VisualBasicName(member.Type), kind, prefix));
                }
            }
        }

        return properties;
    }

    /// <summary>
    /// The value type of an IDictionary(Of String, T) the property is or
    /// implements, or null when it is not one.
    /// </summary>
    private static ITypeSymbol? DictionaryValueType(ITypeSymbol type)
    {
        IEnumerable<INamedTypeSymbol> candidates = type is INamedTypeSymbol named
            ? new[] { named }.Concat(type.AllInterfaces)
            : type.AllInterfaces;

        foreach (var candidate in candidates)
        {
            if (candidate.IsGenericType &&
                candidate.ConstructedFrom.MetadataName == "IDictionary`2" &&
                candidate.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic" &&
                candidate.TypeArguments[0].SpecialType == SpecialType.System_String)
                return candidate.TypeArguments[1];
        }

        return null;
    }

    private static TagHelperPropertyKind KindOf(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String) return TagHelperPropertyKind.String;
        if (type.TypeKind == TypeKind.Enum) return TagHelperPropertyKind.Enum;

        if (type.Name == "ModelExpression" &&
            type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ViewFeatures")
            return TagHelperPropertyKind.ModelExpression;

        return TagHelperPropertyKind.Code;
    }

    private static bool IsRazorAttribute(AttributeData attribute, string name) =>
        attribute.AttributeClass is { } type &&
        type.Name == name &&
        type.ContainingNamespace?.ToDisplayString() == TagHelpersNamespace;

    /// <summary>A type's name as Visual Basic writes it, rooted at Global.</summary>
    private static string VisualBasicName(ITypeSymbol type)
    {
        // A Visual Basic compilation already writes "Global."; a C# one
        // would write "global::".
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "Global.");

        return name.StartsWith("Global.", StringComparison.Ordinal) ? name : "Global." + name;
    }

    /// <summary>
    /// A name as an HTML attribute: "AppendVersion" is "append-version", and
    /// "ProductID" is "product-id".
    /// </summary>
    /// <remarks>
    /// Razor's rule: a dash before a capital that follows a small letter, or
    /// before the last capital of a run followed by a small letter
    /// ("HTMLEncode" is "html-encode"). A dash before every capital made
    /// "product-i-d", an attribute nobody would write.
    /// </remarks>
    private static string Kebab(string name)
    {
        var kebab = new StringBuilder();

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (i > 0 && char.IsUpper(c))
            {
                var afterLower = char.IsLower(name[i - 1]);
                var endsRun = char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);

                if (afterLower || endsRun) kebab.Append('-');
            }

            kebab.Append(char.ToLowerInvariant(c));
        }

        return kebab.ToString();
    }
}

/// <summary>
/// The discovered tag helpers, compared by content.
/// </summary>
/// <remarks>
/// The generator re-runs its output step only when an input changes. Discovery
/// runs on every compilation, but an edit that adds no tag helper yields an
/// equal index, and the views are not generated again for it.
/// </remarks>
internal sealed class TagHelperIndex : IEquatable<TagHelperIndex>
{
    private readonly string _signature;

    public TagHelperIndex(IReadOnlyList<TagHelperDescriptor> descriptors)
    {
        Catalog = new TagHelperCatalog(descriptors);

        _signature = string.Join("\n", descriptors.Select(d =>
            $"{d.AssemblyName}|{d.TypeName}|" +
            string.Join(";", d.Rules.Select(r => $"{r.TagName}[{string.Join(",", r.RequiredAttributes)}]{r.ParentTag}")) + "|" +
            string.Join(";", d.Properties.Select(p => $"{p.AttributeName}={p.PropertyName}:{p.Kind}:{p.TypeName}:{p.DictionaryPrefix}"))));
    }

    public static TagHelperIndex Empty { get; } = new([]);

    public TagHelperCatalog Catalog { get; }

    public bool Equals(TagHelperIndex? other) => other is not null && other._signature == _signature;

    public override bool Equals(object? obj) => Equals(obj as TagHelperIndex);

    public override int GetHashCode() => _signature.GetHashCode();
}
