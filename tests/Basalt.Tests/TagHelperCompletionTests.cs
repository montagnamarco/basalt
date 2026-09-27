using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// A view's asp-* attributes offered from the tag helpers it has in scope.
/// </summary>
/// <remarks>
/// "&lt;a " offered HTML's attributes and none of the anchor tag helper's:
/// asp-action and asp-route-id were typed from memory, and a misspelt one
/// was written into the page as a plain attribute, silently.
/// </remarks>
public sealed class TagHelperCompletionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-taghelper-completion-" + Guid.NewGuid().ToString("N"));

    public TagHelperCompletionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Views", "Home"));
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), """
            Public Module Program
                Public Sub Main()
                End Sub
            End Module
            """);
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    private async Task<IReadOnlyList<string>> LabelsAsync(string view)
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var path = Path.Combine(_root, "Views", "Home", "Index.vbhtml");
        await File.WriteAllTextAsync(path, view);

        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(path);
        documents.Update(uri.ToString(), view, 1);

        var lines = view.Split('\n');
        var line = Array.FindIndex(lines, candidate => candidate.StartsWith("<a ", StringComparison.Ordinal));

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(line, "<a ".Length)
        }, default);

        return [.. items.Select(item => item.Label)];
    }

    [Fact]
    public async Task TheAnchorTagHelpersAttributesAreOffered()
    {
        var labels = await LabelsAsync("@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers\n<a >Home</a>\n");

        Assert.Contains("asp-action", labels);
        Assert.Contains("asp-controller", labels);
        Assert.Contains("asp-route-", labels);
        Assert.Contains("href", labels);
    }

    [Fact]
    public async Task TheViewImportsAboveTheViewBringThemIntoScope()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Views", "_ViewImports.vbhtml"),
            "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers\n");

        var labels = await LabelsAsync("<a >Home</a>\n");

        Assert.Contains("asp-action", labels);
    }

    [Fact]
    public async Task WithoutAddTagHelperNoneAreOffered()
    {
        var labels = await LabelsAsync("<a >Home</a>\n");

        Assert.DoesNotContain("asp-action", labels);
    }
}
