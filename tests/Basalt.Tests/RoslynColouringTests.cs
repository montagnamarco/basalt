using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// View code coloured by what Roslyn says each name is, in the editors that
/// use the language server.
/// </summary>
/// <remarks>
/// The scan could only guess at names: a class was plain text, and every
/// name after a dot a property, methods included. Roslyn's classification of
/// the generated view is carried back to the template through the verified
/// mappings, so a name is coloured where it stands or not at all.
/// </remarks>
public sealed class RoslynColouringTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-lsp-colour-" + Guid.NewGuid().ToString("N"));

    public RoslynColouringTests()
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

    [Fact]
    public async Task EachNameIsColouredAsWhatItIs()
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        // The call sits on the block's second line, where the span mapping
        // drifts by the writer's indent.
        const string view = "@Code\n    Dim builder As New System.Text.StringBuilder()\n" +
                            "    builder.Append(DateTime.Now.Year)\nEnd Code\n<p>@builder.Length</p>\n" +
                            "@If  builder.Length > 0 Then\n    <b>some</b>\nEnd If\n";

        var path = Path.Combine(_root, "Index.vbhtml");
        var document = new OpenDocument(new Uri(path).AbsoluteUri, view, 1);

        var names = await compilation.ClassifyNamesAsync(path, view, default);
        var tokens = VbHtmlSemanticTokensHandler.WithNames(document, VbHtmlSemanticTokensHandler.Tokens(document), names);

        var lines = view.Split('\n');
        var coloured = tokens
            .Select(token => (Text: lines[token.Line].Substring(token.Character, token.Length), token.Type))
            .ToList();

        Assert.Contains(("StringBuilder", SemanticTokenType.Class), coloured);
        Assert.Contains(("Append", SemanticTokenType.Method), coloured);
        Assert.Contains(("DateTime", SemanticTokenType.Struct), coloured);
        Assert.Contains(("Year", SemanticTokenType.Property), coloured);
        Assert.Contains(("Length", SemanticTokenType.Property), coloured);
        Assert.True(coloured.Count(token => token == ("builder", SemanticTokenType.Variable)) >= 3);

        // A block opening the parser normalises ("@If  x", two spaces) is
        // placed by neither line nor mapping; the arithmetic that would place
        // it drifts by the writer's indent. Uncoloured is right, coloured
        // next door is not.
        var ifLine = Array.FindIndex(lines, line => line.StartsWith("@If  ", StringComparison.Ordinal));

        foreach (var token in tokens.Where(token => token.Line == ifLine && token.Type != SemanticTokenType.Keyword))
            Assert.Contains(lines[ifLine].Substring(token.Character, token.Length), new[] { "builder", "Length", "0" });

        // The scan's keywords stay, and nothing overlaps.
        Assert.Contains(("Dim", SemanticTokenType.Keyword), coloured);

        foreach (var group in tokens.GroupBy(token => token.Line))
        {
            var ordered = group.OrderBy(token => token.Character).ToList();

            for (var index = 1; index < ordered.Count; index++)
                Assert.True(ordered[index - 1].Character + ordered[index - 1].Length <= ordered[index].Character,
                    $"tokens overlap on line {group.Key}");
        }
    }
}
