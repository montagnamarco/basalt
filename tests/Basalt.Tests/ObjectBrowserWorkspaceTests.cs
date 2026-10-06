using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// What the Object Browser needs to hear from the language service: that the
/// text of a document changed, and not that someone asked a question about it.
/// </summary>
public sealed class ObjectBrowserWorkspaceTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-objbrowser-").FullName;
    private readonly RoslynLanguageService _service = new();

    private string ModelPath => Path.Combine(_root, "Model.vb");

    private const string ModelText = "Public Class Model\nEnd Class\n";

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

        await File.WriteAllTextAsync(ModelPath, ModelText);
        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ResendingTheSameTextRaisesNothingAndEditingItRaisesOnce()
    {
        var raised = 0;
        _service.DocumentTextChanged += (_, _) => raised++;

        // What every hover, completion and go-to-definition does first.
        await _service.UpdateDocumentAsync(ModelPath, ModelText);
        await _service.UpdateDocumentAsync(ModelPath, ModelText);

        Assert.Equal(0, raised);

        await _service.UpdateDocumentAsync(ModelPath, ModelText + "' edited\n");

        Assert.Equal(1, raised);

        await _service.UpdateDocumentAsync(ModelPath, ModelText + "' edited\n");

        Assert.Equal(1, raised);
    }
}
