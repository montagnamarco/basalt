namespace Basalt.Tests;

/// <summary>
/// The .vbpage generator, run the way the compiler runs it.
/// </summary>
public sealed class VbPageGeneratorTests
{
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "Site");

    private static string InProject(params string[] parts) => Path.Combine([Project, .. parts]);

    [Fact]
    public void ARouteAndNamespaceComeFromTheProjectDirectoryWhenThePathMatchesIt()
    {
        var outcome = GeneratorRun.Run("VbPageGenerator", Project,
            (InProject("Pages", "Admin", "Reports", "Index.vbpage"), "<h1>Reports</h1>"));

        Assert.Empty(outcome.CompilationErrors);

        var code = Assert.Single(outcome.Sources).Value;

        Assert.Contains("Namespace Pages.Admin.Reports", code);
        Assert.Contains("VbPageRoute(\"/admin/reports\")", code);
    }

    [Fact]
    public void BasaltRelativePathMetadataGivesTheRouteAndNamespaceWhenThePathDoesNotMatchTheProjectDirectory()
    {
        // A symlinked checkout: AdditionalFiles is resolved through the
        // physical filesystem while BasaltProjectDir keeps the path MSBuild
        // was given, so they share no prefix. Without BasaltRelativePath the
        // page falls back to its bare file name, "Index.vbpage" — the same
        // route, "/", and namespace, "Pages", as every other Index in the
        // project.
        var physicalPath = "/private/physical/Site/Pages/Admin/Reports/Index.vbpage";

        var outcome = GeneratorRun.Run("VbPageGenerator", new GeneratorRun.Setup
        {
            ProjectDirectory = Project,
            RelativePaths = new Dictionary<string, string>
            {
                [physicalPath] = "Pages/Admin/Reports/Index.vbpage",
            },
        }, (physicalPath, "<h1>Reports</h1>"));

        Assert.Empty(outcome.CompilationErrors);

        var code = Assert.Single(outcome.Sources).Value;

        Assert.Contains("Namespace Pages.Admin.Reports", code);
        Assert.Contains("VbPageRoute(\"/admin/reports\")", code);
    }
}
