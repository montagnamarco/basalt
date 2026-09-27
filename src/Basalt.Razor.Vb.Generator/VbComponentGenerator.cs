using System;
using System.IO;
using System.Linq;
using System.Threading;
using Basalt.Razor.Vb;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Razor.Vb.Generator;

/// <summary>
/// Compiles .vbrazor templates into Blazor components.
/// </summary>
/// <remarks>
/// The counterpart of the .razor compiler, which emits C# and only C#. The
/// extension is what says which one a template wants to be: a .vbhtml is a
/// view that writes markup, a .vbrazor is a component that builds a render
/// tree, and the two produce different classes from the same syntax. Deciding
/// it from the file rather than from a directive means a project can hold both
/// without either having to declare itself.
///
/// Server, WebAssembly and Auto are all served by this: they differ in where a
/// component runs, not in what is compiled for it.
/// </remarks>
[Generator(LanguageNames.VisualBasic, LanguageNames.CSharp)]
public sealed class VbComponentGenerator : IIncrementalGenerator
{
    /// <summary>The extension a Blazor component written in VB carries.</summary>
    public const string Extension = ".vbrazor";

    private static readonly DiagnosticDescriptor WrongLanguage = new(
        "VBRZ010",
        "Blazor components in Visual Basic need a Visual Basic project",
        "'{0}' is a Visual Basic component and this project is not Visual Basic, so nothing was generated for it",
        "Basalt.Razor.Vb",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParseFailed = new(
        "VBRZ011",
        "A component could not be read",
        "'{0}' could not be read: {1}",
        "Basalt.Razor.Vb",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ComponentProblem = new(
        "VBRZ013",
        "Problem in a .vbrazor component",
        "{0}",
        "Basalt.Razor.Vb",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ComponentDirectiveIgnored = new(
        "VBRZ014",
        "A directive in a .vbrazor component is not supported yet",
        "{0}",
        "Basalt.Razor.Vb",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoBlazor = new(
        "VBRZ012",
        "Blazor is not referenced",
        "'{0}' is a Blazor component and this project does not reference Blazor. Add a FrameworkReference to Microsoft.AspNetCore.App.",
        "Basalt.Razor.Vb",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var templates = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .Select((file, ct) => Read(file, ct));

        var language = context.CompilationProvider.Select((c, _) => c.Language);

        // Whether the project can host what is generated. Reported rather than
        // assumed: without the reference the component compiles to a class
        // whose base type does not exist, and the error names ComponentBase
        // rather than the missing package.
        var hasBlazor = context.CompilationProvider.Select((c, _) =>
            c.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ComponentBase") is not null);

        // RootNamespace is deliberately not read: Visual Basic prepends it to
        // every Namespace statement itself, so a generator that also writes it
        // produces Sito.Sito.Home.
        var optionStrict = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider)
            .Select((pair, _) => ProjectOptionStrict.IsOn(pair.Left, pair.Right));

        // Where the project is, so a component is namespaced by its folders
        // as a .razor file is; and whether the project asked for the older
        // scheme with VbRazorComponentNamespaces=Legacy.
        var naming = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
        {
            options.GlobalOptions.TryGetValue("build_property.BasaltProjectDir", out var projectDirectory);
            options.GlobalOptions.TryGetValue("build_property.VbRazorComponentNamespaces", out var scheme);

            var legacy = string.Equals(scheme?.Trim(), "Legacy", StringComparison.OrdinalIgnoreCase);

            return new Naming(legacy ? null : projectDirectory is { Length: > 0 } ? projectDirectory : null);
        });

        var everything = templates.Collect().Combine(language).Combine(hasBlazor).Combine(optionStrict).Combine(naming);

        context.RegisterSourceOutput(everything, (production, data) =>
        {
            var ((((all, compilationLanguage), blazor), strict), names) = data;

            // _Imports.vbrazor is not a component: it lends its directives
            // to every component in its folder and below, as _Imports.razor
            // does. Generated as one, it was a class called _Imports.
            var shared = all.Where(t => IsImports(t.Path)).ToList();

            foreach (var template in all)
            {
                if (IsImports(template.Path)) continue;

                Emit(production, template, compilationLanguage, blazor, strict, shared, names);
            }
        });
    }

    private static void Emit(
        SourceProductionContext production,
        Component template,
        string language,
        bool hasBlazor,
        bool optionStrict,
        IReadOnlyList<Component> shared,
        Naming naming)
    {
        // A .vbrazor in a C# project is reported rather than silently ignored:
        // a template that produces nothing is hard to diagnose from outside.
        if (language != LanguageNames.VisualBasic)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                WrongLanguage, Location.None, Path.GetFileName(template.Path)));
            return;
        }

        if (!hasBlazor)
            production.ReportDiagnostic(Diagnostic.Create(
                NoBlazor, Location.None, Path.GetFileName(template.Path)));

        VbHtmlDocument document;

        try
        {
            document = VbHtmlParser.Parse(template.Text);

            // Outermost first, so the nearest file wins; the component's own
            // directives beat them all.
            foreach (var file in shared
                .Where(s => IsAbove(s.Path, template.Path))
                .OrderBy(s => s.Path.Length))
            {
                var imports = VbHtmlParser.Parse(file.Text);

                ViewImports.ApplyTo(document, imports, VbHtmlGenerator.FoldersBetween(file.Path, template.Path));
                ViewImports.ApplyLayoutTo(document, imports);
            }
        }
        catch (Exception ex)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                ParseFailed, Location.None, Path.GetFileName(template.Path), ex.Message));
            return;
        }

        // What the parser found wrong, where it found it. They used to be
        // dropped: an unclosed @If produced a component that failed later
        // with a compiler error about generated code, or silently rendered
        // half the markup.
        foreach (var diagnostic in document.Diagnostics.Where(d => d.AppliesTo(template.Path)))
        {
            var at = new LinePosition(
                Math.Max(0, diagnostic.Line - 1), Math.Max(0, diagnostic.Column - 1));

            // A directive the parser does not support yet (@rendermode,
            // @typeparam, ...) is skipped and the component still builds, as
            // it did before these were reported: a warning, not an error that
            // would break a project which compiled yesterday.
            var descriptor = diagnostic.Id == "VBH008" ? ComponentDirectiveIgnored : ComponentProblem;

            production.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                Location.Create(template.Path, new TextSpan(0, 0), new LinePositionSpan(at, at)),
                diagnostic.Message));
        }

        // The folder the component sits in becomes part of its namespace, the
        // way it does for a view: two components with the same file name in
        // different folders are two classes, not one collision.
        //
        // Without the root namespace: Visual Basic prepends RootNamespace to
        // every Namespace statement in a file, so writing it here produced
        // Sito.Sito.Home — a class that compiles and that nothing naming the
        // type can find. Measured: MapRazorComponents(Of Global.Sito.Home)
        // failed to resolve while the generated file was sitting right there.
        var namespaceName = NamespaceFor(template.Path, naming);

        // With the template's path, so the generated code carries
        // #ExternalSource: without it a component compiled into the build had
        // no line in the PDB pointing at the .vbrazor, and no breakpoint in
        // one could ever bind.
        var source = VbComponentWriter
            .WriteWithMap(
                document, template.ClassName, namespaceName, template.Path,
                checksum: template.Checksum, optionStrict: optionStrict)
            .Code;

        // With the namespace, so Admin/Index and Shop/Index are two files.
        var qualifier = namespaceName.Replace("[", "").Replace("]", "");

        production.AddSource(
            qualifier.Length == 0
                ? $"{template.ClassName}.Component.g.vb"
                : $"{template.ClassName}.{qualifier}.Component.g.vb",
            SourceText.From(source, System.Text.Encoding.UTF8));
    }

    /// <summary>
    /// A component's namespace: its folders from the project, as the Razor
    /// compiler gives a .razor file — Components/Layout/MainLayout is in
    /// Components.Layout, Components/Pages/Home in Components.Pages.
    /// </summary>
    /// <remarks>
    /// The older scheme, kept for VbRazorComponentNamespaces=Legacy and for a
    /// build without BasaltProjectDir, put every component in Components plus
    /// the folders below a Views or Pages folder: Admin/Index and Shop/Index
    /// were the same class, and a layout in Components/Layout was not in
    /// Components.Layout, where anyone coming from C# looks for it.
    /// </remarks>
    private static string NamespaceFor(string path, Naming naming)
    {
        if (ViewNaming.ProjectFolderNamespaceFor(path, naming.ProjectDirectory) is { } folders)
            return folders;

        var folder = ViewNaming.FolderNamespaceFor(path);

        return string.IsNullOrWhiteSpace(folder) ? "Components" : $"Components.{folder}";
    }

    /// <summary>How components are namespaced: by project folder, or the older way when null.</summary>
    private sealed record Naming(string? ProjectDirectory);

    /// <summary>The name of the file whose directives every component below it shares.</summary>
    internal const string ImportsFileName = "_Imports.vbrazor";

    private static bool IsImports(string path) =>
        string.Equals(Path.GetFileName(path), ImportsFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a shared file sits in a component's folder or one above it.</summary>
    private static bool IsAbove(string sharedPath, string componentPath)
    {
        var folder = (Path.GetDirectoryName(sharedPath) ?? "").TrimEnd('/', '\\');
        var componentFolder = Path.GetDirectoryName(componentPath) ?? "";

        return componentFolder.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
               (componentFolder.Length == folder.Length || componentFolder[folder.Length] is '/' or '\\');
    }

    private static Component Read(AdditionalText file, CancellationToken ct)
    {
        var source = file.GetText(ct);

        return new(
            Path: file.Path,
            Text: source?.ToString() ?? string.Empty,
            ClassName: ViewNaming.MakeClassName(Path.GetFileNameWithoutExtension(file.Path)),
            Checksum: TemplateChecksum.Of(source));
    }

    private sealed record Component(string Path, string Text, string ClassName, string? Checksum);
}
