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
        "'{0}' is a Visual Basic component and this project is not Visual Basic, so nothing was generated for it.",
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
        var everything = templates.Collect().Combine(language).Combine(hasBlazor);

        context.RegisterSourceOutput(everything, (production, data) =>
        {
            var ((all, compilationLanguage), blazor) = data;

            foreach (var template in all)
                Emit(production, template, compilationLanguage, blazor);
        });
    }

    private static void Emit(
        SourceProductionContext production,
        Component template,
        string language,
        bool hasBlazor)
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
        }
        catch (Exception ex)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                ParseFailed, Location.None, Path.GetFileName(template.Path), ex.Message));
            return;
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
        var folder = ViewNaming.FolderNamespaceFor(template.Path);

        var namespaceName = string.IsNullOrWhiteSpace(folder)
            ? "Components"
            : $"Components.{folder}";

        var source = VbComponentWriter.Write(document, template.ClassName, namespaceName);

        production.AddSource(
            $"{template.ClassName}.Component.g.vb",
            SourceText.From(source, System.Text.Encoding.UTF8));
    }

    private static Component Read(AdditionalText file, CancellationToken ct) =>
        new(
            Path: file.Path,
            Text: file.GetText(ct)?.ToString() ?? string.Empty,
            ClassName: ViewNaming.MakeClassName(Path.GetFileNameWithoutExtension(file.Path)));

    private sealed record Component(string Path, string Text, string ClassName);
}
