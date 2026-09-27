using Microsoft.CodeAnalysis;

namespace Basalt.Tests;

/// <summary>
/// The .vbhtml generator for ASP.NET Core, run and compiled the way a build runs it.
/// </summary>
public class ViewGeneratorTests
{
    // Rooted, and ending in a separator-free folder name, as MSBuild passes it.
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "Site");

    private static string InProject(params string[] parts) => Path.Combine([Project, .. parts]);

    private static GeneratorRun.Outcome Run(params (string Path, string Text)[] templates) =>
        GeneratorRun.Run("VbHtmlGenerator", Project, templates);

    [Fact]
    public void RegistersEachViewUnderItsPathFromTheProject()
    {
        var outcome = Run((InProject("Views", "Home", "Index.vbhtml"), "<p>hi</p>\n"));

        Assert.Empty(outcome.CompilationErrors);

        var registration = Assert.Single(outcome.Sources, s => s.Key.EndsWith(".view.g.vb")).Value;

        Assert.Contains("\"mvc.1.0.view\", \"/Views/Home/Index.vbhtml\"", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void AViewThatNamesItsOwnNamespaceStillBuilds()
    {
        // @Namespace moves the class; the attribute registering it used to
        // keep naming the folder's namespace, a type that was not there.
        var outcome = Run((InProject("Views", "Home", "Index.vbhtml"), "@Namespace Custom.Place\n<p>hi</p>\n"));

        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains(outcome.Sources.Values, code => code.Contains("GetType(Global.Custom.Place.Index)"));
    }

    /// <summary>A view using the constructs a real one does, tag helpers included.</summary>
    private static readonly (string Path, string Text)[] OrdinaryViews =
    [
        (InProject("Views", "_ViewImports.vbhtml"), "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers\n"),
        (InProject("Views", "Home", "Index.vbhtml"), """
            @ModelType Global.System.Collections.Generic.List(Of String)
            @Code
                Dim count As Integer = Model.Count
            End Code
            <p>@count items</p>
            <a asp-action="Index" asp-route-id="@count">again</a>
            <input disabled="@(count = 0)" />
            @For Each item As String In Model
                @<li>@item</li>
            Next
            @Section Scripts
                <script></script>
            End Section
            """),
    ];

    [Fact]
    public void WithOptionStrictOnTheGeneratedCodeItselfCompiles()
    {
        // The project's Option Strict now reaches the views, so the code the
        // generator writes around the author's must hold to it too.
        var outcome = GeneratorRun.Run("VbHtmlGenerator", Project, optionStrict: true, properties: null, OrdinaryViews);

        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains(outcome.Sources.Values, code => code.Contains("Option Strict On"));
    }

    [Fact]
    public void AViewIsHeldToTheProjectsOptionStrict()
    {
        // Fixed Off, a project with Option Strict On had late binding
        // accepted in every view.
        (string, string)[] lateBound =
        [
            (InProject("Views", "Late.vbhtml"), "@Code\n    Dim o As Object = \"x\"\n    Dim n = o.Length\nEnd Code\n<p>@n</p>\n"),
        ];

        var strict = GeneratorRun.Run("VbHtmlGenerator", Project, optionStrict: true, properties: null, lateBound);
        var loose = GeneratorRun.Run("VbHtmlGenerator", Project, optionStrict: false, properties: null, lateBound);
        var overridden = GeneratorRun.Run("VbHtmlGenerator", Project, optionStrict: true,
            properties: new Dictionary<string, string> { ["VbRazorOptionStrict"] = "Off" }, lateBound);

        Assert.Contains(strict.CompilationErrors, e => e.Id == "BC30574");
        Assert.Empty(loose.CompilationErrors);
        Assert.Empty(overridden.CompilationErrors);
    }

    [Fact]
    public void ViewStartIsAClassMvcRunsItself()
    {
        // Registered under the .cshtml name MVC looks for, and no longer
        // copied into each view: copied, it wrapped partial views and view
        // components in the layout as well.
        var outcome = Run(
            (InProject("Views", "_ViewStart.vbhtml"), "@Code\n    Layout = \"_Layout\"\nEnd Code\n"),
            (InProject("Views", "Home", "Index.vbhtml"), "<p>hi</p>\n"));

        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains(outcome.Sources.Values,
            code => code.Contains("\"mvc.1.0.view\", \"/Views/_ViewStart.cshtml\""));

        var view = Assert.Single(outcome.Sources,
            s => s.Key.EndsWith("Index.vbhtml.g.vb", StringComparison.Ordinal)).Value;

        Assert.DoesNotContain("Layout = ", view);
    }

    [Fact]
    public void AViewStartAtTheRootAndOneInViewsAreTwoClasses()
    {
        // Both used to be Views._ViewStart with the same hint name: the
        // generator threw and no view at all was generated.
        var outcome = Run(
            (InProject("_ViewStart.vbhtml"), "@Code\n    Layout = \"_Outer\"\nEnd Code\n"),
            (InProject("Views", "_ViewStart.vbhtml"), "@Code\n    Layout = \"_Layout\"\nEnd Code\n"),
            (InProject("Views", "Home", "Index.vbhtml"), "<p>hi</p>\n"));

        Assert.Null(outcome.Exception);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void TwoFoldersNamingTheSameNamespaceDoNotBreakTheGenerator()
    {
        var outcome = Run(
            (InProject("Views", "_ViewImports.vbhtml"), "@Namespace Same\n"),
            (InProject("Pages", "_ViewImports.vbhtml"), "@Namespace Same\n"),
            (InProject("Views", "A.vbhtml"), "<p>a</p>\n"),
            (InProject("Pages", "B.vbhtml"), "@Page\n<p>b</p>\n"));

        Assert.Null(outcome.Exception);
        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void AServiceInjectedInViewImportsReachesEveryView()
    {
        // @Inject in _ViewImports used to stop there: the service was
        // undefined in each view that used it, and the build failed.
        var outcome = Run(
            (InProject("Views", "_ViewImports.vbhtml"),
                "@Inject Microsoft.Extensions.Logging.ILoggerFactory Loggers\n"),
            (InProject("Views", "Home", "Index.vbhtml"),
                "<p>@(Loggers IsNot Nothing)</p>\n"));

        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains(outcome.Sources.Values, code => code.Contains("Public Property Loggers As"));
    }

    [Fact]
    public void ANamespaceInViewImportsCarriesOnDownTheFolders()
    {
        // As C#: "@Namespace Shop.Pages" beside Pages makes Pages/Admin/Index
        // Shop.Pages.Admin, while a view declaring its own keeps it.
        var outcome = Run(
            (InProject("Pages", "_ViewImports.vbhtml"), "@Namespace Shop.Pages\n"),
            (InProject("Pages", "Admin", "Index.vbhtml"), "@Page\n<p>admin</p>\n"),
            (InProject("Pages", "Own.vbhtml"), "@Page\n@Namespace Mine\n<p>own</p>\n"));

        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains(outcome.Sources.Values, code => code.Contains("Namespace Shop.Pages.Admin"));
        Assert.Contains(outcome.Sources.Values, code => code.Contains("Namespace Mine"));
    }

    [Fact]
    public void RenderModeInAViewIsReported()
    {
        // The parser accepts @rendermode now that components use it; a view
        // copied from a component must still hear that it has none.
        var outcome = Run((InProject("Views", "Home", "Index.vbhtml"), "@rendermode InteractiveServer\n<p>hi</p>\n"));

        var problem = Assert.Single(outcome.Diagnostics, d => d.GetMessage().Contains("@rendermode"));

        Assert.Equal(0, problem.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void TheNearestViewImportsDecidesTheModel()
    {
        // As Razor: the file beside the view beats the one above it. Applied
        // outermost first with ??=, the outermost used to win.
        var outcome = Run(
            (InProject("Views", "_ViewImports.vbhtml"), "@ModelType String\n"),
            (InProject("Views", "Admin", "_ViewImports.vbhtml"), "@ModelType Integer\n"),
            (InProject("Views", "Admin", "Index.vbhtml"), "<p>@Model</p>\n"));

        Assert.Empty(outcome.CompilationErrors);

        var view = Assert.Single(outcome.Sources, s => s.Key.EndsWith("Index.vbhtml.g.vb", StringComparison.Ordinal)).Value;

        Assert.Contains("(Of Integer)", view);
    }

    [Fact]
    public void AnAreaViewAndARootViewOfTheSameNameAreTwoClasses()
    {
        var outcome = Run(
            (InProject("Views", "Home", "Index.vbhtml"), "<p>root</p>\n"),
            (InProject("Areas", "Office", "Views", "Home", "Index.vbhtml"), "<p>area</p>\n"),
            (InProject("Areas", "Error", "Views", "Home", "Index.vbhtml"), "<p>keyword</p>\n"));

        Assert.Null(outcome.Exception);
        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains(outcome.Sources.Values, code => code.Contains("Namespace Areas.Office.Views.Home"));

        // An area named after a keyword is escaped rather than breaking the build.
        Assert.Contains(outcome.Sources.Values, code => code.Contains("Namespace Areas.[Error].Views.Home"));
    }

    [Fact]
    public void AProjectInsideAFolderCalledAreasIsNotAnArea()
    {
        // The path is read from the project folder down: a checkout under
        // D:\Areas\Shop is not the Shop area of anything.
        var project = Path.Combine(Path.GetTempPath(), "Areas", "Shop");

        var outcome = GeneratorRun.Run("VbHtmlGenerator", project,
            (Path.Combine(project, "Views", "Home", "Index.vbhtml"), "<p>hi</p>\n"));

        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains(outcome.Sources.Values, code => code.Contains("Namespace Views.Home"));
        Assert.DoesNotContain(outcome.Sources.Values, code => code.Contains("Areas.Shop"));
    }

    [Fact]
    public void SaysWhenAViewCannotBeFoundByItsPath()
    {
        // No BasaltProjectDir: the view would be registered by its file name,
        // which MVC never asks for. It built, and the view was never found.
        var outcome = GeneratorRun.Run("VbHtmlGenerator",
            (InProject("Views", "Home", "Index.vbhtml"), "<p>hi</p>\n"));

        var warning = Assert.Single(outcome.Diagnostics, d => d.Id == "VBH103");

        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void AFolderThatMerelyStartsLikeTheProjectIsOutsideIt()
    {
        // C:\Site is not the folder of C:\Site.Shared\Views\Index.vbhtml.
        var outcome = Run((Project + ".Shared" + Path.DirectorySeparatorChar + "Index.vbhtml", "<p>hi</p>\n"));

        Assert.Contains(outcome.Diagnostics, d => d.Id == "VBH103");
    }
}
