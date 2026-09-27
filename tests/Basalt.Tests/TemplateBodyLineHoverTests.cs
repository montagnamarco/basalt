using Basalt.Workspace.Web;
using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// Hovering a call on a later line of a multi-line code block, or of a
/// method declared in @Functions, describes that call.
/// </summary>
/// <remarks>
/// The writer re-indents such a body, and the span mapping drifted by each
/// line's indentation: on the third line of a @Code block, Math.Max was
/// described as the variable declared on the first. Inside @Functions the
/// caret was not counted as code at all, and nothing was described.
/// </remarks>
public sealed class TemplateBodyLineHoverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-body-hover-" + Guid.NewGuid().ToString("N"));

    public TemplateBodyLineHoverTests()
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
        File.WriteAllText(Path.Combine(_root, "Program.vb"), """
            Public Module Program
                Public Sub Main()
                End Sub
            End Module
            """);
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    [Theory]
    [InlineData("Code.vbhtml",
        "@Code\n    Dim first = 1\n    Dim second = 2\n    Dim total = Math.Max(first, second)\nEnd Code\n<p>Hi</p>")]
    [InlineData("Functions.vbhtml",
        "<p>Hi</p>\n@Functions\n    Function Twice(x As Integer) As Integer\n        Dim total = Math.Max(x, 2)\n        Return total\n    End Function\nEnd Functions\n")]
    public async Task TheCallUnderTheCaretIsDescribed(string name, string text)
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(_root, name));
        documents.Update(uri.ToString(), text, 1);

        var lines = text.Split('\n');
        var line = Array.FindIndex(lines, candidate => candidate.Contains("Math.Max", StringComparison.Ordinal));
        var column = lines[line].IndexOf("Max", StringComparison.Ordinal) + 1;

        var hover = await new VbHtmlHoverHandler(documents, compilation).Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(line, column)
        }, default);

        Assert.NotNull(hover);
        Assert.Contains("Math.Max(", hover.Contents.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADefinitionInsideFunctionsComesBackToItsOwnName()
    {
        // The way back drifted as the way there did: the declaration of
        // "total", two lines into the method, came back on "Math.Max(x".
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        const string text = "<p>Hi</p>\n@Functions\n    Function Twice(x As Integer) As Integer\n" +
                            "        Dim total = Math.Max(x, 2)\n        Return total\n    End Function\nEnd Functions\n";
        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Definition.vbhtml"));
        documents.Update(uri.ToString(), text, 1);

        var found = await new VbHtmlDefinitionHandler(documents, compilation).Handle(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(4, "        Return to".Length)
        }, default);

        var location = Assert.Single(found!).Location!;

        Assert.Equal(3, location.Range.Start.Line);
        Assert.Equal("        Dim ".Length, location.Range.Start.Character);
    }

    [Fact]
    public async Task ADefinitionThatBeginsItsLineComesBackToIt()
    {
        // A label is its line's first token: nothing precedes it to match,
        // and it still has an exact place to come back to.
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        const string text = "<p>Hi</p>\n@Functions\n    Sub Retry()\n        Dim tries = 0\n" +
                            "        Again:\n        tries += 1\n        If tries < 3 Then GoTo Again\n    End Sub\nEnd Functions\n";
        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Label.vbhtml"));
        documents.Update(uri.ToString(), text, 1);

        var found = await new VbHtmlDefinitionHandler(documents, compilation).Handle(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(6, "        If tries < 3 Then GoTo Ag".Length)
        }, default);

        var location = Assert.Single(found!).Location!;

        Assert.Equal(4, location.Range.Start.Line);
        Assert.Equal("        ".Length, location.Range.Start.Character);
    }

    [Fact]
    public async Task ReferencesThatBeginTheirLineComeBackToThem()
    {
        // "total = total + 1": the use that starts the line has nothing before
        // it to match, and came back several characters on.
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        const string text = "<p>Hi</p>\n@Functions\n    Function Sum() As Integer\n        Dim total = 0\n" +
                            "        total = total + 1\n        Return total\n    End Function\nEnd Functions\n";
        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "References.vbhtml"));
        documents.Update(uri.ToString(), text, 1);

        var found = await new VbHtmlReferencesHandler(documents, compilation).Handle(new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(3, "        Dim to".Length),
            Context = new ReferenceContext { IncludeDeclaration = true }
        }, default);

        var places = found!
            .Where(location => location.Uri.GetFileSystemPath().EndsWith("References.vbhtml", StringComparison.OrdinalIgnoreCase))
            .Select(location => (location.Range.Start.Line, location.Range.Start.Character))
            .OrderBy(place => place)
            .ToList();

        Assert.Equal(
            [(3, 12), (4, 8), (4, 16), (5, 15)],
            places);
    }

    [Fact]
    public async Task HoveringTheIndentationOfABodyLineDescribesNothing()
    {
        // Blank space has no symbol; the line mapping used to answer with the
        // line's first token.
        const string template = "@Code\n    Dim first = 1\n    Dim second = 2\nEnd Code\n";
        var generated = await TemplateGeneration.ForAsync("Indent.vbhtml", Basalt.Razor.Vb.ViewHost.AspNetCore, template, null, default);
        var inIndent = template.IndexOf("    Dim second", StringComparison.Ordinal) + 2;

        Assert.Null(TemplateGeneration.StatementLineCaret("Indent.vbhtml", template, generated, inIndent));
    }
}
