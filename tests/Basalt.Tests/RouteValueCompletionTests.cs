using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// The values asp-controller, asp-action and asp-page can take, from the
/// project, inside a view's tag.
/// </summary>
/// <remarks>
/// Each was a string typed from memory: a controller or action misspelt
/// made a link to nowhere, found only by clicking it.
/// </remarks>
public sealed class RouteValueCompletionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-route-completion-" + Guid.NewGuid().ToString("N"));

    public RouteValueCompletionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Views", "Orders"));
        Directory.CreateDirectory(Path.Combine(_root, "Pages", "Admin"));
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Controllers.vb"), """
            Imports Microsoft.AspNetCore.Mvc

            Public Class OrdersController
                Inherits Controller

                Public Function Index() As IActionResult
                    Return View()
                End Function

                Public Function Details(id As Integer) As IActionResult
                    Return View()
                End Function

                <NonAction> Public Function Helper() As Integer
                    Return 1
                End Function
            End Class

            Public Class ApiController
                Inherits ControllerBase

                Public Function Ping() As String
                    Return "pong"
                End Function
            End Class

            Public Module Program
                Public Sub Main()
                End Sub
            End Module
            """);
        File.WriteAllText(Path.Combine(_root, "Pages", "Index.vbhtml"), "@Page\n<h1>Home</h1>\n");
        File.WriteAllText(Path.Combine(_root, "Pages", "Admin", "Users.vbhtml"), "@Page\n<h1>Users</h1>\n");
        File.WriteAllText(Path.Combine(_root, "Pages", "_Layout.vbhtml"), "@RenderBody()\n");
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    private async Task<IReadOnlyList<string>> ValuesAsync(string view, string before)
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var path = Path.Combine(_root, "Views", "Orders", "Index.vbhtml");
        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(path);
        documents.Update(uri.ToString(), view, 1);

        var offset = view.IndexOf(before, StringComparison.Ordinal) + before.Length;
        var (line, character) = VbHtmlSemanticTokensHandler.LineAndCharacterOf(view, offset);

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(line, character)
        }, default);

        return [.. items.Select(item => item.Label)];
    }

    [Fact]
    public async Task TheProjectsControllersAreOffered()
    {
        var values = await ValuesAsync("<a asp-controller=\"\">x</a>\n", "asp-controller=\"");

        Assert.Contains("Orders", values);
        Assert.Contains("Api", values);
    }

    [Fact]
    public async Task TheActionsOfTheControllerTheTagNamesAreOffered()
    {
        var values = await ValuesAsync("<a asp-controller=\"Api\" asp-action=\"\">x</a>\n", "asp-action=\"");

        Assert.Contains("Ping", values);
        Assert.DoesNotContain("Details", values);
    }

    [Fact]
    public async Task WithoutAControllerTheViewsOwnActionsAreOffered()
    {
        // The view sits in Views/Orders, so its controller is Orders.
        var values = await ValuesAsync("<a asp-action=\"\">x</a>\n", "asp-action=\"");

        Assert.Contains("Index", values);
        Assert.Contains("Details", values);
        Assert.DoesNotContain("Helper", values);
    }

    [Fact]
    public async Task TheProjectsPagesAreOffered()
    {
        var values = await ValuesAsync("<a asp-page=\"\">x</a>\n", "asp-page=\"");

        Assert.Contains("/Index", values);
        Assert.Contains("/Admin/Users", values);
        Assert.DoesNotContain("/_Layout", values);
    }
}
