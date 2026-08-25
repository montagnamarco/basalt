using Basalt.Extensibility;
using Basalt.Workspace;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// What a call inside a template expects.
///
/// It used to be a table of three runtime helpers — Html.Raw, Html.Encode,
/// String.Format — which is exactly the set a template author does not need
/// help with. The model's own methods, the ones they wrote, said nothing.
/// </summary>
public sealed class VbHtmlSignatureHelpTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-sighelp-").FullName;

    private readonly RoslynLanguageService _service = new();

    private const string GeneratedViewPath = "__BasaltGeneratedView.vb";

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(_root, "Model.vb"), """
            Public Class Person
                Public Function Greet(salutation As String, times As Integer) As String
                    Return salutation
                End Function
            End Class
            """);

        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    /// <summary>
    /// Only the signature question is wired: what completion answers is a
    /// separate test's business.
    /// </summary>
    private VbHtmlCompletionProvider Provider() =>
        new(
            (_, _, _) => Task.FromResult<IReadOnlyList<CompletionItem>>([]),
            (generated, at, ct) =>
                _service.GetSignatureHelpAsync(GeneratedViewPath, at, generated, ct));

    [Fact]
    public async Task DescribesAMethodTheUserWrote()
    {
        const string view = """
            @Code
                Dim p As New Person()
            End Code
            <p>@p.Greet("hi", 2)</p>
            """;

        // Just inside the bracket.
        var caret = view.IndexOf("Greet(", StringComparison.Ordinal) + "Greet(".Length;

        var help = await Provider().GetSignatureHelpAsync(
            new LanguageDocument("/Views/Index.vbhtml", view), caret);

        Assert.NotNull(help);
        Assert.Contains("Greet", help.Signatures[0].Signature, StringComparison.Ordinal);
        Assert.Contains("salutation", help.Signatures[0].Signature, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TracksWhichArgumentTheCaretIsOn()
    {
        const string view = """
            @Code
                Dim p As New Person()
            End Code
            <p>@p.Greet("hi", 2)</p>
            """;

        // After the comma: the second parameter.
        var caret = view.IndexOf(@"""hi"", ", StringComparison.Ordinal) + @"""hi"", ".Length;

        var help = await Provider().GetSignatureHelpAsync(
            new LanguageDocument("/Views/Index.vbhtml", view), caret);

        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
    }

    [Fact]
    public async Task SaysNothingInTheMarkupHalf()
    {
        const string view = "<p>plain (markup)</p>";

        Assert.Null(await Provider().GetSignatureHelpAsync(
            new LanguageDocument("/Views/Index.vbhtml", view),
            view.IndexOf("markup", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SaysNothingWithoutALanguageService()
    {
        const string view = """
            @Code
                Dim p As New Person()
            End Code
            <p>@p.Greet("hi", 2)</p>
            """;

        var caret = view.IndexOf("Greet(", StringComparison.Ordinal) + "Greet(".Length;

        Assert.Null(await new VbHtmlCompletionProvider().GetSignatureHelpAsync(
            new LanguageDocument("/Views/Index.vbhtml", view), caret));
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();

        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }
}
