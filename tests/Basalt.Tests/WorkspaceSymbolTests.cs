using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// workspace/symbol: the solution's declarations by name, from the server
/// the editors already run for views.
/// </summary>
public sealed class WorkspaceSymbolTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-workspace-symbol-" + Guid.NewGuid().ToString("N"));

    public WorkspaceSymbolTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Model.vb"), """
            Public Class Customer
                Public Property CustomerName As String

                Public Function Describe() As String
                    Return CustomerName
                End Function
            End Class

            Public Module Program
                Public Sub Main()
                End Sub
            End Module
            """);
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    [Fact]
    public async Task DeclarationsAreFoundByPartOfTheirName()
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var found = (await new VbHtmlWorkspaceSymbolHandler(compilation)
            .Handle(new WorkspaceSymbolParams { Query = "custom" }, default))!.ToList();

        var type = Assert.Single(found, symbol => symbol.Name == "Customer");
        Assert.Equal(SymbolKind.Class, type.Kind);
        Assert.EndsWith("Model.vb", type.Location.Location!.Uri.GetFileSystemPath(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, type.Location.Location.Range.Start.Line);

        var property = Assert.Single(found, symbol => symbol.Name == "CustomerName");
        Assert.Equal(SymbolKind.Property, property.Kind);
        Assert.Equal("Customer", property.ContainerName);

        Assert.DoesNotContain(found, symbol => symbol.Name == "Describe");
    }
}
