using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The decisions the generator makes from a template's path: what the class
/// is called, and which namespace it lands in.
///
/// The generator had no tests at all. Its assembly cannot be referenced from
/// here — it carries its own copy of the parser sources, and the duplicate
/// types hang test discovery — so the decisions live in the shared library
/// and are checked directly.
/// </summary>
public class ViewNamingTests
{
    [Theory]
    [InlineData("Index", "Index")]
    [InlineData("my-page", "my_page")]
    [InlineData("my page", "my_page")]
    [InlineData("page.partial", "page_partial")]
    public void TurnsAFileNameIntoAnIdentifier(string fileName, string expected)
    {
        // A hyphen, a space and a dot are all legal in a file name and none
        // of them can appear in a Visual Basic identifier.
        Assert.Equal(expected, ViewNaming.MakeClassName(fileName));
    }

    [Fact]
    public void PrefixesANameThatStartsWithADigit()
    {
        // An identifier cannot start with a digit.
        Assert.Equal("_404", ViewNaming.MakeClassName("404"));
    }

    [Fact]
    public void GivesAnEmptyNameSomethingUsable()
    {
        Assert.Equal("_", ViewNaming.MakeClassName(""));
    }

    [Theory]
    [InlineData("/app/Views/Index.vbhtml", "")]
    [InlineData("/app/Views/Home/Index.vbhtml", "Home")]
    [InlineData("/app/Views/Home/Parts/Row.vbhtml", "Home.Parts")]
    [InlineData("/app/Pages/Admin/Index.vbhtml", "Admin")]
    public void TakesTheNamespaceFromTheFoldersBelowTheViewsRoot(
        string path, string expected)
    {
        // Two controllers commonly both have an Index view; without the
        // folder in the namespace the second would collide with the first.
        Assert.Equal(expected, ViewNaming.FolderNamespaceFor(path));
    }

    [Fact]
    public void UsesNoNamespaceForAPathWithNoViewsRoot()
    {
        // Not a recognised layout: better a flat namespace than one invented
        // from whatever the folders happen to be called.
        Assert.Equal("", ViewNaming.FolderNamespaceFor("/somewhere/else/Index.vbhtml"));
    }

    [Fact]
    public void MakesTheFolderNamespaceAnIdentifierToo()
    {
        Assert.Equal(
            "my_area", ViewNaming.FolderNamespaceFor("/app/Views/my-area/Index.vbhtml"));
    }

    [Theory]
    [InlineData("/app/Views/_ViewImports.vbhtml", true)]
    [InlineData("/app/Views/_ViewStart.vbhtml", true)]
    [InlineData("/app/Views/Index.vbhtml", false)]
    [InlineData("/app/Views/_Row.vbhtml", false)]
    public void KnowsWhichFilesAreSharedRatherThanViews(string path, bool shared)
    {
        // A shared file is not a view: generating a class for it would
        // produce a page nobody asked for. A partial starting with "_" is
        // still a view.
        Assert.Equal(shared, ViewImports.IsShared(path));
    }
}
