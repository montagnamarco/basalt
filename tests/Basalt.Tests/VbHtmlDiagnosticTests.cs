using Basalt.Extensibility;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Problems in a Razor view for Visual Basic, reported while editing.
///
/// Without this they appear only at build time, as errors in generated code
/// the user never wrote.
/// </summary>
public class VbHtmlDiagnosticProviderTests
{
    private static async Task<IReadOnlyList<Diagnostic>> CheckAsync(string view) =>
        await new VbHtmlDiagnosticProvider()
            .GetDiagnosticsAsync(new LanguageDocument("/Views/Index.vbhtml", view));

    [Fact]
    public async Task AcceptsAWellFormedView()
    {
        const string view = """
            @Code
                Dim name = "world"
            End Code
            <p>Hello @name</p>
            """;

        Assert.Empty(await CheckAsync(view));
    }

    [Fact]
    public async Task ReportsAnUnclosedTagInTheMarkup()
    {
        var diagnostics = await CheckAsync("<div><p>hello</p>");

        Assert.Contains(diagnostics, d => d.Message.Contains("never closed"));
    }

    [Fact]
    public async Task ReportsAgainstTheViewItself()
    {
        // The whole point: the location must be in the file the user is
        // editing, not in the generated code.
        var diagnostics = await CheckAsync("<div>");

        Assert.All(diagnostics, d => Assert.Equal("/Views/Index.vbhtml", d.FilePath));
    }

    [Fact]
    public async Task StaysUsableOnAViewThatIsHalfTyped()
    {
        // Editing means the file is nearly always incomplete; that must not
        // throw.
        var diagnostics = await CheckAsync("@Code\n    Dim x =");

        Assert.NotNull(diagnostics);
    }

    [Fact]
    public async Task ChecksBothTheCodeAndTheMarkup()
    {
        var diagnostics = await CheckAsync("@Code\n    Dim x = 1\nEnd Code\n<div>");

        Assert.Contains(diagnostics, d => d.Message.Contains("never closed"));
    }

    [Fact]
    public async Task IsReachedThroughTheLanguageRegistry()
    {
        // Registered like any other language: the shell asks the registry and
        // does not know this file is special.
        var registry = new LanguageRegistry();
        WebLanguageProviders.RegisterAll(registry);

        var provider = registry.ForFile("/Views/Index.vbhtml");

        Assert.NotNull(provider?.Diagnostics);

        var diagnostics = await provider!.Diagnostics!.GetDiagnosticsAsync(
            new LanguageDocument("/Views/Index.vbhtml", "<div>"));

        Assert.NotEmpty(diagnostics);
    }
}
