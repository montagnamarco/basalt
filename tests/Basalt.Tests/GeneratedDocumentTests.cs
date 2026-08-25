using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Asking the language service about code the workspace does not hold.
///
/// Generated Razor is the case that matters: it exists only in memory, under
/// a path no project contains. The service used to look the path up, find
/// nothing, and return an empty answer — so the whole Razor delegation was
/// silently dead, and the unit tests could not tell because they used a fake.
/// </summary>
public sealed class GeneratedDocumentTests : IDisposable
{
    private readonly RoslynLanguageService _service = new();

    [Fact]
    public async Task CompletesInCodeTheWorkspaceDoesNotContain()
    {
        const string code = """
            Public Class Scratch
                Public Sub M()
                    Dim s As String = ""
                    s.
                End Sub
            End Class
            """;

        var items = await _service.GetCompletionsAsync(
            "__NotInAnyProject.vb", code.IndexOf("s.", StringComparison.Ordinal) + 2, code);

        // Something came back at all: before, a path the workspace did not
        // know gave an empty list whatever the code said.
        Assert.NotEmpty(items);
    }

    [Fact]
    public async Task OffersTheMembersOfTheTypeUnderTheCaret()
    {
        const string code = """
            Public Class Scratch
                Public Sub M()
                    Dim s As String = ""
                    s.
                End Sub
            End Class
            """;

        var items = await _service.GetCompletionsAsync(
            "__NotInAnyProject.vb", code.IndexOf("s.", StringComparison.Ordinal) + 2, code);

        // Real members of String, not three of System.Object.
        Assert.Contains(items, i => i.DisplayText == "Substring");
        Assert.Contains(items, i => i.DisplayText == "Length");
    }

    [Fact]
    public async Task SaysSomethingAboutASymbolInCodeItWasHanded()
    {
        const string code = """
            Public Class Scratch
                Public Property Title As String
            End Class
            """;

        var info = await _service.GetQuickInfoAsync(
            "__NotInAnyProject.vb", code.IndexOf("Title", StringComparison.Ordinal) + 2, code);

        Assert.NotNull(info);
        Assert.Contains("Title", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysNothingWhenThereIsNoTextToFallBackOn()
    {
        // A path the workspace does not know and no text with it: there is
        // genuinely nothing to answer about.
        Assert.Empty(await _service.GetCompletionsAsync("__NotInAnyProject.vb", 0));
        Assert.Null(await _service.GetQuickInfoAsync("__NotInAnyProject.vb", 0));
    }

    public void Dispose() => _service.Dispose();
}
