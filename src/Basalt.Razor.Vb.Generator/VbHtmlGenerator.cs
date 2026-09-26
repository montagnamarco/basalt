using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Basalt.Razor.Vb;

namespace Basalt.Razor.Vb.Generator;

/// <summary>
/// Compiles .vbhtml templates into Visual Basic view classes.
///
/// This sits alongside Razor's own generator rather than replacing it: Razor
/// emits C# for .cshtml, this emits Visual Basic for .vbhtml, and a project may
/// contain both. Nothing here depends on Razor's internals, so an SDK update
/// cannot break it.
///
/// The attribute names both languages because a project can be either: a
/// generator registered without a language is offered to C# alone, which is
/// what makes an otherwise correct VB generator silently produce nothing.
/// </summary>
[Generator(LanguageNames.VisualBasic, LanguageNames.CSharp)]
public sealed class VbHtmlGenerator : IIncrementalGenerator
{

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var templates = context.AdditionalTextsProvider
            .Where(file => file.Path.EndsWith(".vbhtml", StringComparison.OrdinalIgnoreCase))
            .Select((file, ct) => ReadTemplate(file, ct));

        // The root namespace is a build property, so generated views land in
        // the same namespace as the project's hand-written code.
        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
            options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value)
             && !string.IsNullOrWhiteSpace(value)
                ? value
                : null);

        // Collected rather than taken one by one: a view needs the shared
        // files of its folder, and those are other templates. The language is
        // read once here, outside the per-template pipeline.
        var language = context.CompilationProvider.Select((c, _) => c.Language);

        // Which runtime the views are written for is detected rather than
        // configured: a project referencing ASP.NET Core wants pages MVC can
        // serve, and asking the user to say so is a step they would find out
        // about only when the site failed to render.
        var host = context.CompilationProvider.Select((c, _) =>
            c.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Razor.RazorPage`1") is not null
                ? ViewHost.AspNetCore
                : ViewHost.Standalone);

        // Where the project lives, so a view's full path can be turned into
        // the application-relative one ASP.NET Core routes on.
        var projectDir = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
            options.GlobalOptions.TryGetValue("build_property.BasaltProjectDir", out var value)
                ? value
                : null);

        // The tag helpers the project can use, compared by content so an
        // edit that adds none does not regenerate every view.
        var tagHelpers = context.CompilationProvider.Select(TagHelperDiscovery.Discover);

        var everything = templates.Collect().Combine(rootNamespace).Combine(language)
            .Combine(host).Combine(projectDir).Combine(tagHelpers);

        context.RegisterSourceOutput(everything, (production, data) =>
        {
            var (((((all, root), compilationLanguage), viewHost), projectDirectory), tagHelperIndex) = data;

            var shared = all.Where(t => ViewImports.IsShared(t.Path)).ToList();

            foreach (var template in all)
            {
                // The shared files are not views: generating them as classes
                // would produce a page nobody asked for. Except _ViewStart
                // under ASP.NET Core, which MVC runs itself, as it runs a C#
                // one, before the page it applies to and never before a
                // partial.
                if (ViewImports.IsShared(template.Path) &&
                    !(viewHost == ViewHost.AspNetCore && IsViewStart(template.Path)))
                    continue;

                Emit(production, template, root, compilationLanguage, shared, viewHost,
                    projectDirectory, tagHelperIndex.Catalog);
            }
        });
    }

    /// <summary>
    /// The assembly attributes that make a generated class a Razor Page.
    /// </summary>
    /// <remarks>
    /// RazorCompiledItem is how the framework finds compiled Razor content at
    /// all; RazorPage adds the route. Emitted beside the class rather than
    /// registered at startup, so a page works with no setup — which is what
    /// Razor Pages promises.
    /// </remarks>
    private static string PageRegistration(
        Template template, string namespaceName, string? projectDirectory)
    {
        var identifier = ApplicationRelativePath(template.Path, projectDirectory);
        // Rooted at Global, and carrying the project's root namespace, which
        // Visual Basic prepends to every Namespace statement in the file: the
        // generated class is really RootNamespace.Views.Home.Index, and
        // naming it without the root pointed the attribute at a type that
        // does not exist — a page compiled, registered, and never routed to.
        var typeName = $"Global.{namespaceName}.{ViewNaming.Escape(template.ClassName)}";

        // Only RazorCompiledItem, matching what Razor's own C# generator
        // emits: the route comes from the identifier's path, and adding
        // RazorPageAttribute on top of it registers the page twice.
        return $"""
' <auto-generated>
'     Generated from a .vbhtml template. Changes will be lost.
' </auto-generated>

<Assembly: Global.Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItem(
    GetType({typeName}), "mvc.1.0.razor-page", "{identifier}")>
""";
    }

    /// <summary>
    /// The assembly attribute that lets MVC find a view by its path.
    /// </summary>
    /// <remarks>
    /// The identifier keeps the .vbhtml extension, unlike a page's: the view
    /// engine asks for /Views/Home/Index.vbhtml, through the location the
    /// expander adds, and matches the path exactly.
    /// </remarks>
    private static string ViewRegistration(
        Template template, string namespaceName, string? projectDirectory)
    {
        // A _ViewStart is looked for by its .cshtml name — MVC builds the
        // list of them itself, and only with that extension.
        var identifier = IsViewStart(template.Path)
            ? ApplicationRelativePath(template.Path, projectDirectory)
            : ProjectRelativePath(template.Path, projectDirectory);
        var typeName = $"Global.{namespaceName}.{ViewNaming.Escape(template.ClassName)}";

        return $"""
' <auto-generated>
'     Generated from a .vbhtml template. Changes will be lost.
' </auto-generated>

<Assembly: Global.Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItem(
    GetType({typeName}), "mvc.1.0.view", "{identifier}")>
""";
    }

    /// <summary>
    /// A template's path from the project folder, /Views/Home/Index.vbhtml, or
    /// null when it cannot be worked out.
    /// </summary>
    /// <remarks>
    /// Null without BasaltProjectDir, or for a file outside the project
    /// folder. The folder must be followed by a separator: C:\Site is not the
    /// folder of C:\Site.Shared\Views\Index.vbhtml.
    /// </remarks>
    private static string? RelativeToProject(string path, string? projectDirectory)
    {
        if (projectDirectory is not { Length: > 0 }) return null;

        var folder = projectDirectory.TrimEnd('/', '\\');

        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Length <= folder.Length || path[folder.Length] is not ('/' or '\\')) return null;

        return "/" + path.Substring(folder.Length).Replace('\\', '/').TrimStart('/');
    }

    /// <summary>A template's path from the project folder, or its file name.</summary>
    private static string ProjectRelativePath(string path, string? projectDirectory) =>
        RelativeToProject(path, projectDirectory) ?? "/" + Path.GetFileName(path);

    /// <summary>
    /// A view's path as the application sees it: /Pages/Index.vbhtml.
    /// </summary>
    /// <remarks>
    /// Routing is done on this, so an absolute path would put the developer's
    /// home directory in the route. Without a project directory the file name
    /// is used, which keeps a page working in the odd build that does not
    /// supply one.
    /// </remarks>
    private static string ApplicationRelativePath(string path, string? projectDirectory)
    {
        var normalised = ProjectRelativePath(path, projectDirectory);

        // ASP.NET Core keys its pages on a .cshtml path: the framework's own
        // conventions, route resolution and page-name lookups all assume that
        // extension, and a .vbhtml identifier is simply never matched.
        return normalised.EndsWith(".vbhtml", StringComparison.OrdinalIgnoreCase)
            ? normalised.Substring(0, normalised.Length - ".vbhtml".Length) + ".cshtml"
            : normalised;
    }

    /// <summary>
    /// The folders from a shared file down to a template, as a namespace suffix:
    /// "Admin.Reports" for Pages/Admin/Reports/Index beside Pages/_ViewImports.
    /// </summary>
    private static string FoldersBetween(string sharedPath, string templatePath)
    {
        var sharedFolder = (Path.GetDirectoryName(sharedPath) ?? "").TrimEnd('/', '\\');
        var templateFolder = Path.GetDirectoryName(templatePath) ?? "";

        if (templateFolder.Length <= sharedFolder.Length) return "";

        var below = templateFolder.Substring(sharedFolder.Length)
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(".", below.Select(ViewNaming.MakeClassName));
    }

    /// <summary>
    /// The shared files that apply to a template, outermost first.
    ///
    /// Outermost first so a file deeper in the tree is applied last and wins,
    /// which is how Razor resolves them.
    /// </summary>
    private static IEnumerable<Template> SharedFor(Template template, IReadOnlyList<Template> shared)
    {
        var folder = Path.GetDirectoryName(template.Path) ?? "";

        return shared
            .Where(s => folder.StartsWith(Path.GetDirectoryName(s.Path) ?? "",
                       StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => (Path.GetDirectoryName(s.Path) ?? "").Length);
    }

    private static Template ReadTemplate(AdditionalText file, CancellationToken ct)
    {
        var source = file.GetText(ct);

        return new Template(
            Path: file.Path,
            ClassName: ViewNaming.MakeClassName(Path.GetFileNameWithoutExtension(file.Path)),
            FolderNamespace: ViewNaming.FolderNamespaceFor(file.Path),
            Text: source?.ToString() ?? string.Empty,
            Checksum: TemplateChecksum.Of(source));
    }


    /// <summary>
    /// The folder of a template as a hint-name prefix: /Views/Home → Views_Home.
    /// </summary>
    private static string HintFor(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var hint = new StringBuilder(folder.Length);

        foreach (var c in folder)
            hint.Append(char.IsLetterOrDigit(c) ? c : '_');

        return hint.ToString().Trim('_');
    }

    /// <summary>Whether a template is a _ViewStart.</summary>
    private static bool IsViewStart(string path) =>
        string.Equals(Path.GetFileName(path), ViewImports.ViewStartFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a template is a layout, by the convention Razor uses: a name
    /// starting with an underscore, in the shared folder.
    /// </summary>
    private static bool IsLayout(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        return name is { Length: > 0 } && name[0] == '_';
    }

    private static void Emit(
        SourceProductionContext production,
        Template template,
        string? rootNamespace,
        string language,
        IReadOnlyList<Template> shared,
        ViewHost host,
        string? projectDirectory,
        TagHelperCatalog tagHelpers)
    {
        // Only Visual Basic projects get Visual Basic views. In a C# project a
        // .vbhtml file is reported rather than silently ignored, because a
        // template that produces nothing is hard to diagnose from the outside.
        if (language != LanguageNames.VisualBasic)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                WrongLanguage, Location.None, Path.GetFileName(template.Path)));
            return;
        }

        VbHtmlDocument document;
        try
        {
            document = VbHtmlParser.Parse(template.Text);

            // The folder's shared files, outermost first so the nearest one
            // wins. A view's own directives beat all of them.
            foreach (var file in SharedFor(template, shared))
            {
                var sharedDocument = VbHtmlParser.Parse(file.Text);

                ViewImports.ApplyTo(document, sharedDocument, FoldersBetween(file.Path, template.Path));

                // A layout must not be given a layout of its own: _ViewStart
                // applies to the pages inside the layout, not to the layout
                // wrapping them, and the engine rejects the result as a
                // circular reference. Razor draws the same line by name.
                // Assigned rather than defaulted: the files arrive outermost
                // first, so the nearest _ViewStart must overwrite the ones
                // above it. With ??= the outermost won instead, and a
                // controller's own layout was silently ignored.
                //
                // Only where nothing runs _ViewStart. Under ASP.NET Core MVC
                // does, and baking its layout into every view gave one to
                // partial views and view components too: their markup came
                // back wrapped in a second copy of the page.
                if (host != ViewHost.AspNetCore &&
                    !IsLayout(template.Path) &&
                    ViewImports.LayoutFrom(sharedDocument) is { Length: > 0 } layout)
                    document.DefaultLayout = layout;
            }
        }
        catch (Exception ex)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                ParseFailed, Location.None, Path.GetFileName(template.Path), ex.Message));
            return;
        }

        foreach (var diagnostic in document.Diagnostics)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                TemplateProblem,
                LocationFor(template, diagnostic),
                diagnostic.Message));
        }

        // Pages and Views each root their own namespace, as the C# generator
        // does: a page under Pages must not be named as though it were a view.
        //
        // From the path inside the project where it is known, so a project
        // that itself sits in a folder called Areas is not taken for an area.
        var relativePath = RelativeToProject(template.Path, projectDirectory);
        var rootFolder = ViewNaming.RootFolderFor(relativePath ?? template.Path);

        // A _ViewStart at the project root belongs to no Views or Pages
        // folder; given the Views namespace it was the same class as
        // Views/_ViewStart, and the build failed on the duplicate.
        if (IsViewStart(template.Path) && relativePath is not null &&
            relativePath.IndexOf('/', 1) < 0)
            rootFolder = "ProjectRoot";

        // MVC finds a view by its path from the project folder. Without one
        // the view is registered under its bare file name, which MVC never
        // asks for: the site builds and the view is simply not found.
        if (host == ViewHost.AspNetCore && relativePath is null)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                OutsideTheProject, Location.None, template.Path));
        }

        // Without the root namespace: Visual Basic prepends RootNamespace to
        // every Namespace statement in a file, so writing it here produced
        // MySite.MySite.Views.Home. Nothing noticed while only the view
        // engine looked the classes up by path, but an attribute naming the
        // type pointed at one that was not there.
        var namespaceName = rootFolder;

        if (template.FolderNamespace.Length > 0)
            namespaceName = $"{namespaceName}.{template.FolderNamespace}";

        // @Namespace replaces the folder's namespace in the class the writer
        // emits, and the attribute registering the class has to name the
        // same one, or the build fails on a type that is not there.
        if (!string.IsNullOrWhiteSpace(document.Namespace))
            namespaceName = document.Namespace!;

        // The full name, for anything that must name the type rather than
        // find it by path.
        var qualifiedNamespace = string.IsNullOrWhiteSpace(rootNamespace)
            ? namespaceName
            : $"{rootNamespace}.{namespaceName}";

        // With the template's path, so #ExternalSource points the compiler
        // back at the .vbhtml: an error in a template must be reported
        // against the template, not against code the user never wrote.
        // A page declaring @Page is a Razor Page, whatever the project is:
        // the two live side by side in the same application, and the base
        // class differs between them.
        var templateHost = host == ViewHost.AspNetCore && document.PageRoute is not null
            ? ViewHost.RazorPage
            : host;

        if (templateHost == ViewHost.RazorPage)
            document.PageIdentifier = ApplicationRelativePath(template.Path, projectDirectory);

        var code = VbHtmlCodeWriter
            .WriteWithMap(
                document, template.ClassName, namespaceName, template.Path, templateHost,
                template.Checksum, tagHelpers)
            .Code;



        // The hint name must stay unique, or two Index views would collide:
        // from the path in the project, which is unique by definition, where
        // a namespace is not — two _ViewImports may name the same one.
        var prefix = HintFor(relativePath ?? template.Path) + "_";

        production.AddSource(
            $"{prefix}{template.ClassName}.vbhtml.g.vb", SourceText.From(code, Encoding.UTF8));

        // A Razor Page is found through assembly attributes rather than
        // through the view engine: ASP.NET Core reads them to build its route
        // table, which is why a page needs no registration of its own.
        //
        // In their own file because Visual Basic requires assembly attributes
        // to precede every declaration, unlike C# where they may sit at the
        // end.
        if (host == ViewHost.AspNetCore && document.PageRoute is not null)
        {
            production.AddSource(
                $"{prefix}{template.ClassName}.vbhtml.page.g.vb",
                SourceText.From(
                    PageRegistration(template, qualifiedNamespace, projectDirectory),
                    Encoding.UTF8));
        }
        else if (host == ViewHost.AspNetCore)
        {
            // A view is registered the same way, as the C# compiler registers
            // its own: MVC then finds it by its path, exactly, through the
            // framework's own view compiler. Found by the ending of its type
            // name instead, Views/Home/Index and Areas/Admin/Views/Home/Index
            // were the same view, and whichever came first was served for both.
            production.AddSource(
                $"{prefix}{template.ClassName}.vbhtml.view.g.vb",
                SourceText.From(
                    ViewRegistration(template, qualifiedNamespace, projectDirectory),
                    Encoding.UTF8));
        }
    }

    /// <summary>
    /// Points a diagnostic at the template rather than at the generated code,
    /// so the error opens the file the author actually wrote.
    /// </summary>
    private static Location LocationFor(Template template, VbHtmlDiagnostic diagnostic)
    {
        var line = Math.Max(0, diagnostic.Line - 1);
        var column = Math.Max(0, diagnostic.Column - 1);

        var start = new LinePosition(line, column);

        return Location.Create(
            template.Path,
            new TextSpan(0, 0),
            new LinePositionSpan(start, start));
    }

    /// <summary>
    /// Turns a file name into a valid Visual Basic identifier.
    ///
    /// Razor pages are often named "_Layout" or "About-Us", neither of which is
    /// usable as written.
    /// </summary>

    private sealed record Template(
        string Path, string ClassName, string FolderNamespace, string Text, string? Checksum);

    private static readonly DiagnosticDescriptor TemplateProblem = new(
        id: "VBH100",
        title: "Problem in .vbhtml template",
        messageFormat: "{0}",
        category: "Basalt.Razor.Vb",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParseFailed = new(
        id: "VBH101",
        title: "Could not parse .vbhtml template",
        messageFormat: "'{0}' could not be parsed: {1}",
        category: "Basalt.Razor.Vb",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor WrongLanguage = new(
        id: "VBH102",
        title: ".vbhtml template in a non-Visual Basic project",
        messageFormat: "'{0}' is a Visual Basic template and is ignored in this project; "
                     + "use .cshtml for C#, or move the template to a Visual Basic project",
        category: "Basalt.Razor.Vb",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor OutsideTheProject = new(
        id: "VBH103",
        title: "A view MVC cannot find by its path",
        messageFormat: "'{0}' is outside the project folder, or BasaltProjectDir is not visible to the compiler, "
                     + "so ASP.NET Core cannot find this view by its path",
        category: "Basalt.Razor.Vb",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
