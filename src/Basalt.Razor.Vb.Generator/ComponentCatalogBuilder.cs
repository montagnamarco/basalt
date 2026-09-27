using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Basalt.Razor.Vb;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Razor.Vb.Generator;

/// <summary>A component to be declared before any render tree is written.</summary>
internal sealed record ComponentDeclaration(string Path, VbHtmlDocument Document, string ClassName, string Namespace);

/// <summary>
/// Learns every component's parameters from the compilation, the first of the
/// generator's two phases.
/// </summary>
/// <remarks>
/// Each .vbrazor is declared — its class, base type and the members its
/// @Functions and @Code blocks declare, with no render tree — and the
/// declarations are added to the compilation. The semantic model then answers
/// what a tag names and what its parameters take, with Visual Basic's own
/// lookup: imports, the root namespace, project-wide imports, code-behind
/// partial classes and components from referenced libraries all count, as
/// they do for any Visual Basic name.
///
/// The declarations are parsed through an existing syntax tree of the
/// compilation (WithChangedText), so the generator needs no reference to the
/// Visual Basic compiler package, which would pin every project using it to
/// that version.
/// </remarks>
internal static class ComponentCatalogBuilder
{
    public static IReadOnlyDictionary<string, IComponentCatalog> Build(
        Compilation compilation, IReadOnlyList<ComponentDeclaration> components, CancellationToken cancellation)
    {
        var catalogs = new Dictionary<string, IComponentCatalog>(StringComparer.Ordinal);

        if (components.Count == 0 || compilation.Language != LanguageNames.VisualBasic) return catalogs;

        var seed = compilation.SyntaxTrees.FirstOrDefault();
        var component = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.IComponent");

        if (seed is null || component is null) return catalogs;

        var declared = new List<(ComponentDeclaration Component, SyntaxTree Tree, int LookupAt)>();

        foreach (var declaration in components)
        {
            cancellation.ThrowIfCancellationRequested();

            var text = VbComponentWriter.DeclarationStub(
                declaration.Document, declaration.ClassName, declaration.Namespace, out var lookupAt);

            var tree = seed
                .WithChangedText(SourceText.From(text))
                .WithFilePath(declaration.Path + ".declarations.vb");

            declared.Add((declaration, tree, lookupAt));
        }

        var withDeclarations = compilation.AddSyntaxTrees(declared.Select(d => d.Tree));

        // Found again in the new compilation: symbols are per compilation.
        component = withDeclarations.GetTypeByMetadataName("Microsoft.AspNetCore.Components.IComponent")!;

        foreach (var (declaration, tree, lookupAt) in declared)
        {
            var model = withDeclarations.GetSemanticModel(tree);

            catalogs[declaration.Path] = new SemanticCatalog(model, lookupAt, component);
        }

        return catalogs;
    }

    /// <summary>Answers from one component's class, as its code would see names.</summary>
    private sealed class SemanticCatalog(SemanticModel model, int position, INamedTypeSymbol component) : IComponentCatalog
    {
        private readonly Dictionary<(string, int), ComponentShape?> _found = new();

        public ComponentShape? Find(string tagName, int typeArgumentCount)
        {
            if (_found.TryGetValue((tagName, typeArgumentCount), out var known)) return known;

            var shape = Resolve(tagName, typeArgumentCount) is { } type ? ShapeOf(type) : null;

            _found[(tagName, typeArgumentCount)] = shape;

            return shape;
        }

        /// <summary>
        /// The type a tag names: the first part looked up from inside the
        /// class, each later one a member of the one before, as a dotted name
        /// is read in Visual Basic.
        /// </summary>
        private INamedTypeSymbol? Resolve(string tagName, int arity)
        {
            var parts = tagName.Split('.');
            var first = 0;

            IEnumerable<INamespaceOrTypeSymbol> candidates;

            if (string.Equals(parts[0], "Global", StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
            {
                candidates = new INamespaceOrTypeSymbol[] { model.Compilation.GlobalNamespace };
                first = 1;
            }
            else
            {
                candidates = model.LookupNamespacesAndTypes(position, container: null, name: parts[0]).OfType<INamespaceOrTypeSymbol>();
                first = 1;

                if (parts.Length == 1) return Components(candidates, arity);

                candidates = candidates.Where(c => c is INamespaceSymbol or INamedTypeSymbol { Arity: 0 });
            }

            for (var index = first; index < parts.Length; index++)
            {
                var name = parts[index];
                var next = candidates
                    .SelectMany(c => c.GetMembers(name))
                    .OfType<INamespaceOrTypeSymbol>()
                    .ToList();

                if (index == parts.Length - 1) return Components(next, arity);

                candidates = next.Where(c => c is INamespaceSymbol or INamedTypeSymbol { Arity: 0 }).ToList();
            }

            return null;
        }

        private INamedTypeSymbol? Components(IEnumerable<ISymbol> symbols, int arity) =>
            symbols
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(type => type.Arity == arity && type.AllInterfaces.Contains(component, SymbolEqualityComparer.Default));

        private static ComponentShape ShapeOf(INamedTypeSymbol type)
        {
            var parameters = new List<ComponentParameter>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // The type's own first, so a property that hides an inherited one wins.
            for (var current = type; current is not null; current = current.BaseType)
            {
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (!IsParameter(property) || !seen.Add(property.Name)) continue;

                    parameters.Add(ParameterOf(property));
                }
            }

            var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var generic = name.IndexOf("(Of ", StringComparison.Ordinal);

            return new ComponentShape(
                generic > 0 ? name.Substring(0, generic) : name,
                type.TypeParameters.Select(t => t.Name).ToList(),
                parameters);
        }

        private static bool IsParameter(IPropertySymbol property) =>
            property.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Components.ParameterAttribute");

        private static ComponentParameter ParameterOf(IPropertySymbol property)
        {
            var type = property.Type;
            var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (type.SpecialType is SpecialType.System_String or SpecialType.System_Object)
                return new ComponentParameter(property.Name, typeName, ParameterKind.Text, null);

            if (type is INamedTypeSymbol named)
            {
                // By metadata name: the display of a generic definition names
                // its type parameter, TValue for both of these.
                // A type that does not resolve has no namespace: it is left a Value.
                var definition = named.OriginalDefinition.ContainingNamespace?.ToDisplayString() + "." +
                    named.OriginalDefinition.MetadataName;
                var argument = named.TypeArguments.Length == 1
                    ? named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    : null;

                var kind = definition switch
                {
                    "Microsoft.AspNetCore.Components.RenderFragment" => ParameterKind.RenderFragment,
                    "Microsoft.AspNetCore.Components.RenderFragment`1" => ParameterKind.RenderFragmentOf,
                    "Microsoft.AspNetCore.Components.EventCallback" => ParameterKind.EventCallback,
                    "Microsoft.AspNetCore.Components.EventCallback`1" => ParameterKind.EventCallbackOf,
                    _ => ParameterKind.Value,
                };

                return new ComponentParameter(property.Name, typeName, kind,
                    kind is ParameterKind.RenderFragmentOf or ParameterKind.EventCallbackOf ? argument : null);
            }

            return new ComponentParameter(property.Name, typeName, ParameterKind.Value, null);
        }
    }
}
