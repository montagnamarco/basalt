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
