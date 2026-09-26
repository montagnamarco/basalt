namespace Basalt.Tests;

/// <summary>
/// The MSBuild props the packages put into a user's project.
/// </summary>
/// <remarks>
/// They decide which files reach the generators, and a wrong glob fails
/// quietly: the project builds, and the pages are simply not in it.
/// </remarks>
public class PackagePropsTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basalt.slnx")))
                directory = directory.Parent;

            Assert.NotNull(directory);

            return directory.FullName;
        }
    }

    [Theory]
    [InlineData("src/Basalt.Razor.Vb.Generator/build/Basalt.Razor.Vb.props")]
    [InlineData("src/Basalt.Razor.Vb.AspNetCore/build/Basalt.Razor.Vb.AspNetCore.props")]
    public void HandsEveryPageToTheGeneratorAndNoVisualBasic6Project(string props)
    {
        // The glob was "**/*.vbp": the extension of a Visual Basic 6 project,
        // not of a page. The page generator only takes .vbpage.
        var text = File.ReadAllText(Path.Combine(RepositoryRoot, props));

        Assert.Contains("Include=\"**/*.vbpage\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Include=\"**/*.vbp\"", text, StringComparison.Ordinal);
    }
}
