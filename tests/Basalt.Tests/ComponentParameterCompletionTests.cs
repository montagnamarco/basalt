using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// A component's parameters offered where its tag takes attributes.
/// </summary>
/// <remarks>
/// "&lt;Counter " offered HTML's global attributes and nothing of Counter's
/// own, in the IDE and every editor using the server alike: a parameter was
/// typed from memory, and a misspelt one found at run time.
/// </remarks>
public sealed class ComponentParameterCompletionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-component-parameters-" + Guid.NewGuid().ToString("N"));

    public ComponentParameterCompletionTests()
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
        File.WriteAllText(Path.Combine(_root, "Counter.vb"), """
            Imports Microsoft.AspNetCore.Components

            Public Class Counter
                Inherits ComponentBase

                <Parameter> Public Property Step As Integer
                <Parameter> Public Property Value As Integer
                <Parameter> Public Property ValueChanged As EventCallback(Of Integer)
            End Class

            Public Module Program
                Public Sub Main()
                End Sub
            End Module
            """);
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    [Fact]
    public async Task TheComponentsParametersAndItsBindingAreOffered()
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        const string before = "<h1>Home</h1>\n<Counter ";
        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Home.vbrazor"));
        documents.Update(uri.ToString(), before + "/>\n", 1);

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(1, "<Counter ".Length)
        }, default);

        var labels = items.Select(item => item.Label).ToList();

        Assert.Contains("Step", labels);
        Assert.Contains("Value", labels);
        Assert.Contains("@bind-Value", labels);
        Assert.DoesNotContain("@bind-Step", labels);
        Assert.Equal("Integer", items.Single(item => item.Label == "Step").Detail);
    }

    [Fact]
    public async Task AViewsTagsAreNotComponents()
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;

        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Index.vbhtml"));
        documents.Update(uri.ToString(), "<Counter />\n", 1);

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, "<Counter ".Length)
        }, default);

        Assert.DoesNotContain(items, item => item.Label == "Step");
    }
}
