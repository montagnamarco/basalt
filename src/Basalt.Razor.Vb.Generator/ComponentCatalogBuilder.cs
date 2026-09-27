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
    /// <param name="inferences">
    /// Generic components written without type arguments, by the path of the
    /// component using them: each is answered by a probe the compiler infers.
    /// </param>
    public static IReadOnlyDictionary<string, IComponentCatalog> Build(
        Compilation compilation,
        IReadOnlyList<ComponentDeclaration> components,
        IReadOnlyDictionary<string, List<TypeInference>>? inferences,
        CancellationToken cancellation)
    {
        var catalogs = new Dictionary<string, IComponentCatalog>(StringComparer.Ordinal);

        if (components.Count == 0 || compilation.Language != LanguageNames.VisualBasic) return catalogs;

        var seed = compilation.SyntaxTrees.FirstOrDefault();
        var component = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.IComponent");

        if (seed is null || component is null) return catalogs;

        var declared = new List<(ComponentDeclaration Component, SyntaxTree Tree, int LookupAt, List<Probe> Probes)>();

        foreach (var declaration in components)
        {
            cancellation.ThrowIfCancellationRequested();

            var text = VbComponentWriter.DeclarationStub(
                declaration.Document, declaration.ClassName, declaration.Namespace, out var lookupAt);

            var probes = new List<Probe>();

            if (inferences is not null && inferences.TryGetValue(declaration.Path, out var requests))
                text = WithProbes(text, requests, probes);

            var tree = seed
                .WithChangedText(SourceText.From(text))
                .WithFilePath(declaration.Path + ".declarations.vb");

            declared.Add((declaration, tree, lookupAt, probes));
        }

        var withDeclarations = compilation.AddSyntaxTrees(declared.Select(d => d.Tree));

        // Found again in the new compilation: symbols are per compilation.
        component = withDeclarations.GetTypeByMetadataName("Microsoft.AspNetCore.Components.IComponent")!;

        foreach (var (declaration, tree, lookupAt, probes) in declared)
        {
            var model = withDeclarations.GetSemanticModel(tree);
            var inferred = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

            foreach (var probe in probes)
            {
                if (Answer(model, tree, probe) is { } arguments) inferred[probe.Key] = arguments;
            }

            catalogs[declaration.Path] = new SemanticCatalog(model, lookupAt, component, inferred);
        }

        return catalogs;
    }

    /// <summary>A probe written into a declaration: where its call is, and what it answers.</summary>
    private sealed record Probe(string Key, int CallAt, string Call);

    /// <summary>
    /// The declaration with a probe for each inference: a generic function
    /// taking the parameters' types, called with the tag's values.
    /// </summary>
    /// <remarks>
    /// The Visual Basic compiler infers the function's type arguments from
    /// the values exactly as it would a component's, and the call's type,
    /// a Tuple of them, says what they are. The component's own type
    /// parameters are renamed __T0, __T1..., so they cannot clash with the
    /// type parameters of a generic component using it.
    /// </remarks>
    private static string WithProbes(string declaration, List<TypeInference> requests, List<Probe> probes)
    {
        var classEnd = declaration.LastIndexOf("    End Class", StringComparison.Ordinal);

        if (classEnd < 0) return declaration;

        var written = new System.Text.StringBuilder(declaration.Substring(0, classEnd));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            var typeParameters = request.Shape.TypeParameters;

            if (request.Arguments.Count == 0 || typeParameters.Count > 7 || !seen.Add(request.Key)) continue;

            var index = probes.Count;
            var renamed = typeParameters.Select((_, at) => $"__T{at}").ToList();
            var parameters = request.Arguments
                .Select((argument, at) => $"__a{at} As {Rename(argument.Parameter.TypeName, typeParameters, renamed)}");
            var tuple = $"Global.System.Tuple(Of {string.Join(", ", renamed)})";

            written.AppendLine();
            written.AppendLine($"        Private Shared Function __Infer{index}(Of {string.Join(", ", renamed)})({string.Join(", ", parameters)}) As {tuple}");
            written.AppendLine("            Return Nothing");
            written.AppendLine("        End Function");
            written.AppendLine();
            written.AppendLine($"        Private Sub __Probe{index}()");

            var call = $"__Infer{index}(";

            written.Append($"            Dim __inferred{index} = ");

            var callAt = written.Length;

            written.AppendLine($"{call}{string.Join(", ", request.Arguments.Select(argument => argument.Code))})");
            written.AppendLine("        End Sub");

            probes.Add(new Probe(request.Key, callAt, call));
        }

        written.Append(declaration.Substring(classEnd));

        return written.ToString();
    }

    /// <summary>A type with the component's type parameters renamed, all at once.</summary>
    private static string Rename(string type, IReadOnlyList<string> from, IReadOnlyList<string> to)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < from.Count; index++) names[from[index]] = to[index];

        return System.Text.RegularExpressions.Regex.Replace(type, @"\b\w+\b",
            match => names.TryGetValue(match.Value, out var renamed) ? renamed : match.Value);
    }

    /// <summary>The type arguments the compiler inferred for a probe, or null.</summary>
    private static IReadOnlyList<string>? Answer(SemanticModel model, SyntaxTree tree, Probe probe)
    {
        var token = tree.GetRoot().FindToken(probe.CallAt);

        // The whole call: the widest node starting at the probe that is still
        // the call, not the declaration around it.
        SyntaxNode? call = null;

        for (var node = token.Parent; node is not null && node.SpanStart == probe.CallAt; node = node.Parent)
        {
            if (node.ToString().StartsWith(probe.Call, StringComparison.Ordinal)) call = node;
        }

        if (call is null) return null;

        if (model.GetTypeInfo(call).Type is not INamedTypeSymbol { TypeKind: not TypeKind.Error } tuple) return null;

        if (tuple.TypeArguments.Any(argument => argument.TypeKind == TypeKind.Error)) return null;

        return tuple.TypeArguments
            .Select(argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToList();
    }

    /// <summary>Answers from one component's class, as its code would see names.</summary>
    private sealed class SemanticCatalog(
        SemanticModel model, int position, INamedTypeSymbol component, IReadOnlyDictionary<string, IReadOnlyList<string>> inferred)
        : IComponentCatalog
    {
        public IReadOnlyList<string>? InferTypeArguments(TypeInference request) =>
            inferred.TryGetValue(request.Key, out var arguments) ? arguments : null;

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

        /// <summary>The component among the symbols a name found, of the arity asked for, or any when -1.</summary>
        private INamedTypeSymbol? Components(IEnumerable<ISymbol> symbols, int arity) =>
            symbols
                .OfType<INamedTypeSymbol>()
                .Where(type => (arity < 0 || type.Arity == arity) &&
                               type.AllInterfaces.Contains(component, SymbolEqualityComparer.Default))
                .OrderBy(type => type.Arity)
                .FirstOrDefault();

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
