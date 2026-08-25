using Basalt.Extensibility;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Go to definition from inside a Razor template for Visual Basic.
///
/// Two outcomes matter. A definition in another file — a model class — is
/// handed over untouched. A definition inside the view itself arrives as a
/// position in generated code, and must come back to the template or be
/// dropped: jumping into a file the author cannot see is worse than not
/// jumping at all.
/// </summary>
public class VbHtmlDefinitionTests
{
    private static LanguageDocument View(string text) =>
        new("/Views/Index.vbhtml", text);

    private static VbHtmlNavigationProvider Answering(SourceLocation? answer) =>
        new(new VbHtmlNavigationProvider.Questions
        {
            Definition = (_, _, _) => Task.FromResult(answer),
        });

    [Fact]
    public async Task PassesThroughADefinitionInAnotherFile()
    {
        var model = new SourceLocation(
            "/Models/Person.vb", SourceRange.At(new SourcePosition(2, 5)));

        const string view = """
            @Code
                Dim name = "x"
            End Code
            <p>@name</p>
            """;

        var found = await Answering(model).GoToDefinitionAsync(
            View(view), view.IndexOf("@name", StringComparison.Ordinal) + 2);

        Assert.Equal("/Models/Person.vb", found?.FilePath);
        Assert.Equal(2, found?.Range.Start.Line);
    }

    [Fact]
    public async Task DropsADefinitionThatOnlyExistsInTheGeneratedCode()
    {
        // Line 1 of the generated file is the Imports header: scaffolding no
        // mapping covers, so there is nowhere in the template to send the user.
        var scaffolding = new SourceLocation(
            "__BasaltGeneratedView.vb", SourceRange.At(new SourcePosition(1, 1)));

        const string view = """
            @Code
                Dim name = "x"
            End Code
            <p>@name</p>
            """;

        var found = await Answering(scaffolding).GoToDefinitionAsync(
            View(view), view.IndexOf("@name", StringComparison.Ordinal) + 2);

        Assert.Null(found);
    }

    [Fact]
    public async Task AsksNothingAboutTheMarkupHalf()
    {
        var anywhere = new SourceLocation(
            "/Models/Person.vb", SourceRange.At(new SourcePosition(1, 1)));

        var asked = false;

        var provider = new VbHtmlNavigationProvider(new VbHtmlNavigationProvider.Questions
        {
            Definition = (_, _, _) =>
            {
                asked = true;
                return Task.FromResult<SourceLocation?>(anywhere);
            },
        });

        const string view = "<p>plain markup</p>";

        Assert.Null(await provider.GoToDefinitionAsync(
            View(view), view.IndexOf("markup", StringComparison.Ordinal)));

        Assert.False(asked);
    }

    [Fact]
    public async Task ListsUsesInOtherFilesAndOnTheTemplate()
    {
        const string view = """
            @Code
                Dim name = "x"
            End Code
            <p>@name</p>
            """;

        // One use in the model, one in scaffolding the map cannot place.
        var answers = new List<SourceLocation>
        {
            new("/Models/Person.vb", SourceRange.At(new SourcePosition(2, 5))),
            new("__BasaltGeneratedView.vb", SourceRange.At(new SourcePosition(1, 1))),
        };

        var provider = new VbHtmlNavigationProvider(new VbHtmlNavigationProvider.Questions
        {
            References = (_, _, _) =>
                Task.FromResult<IReadOnlyList<SourceLocation>>(answers),
        });

        var found = await provider.FindReferencesAsync(
            View(view), view.IndexOf("@name", StringComparison.Ordinal) + 2);

        // The model's use survives; the scaffolding's is dropped.
        var only = Assert.Single(found);

        Assert.Equal("/Models/Person.vb", only.FilePath);
    }

    [Fact]
    public async Task WithoutALanguageServiceThereAreNoReferences()
    {
        const string view = """
            @Code
                Dim name = "x"
            End Code
            <p>@name</p>
            """;

        Assert.Empty(await new VbHtmlNavigationProvider().FindReferencesAsync(
            View(view), view.IndexOf("@name", StringComparison.Ordinal) + 2));
    }

    [Fact]
    public async Task WithoutALanguageServiceThereIsNoDefinition()
    {
        const string view = """
            @Code
                Dim name = "x"
            End Code
            <p>@name</p>
            """;

        Assert.Null(await new VbHtmlNavigationProvider().GoToDefinitionAsync(
            View(view), view.IndexOf("@name", StringComparison.Ordinal) + 2));
    }
}
