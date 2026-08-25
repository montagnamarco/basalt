using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Generated Razor asking about types the user declared.
///
/// The scratch document used to live in a workspace of its own, holding the
/// project's metadata references but none of its source. That resolves
/// <c>String</c> and <c>Integer</c> and hides the failure: a model class in
/// the file next door has its symbol in source, so it did not resolve at all
/// and go-to-definition silently did nothing. These tests use a real project
/// and ask about a type declared inside it.
/// </summary>
public sealed class GeneratedCodeSeesTheProjectTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-generated-").FullName;

    private readonly RoslynLanguageService _service = new();

    private string ModelPath => Path.Combine(_root, "Model.vb");

    /// <summary>Code as the view generator would emit it, using the model.</summary>
    private const string Generated = """
        Public Class GeneratedView
            Public Sub Render()
                Dim p As New Person()
                Dim n = p.Name
            End Sub
        End Class
        """;

    private static int Caret(string fragment, int offset = 0) =>
        Generated.IndexOf(fragment, StringComparison.Ordinal) + offset;

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

        await File.WriteAllTextAsync(ModelPath, """
            Public Class Person
                Public Property Name As String
            End Class
            """);

        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    [Fact]
    public async Task FindsTheDefinitionOfATypeDeclaredInTheProject()
    {
        var found = await _service.GoToDefinitionAsync(
            "__Generated.vb", Caret("p.Name", 3), Generated);

        Assert.NotNull(found);
        Assert.Equal(ModelPath, found.Value.FilePath);

        // Line 2 declares the property, not line 1 which declares the class.
        Assert.Equal(2, found.Value.Line);
    }

    [Fact]
    public async Task HoveringAModelPropertySaysWhatItIs()
    {
        var info = await _service.GetQuickInfoAsync(
            "__Generated.vb", Caret("p.Name", 3), Generated);

        Assert.NotNull(info);
        Assert.Contains("Name", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletesTheMembersOfAProjectType()
    {
        const string code = """
            Public Class GeneratedView
                Public Sub Render()
                    Dim p As New Person()
                    p.
                End Sub
            End Class
            """;

        // Just after the dot on its own line — not the "p." inside the New.
        var marker = code.IndexOf("        p.", StringComparison.Ordinal);
        var caret = marker + "        p.".Length;

        var items = await _service.GetCompletionsAsync("__Generated.vb", caret, code);

        Assert.Contains(items, i => i.DisplayText == "Name");
    }

    [Fact]
    public async Task FindsUsesOfAModelPropertyFromTheGeneratedView()
    {
        var found = await _service.FindReferencesAsync(
            "__Generated.vb", Caret("p.Name", 3), Generated);

        // The declaration in the model, and the use in the generated view.
        Assert.Contains(found, f => f.FilePath == ModelPath);
        Assert.True(found.Count >= 2, $"expected the declaration and a use, got {found.Count}");
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();

        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }
}
