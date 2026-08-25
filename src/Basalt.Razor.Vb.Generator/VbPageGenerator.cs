using System.Text;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Classic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Razor.Vb.Generator;

/// <summary>
/// Compiles <c>.vbp</c> pages into classes, one per file.
/// </summary>
/// <remarks>
/// The Classic ASP idea with the compiler kept: a page is a file you drop in
/// a folder and it answers on its own path, but it is compiled with the rest
/// of the project rather than parsed on every request. Errors arrive at build
/// time, at the line of the page.
/// </remarks>
[Generator(LanguageNames.VisualBasic)]
public sealed class VbPageGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pages = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase))
            .Select((file, ct) => (file.Path, Text: file.GetText(ct)?.ToString() ?? ""));

        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
            options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value)
             && !string.IsNullOrWhiteSpace(value)
                ? value
                : null);

        var projectDir = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
            options.GlobalOptions.TryGetValue("build_property.BasaltProjectDir", out var value)
                ? value
                : null);

        var everything = pages.Combine(rootNamespace).Combine(projectDir);

        context.RegisterSourceOutput(everything, (production, data) =>
        {
            Emit(production,
                data.Left.Left.Path, data.Left.Left.Text,
                data.Left.Right, data.Right);
        });
    }

    private static void Emit(
        SourceProductionContext production,
        string path, string text, string? rootNamespace, string? projectDirectory)
    {
        var page = VbPageParser.Parse(text);

        foreach (var diagnostic in page.Diagnostics)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                PageProblem, LocationFor(path, diagnostic.Line), diagnostic.Message));
        }

        var className = ViewNaming.MakeClassName(Path.GetFileNameWithoutExtension(path));
        var folder = FolderOf(path, projectDirectory);

        // Without the root namespace: Visual Basic prepends RootNamespace to
        // every Namespace statement, so writing it here would produce
        // MySite.MySite.Pages.
        var namespaceName = folder.Length > 0 ? $"Pages.{folder}" : "Pages";

        var code = VbPageWriter.Write(page, className, namespaceName, path);

        var route = RouteFor(path, projectDirectory);
        var qualified = string.IsNullOrWhiteSpace(rootNamespace)
            ? $"{namespaceName}.{ViewNaming.Escape(className)}"
            : $"{rootNamespace}.{namespaceName}.{ViewNaming.Escape(className)}";

        // The route as an attribute rather than a registration call: a page
        // added to the folder answers without anyone editing a startup file,
        // which is the whole promise of working this way.
        code = code.Replace(
            $"    Public Class {ViewNaming.Escape(className)}",
            $"    <Global.Basalt.Web.VbPageRoute(\"{route}\")>\r\n"
          + $"    Public Class {ViewNaming.Escape(className)}");

        var hint = folder.Length > 0
            ? $"{folder.Replace('.', '_')}_{className}.vbp.g.vb"
            : $"{className}.vbp.g.vb";

        production.AddSource(hint, SourceText.From(code, Encoding.UTF8));
    }

    /// <summary>
    /// The URL a page answers on, from where its file sits.
    /// </summary>
    /// <remarks>
    /// Lower case, because a URL that differs only in capitalisation is a
    /// different URL to some servers and the same to others. Index is the
    /// folder itself, as every web server since the first one has done.
    /// </remarks>
    private static string RouteFor(string path, string? projectDirectory)
    {
        var relative = Relative(path, projectDirectory);

        var withoutExtension = relative.Substring(0, relative.Length - ".vbp".Length);
        var segments = withoutExtension.Split('/', '\\')
            .Where(s => s.Length > 0)
            .ToList();

        // The Pages folder is where they live, not part of the address.
        if (segments.Count > 0 &&
            segments[0].Equals("Pages", StringComparison.OrdinalIgnoreCase))
            segments.RemoveAt(0);

        if (segments.Count > 0 &&
            segments[segments.Count - 1].Equals("Index", StringComparison.OrdinalIgnoreCase))
            segments.RemoveAt(segments.Count - 1);

        return "/" + string.Join("/", segments).ToLowerInvariant();
    }

    /// <summary>The namespace suffix taken from the folders below Pages.</summary>
    private static string FolderOf(string path, string? projectDirectory)
    {
        var relative = Relative(path, projectDirectory);

        var segments = relative.Split('/', '\\')
            .Where(s => s.Length > 0)
            .ToList();

        // The file itself, and the Pages folder that holds them all.
        if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);

        if (segments.Count > 0 &&
            segments[0].Equals("Pages", StringComparison.OrdinalIgnoreCase))
            segments.RemoveAt(0);

        return string.Join(".", segments.Select(ViewNaming.MakeClassName));
    }

    private static string Relative(string path, string? projectDirectory) =>
        projectDirectory is { Length: > 0 } &&
        path.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(projectDirectory.Length).TrimStart('/', '\\')
            : Path.GetFileName(path);

    private static Location LocationFor(string path, int line) =>
        Location.Create(
            path,
            new TextSpan(0, 0),
            new LinePositionSpan(
                new LinePosition(Math.Max(0, line - 1), 0),
                new LinePosition(Math.Max(0, line - 1), 0)));

    private static readonly DiagnosticDescriptor PageProblem = new(
        "VBP100",
        "Problem in a .vbp page",
        "{0}",
        "Basalt.Pages",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
