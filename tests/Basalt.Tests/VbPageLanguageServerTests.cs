using System.Text.Json;
using Basalt.Web;
using Microsoft.CodeAnalysis.Text;
using LspHarness = Basalt.Tests.LanguageServerProtocolTests.LspHarness;

namespace Basalt.Tests;

public sealed class VbPageLanguageServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basalt-page-lsp-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => ScratchFolder.Delete(_root);

    [Fact]
    public async Task MultilinePageDefinitionCompletionAndDiagnosticUseRealUtf16Positions()
    {
        Directory.CreateDirectory(_root);
        var runtime = System.Security.SecurityElement.Escape(typeof(VbPage).Assembly.Location);
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>Site</RootNamespace></PropertyGroup>
              <ItemGroup><Reference Include="Basalt.Razor.Vb.AspNetCore"><HintPath>{{runtime}}</HintPath></Reference></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), """
            Imports Microsoft.AspNetCore.Builder
            Public Module Program
                Public Sub Main(args As String())
                    WebApplication.CreateBuilder(args).Build().Run()
                End Sub
            End Module
            """);
        var uri = new Uri(Path.Combine(_root, "Index.vbpage")).AbsoluteUri;
        var text = string.Join("\r\n",
            "<h1>header 😀</h1>",
            "<%",
            "    Dim count = 3",
            "%>",
            "<%!",
            "Private Function Twice(value As Integer) As Integer",
            "    Return value * 2",
            "End Function",
            "%>",
            "<p>😀 <%= Twice(count) %> <%= missingName %> <%= \"hello\".ToU %></p>");
        await using var client = new LspHarness();
        await client.InitializeAsync(_root);
        await client.InitializedAsync();
        var compilation = await client.CompilationAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(compilation.IsReady, compilation.Problem);
        await client.DidOpenAsync(uri, text);

        var use = text.LastIndexOf("Twice", StringComparison.Ordinal);
        var at = SourceText.From(text).Lines.GetLinePosition(use + 1);
        var definition = await client.RequestAsync("textDocument/definition", new
        {
            textDocument = new { uri }, position = new { line = at.Line, character = at.Character }
        });
        var found = definition.ValueKind == JsonValueKind.Array ? Assert.Single(definition.EnumerateArray()) : definition;
        var declared = SourceText.From(text).Lines.GetLinePosition(text.IndexOf("Twice", StringComparison.Ordinal));
        Assert.Equal(declared.Line, found.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(declared.Character, found.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());

        var caret = SourceText.From(text).Lines.GetLinePosition(text.IndexOf(".ToU", StringComparison.Ordinal) + ".ToU".Length);
        var completions = await client.RequestAsync("textDocument/completion", new
        {
            textDocument = new { uri }, position = new { line = caret.Line, character = caret.Character }
        });
        var items = completions.ValueKind == JsonValueKind.Array ? completions : completions.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("label").GetString() == "ToUpper");

        var missing = SourceText.From(text).Lines.GetLinePosition(text.IndexOf("missingName", StringComparison.Ordinal));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var diagnostics = client.ServerMessages.Where(message =>
                    message.GetProperty("method").GetString() == "textDocument/publishDiagnostics")
                .SelectMany(message => message.GetProperty("params").GetProperty("diagnostics").EnumerateArray())
                .ToArray();
            if (diagnostics.Any(diagnostic => diagnostic.GetProperty("code").ToString() == "BC30451" &&
                diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32() == missing.Line &&
                diagnostic.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32() == missing.Character))
                break;
            Assert.True(DateTime.UtcNow < deadline, "The page error did not reach the actual UTF-16 position over LSP.");
            await Task.Delay(50);
        }
    }
}
