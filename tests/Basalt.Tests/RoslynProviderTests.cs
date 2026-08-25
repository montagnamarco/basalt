using Basalt.Extensibility;
using Basalt.Workspace;
using Basalt.Workspace.Languages;

namespace Basalt.Tests;

/// <summary>
/// Visual Basic through the extensibility contracts.
///
/// The abstraction has to carry the language the IDE already supports before it
/// can be trusted with one it does not, so these run against a real project.
/// </summary>
public sealed class RoslynProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-provider", Guid.NewGuid().ToString("N"));

    private RoslynLanguageProvider? _provider;

    public RoslynProviderTests() => Directory.CreateDirectory(_root);

    private async Task<(RoslynLanguageProvider Provider, LanguageDocument Document)> OpenAsync(
        string code)
    {
        var project = Path.Combine(_root, "Probe.vbproj");
        var source = Path.Combine(_root, "Program.vb");

        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(source, code);

        _provider = RoslynLanguageProvider.CreateVisualBasic(
            new RoslynLanguageService(), new RoslynFormattingService());

        await _provider.OpenSolutionAsync(project);

        return (_provider, new LanguageDocument(source, code) { ProjectPath = project });
    }

    [Fact]
    public void DescribesVisualBasicAsCaseInsensitive()
    {
        var provider = RoslynLanguageProvider.CreateVisualBasic(
            new RoslynLanguageService(), new RoslynFormattingService());
        _provider = provider;

        Assert.Equal("vb", provider.Identity.Id);
        Assert.Contains(".vb", provider.Identity.FileExtensions);
        Assert.False(provider.Identity.IsCaseSensitive);
    }

    [Fact]
    public async Task OffersCompletionsThroughTheContract()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim s As String = "x"
                    s.
                End Sub
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var items = await provider.Completion!.GetCompletionsAsync(
            document, code.IndexOf("s.", StringComparison.Ordinal) + 2);

        Assert.NotEmpty(items);
        Assert.Contains(items, i => i.DisplayText == "Length");
    }

    [Fact]
    public async Task ReportsDiagnosticsThroughTheContract()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim n As Integer = undefinedThing
                End Sub
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var diagnostics = await provider.Diagnostics!.GetDiagnosticsAsync(document);

        Assert.Contains(diagnostics,
            d => d.Id == "BC30451" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ListsDocumentSymbolsNestedAsWritten()
    {
        const string code = """
            Public Class Customer
                Public Property Name As String

                Public Sub Save()
                End Sub

                Public Function Total() As Integer
                    Return 0
                End Function
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var symbols = await provider.Navigation!.GetDocumentSymbolsAsync(document);

        var customer = Assert.Single(symbols);
        Assert.Equal("Customer", customer.Name);
        Assert.Equal(SymbolKind.Class, customer.Kind);

        var names = customer.Children.Select(c => c.Name).ToList();
        Assert.Contains("Name", names);
        Assert.Contains("Save", names);
        Assert.Contains("Total", names);
    }

    [Fact]
    public async Task FindsWhereASymbolIsDeclared()
    {
        const string code = """
            Public Class Probe
                Public Sub Target()
                End Sub

                Public Sub Caller()
                    Target()
                End Sub
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var position = code.LastIndexOf("Target", StringComparison.Ordinal) + 2;
        var location = await provider.Navigation!.GoToDefinitionAsync(document, position);

        Assert.NotNull(location);
        Assert.EndsWith("Program.vb", location!.FilePath);
        Assert.Equal(2, location.Range.Start.Line);
    }

    [Fact]
    public async Task FindsEveryUseOfASymbol()
    {
        const string code = """
            Public Class Probe
                Public Sub Target()
                End Sub

                Public Sub A()
                    Target()
                End Sub

                Public Sub B()
                    Target()
                End Sub
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var position = code.IndexOf("Target", StringComparison.Ordinal) + 2;
        var references = await provider.Navigation!.FindReferencesAsync(document, position);

        // The declaration plus the two calls.
        Assert.True(references.Count >= 3,
            $"expected the declaration and both calls, found {references.Count}");
    }

    [Fact]
    public async Task DescribesASymbolOnHover()
    {
        const string code = """
            Imports System

            Public Class Probe
                Public Sub M()
                    Console.WriteLine("x")
                End Sub
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var position = code.IndexOf("WriteLine", StringComparison.Ordinal) + 3;
        var info = await provider.Navigation!.GetQuickInfoAsync(document, position);

        Assert.NotNull(info);
        Assert.Contains("WriteLine", info!.Signature);
    }

    [Fact]
    public async Task SearchesSymbolsAcrossTheSolution()
    {
        const string code = """
            Public Class DistinctiveName
            End Class
            """;

        var (provider, _) = await OpenAsync(code);

        var found = await provider.Navigation!.SearchSymbolsAsync("Distinctive");

        Assert.NotEmpty(found);
    }

    [Fact]
    public async Task AppliesVisualBasicTypingConventions()
    {
        const string code = """
            Public Class Probe
                Sub M()
                    dim n as integer=5
                End Sub
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var caret = code.IndexOf("=5", StringComparison.Ordinal) + 2;
        var result = await provider.Formatting!.FormatLineAsync(document, caret);

        Assert.True(result.Changed);
        Assert.Contains("Dim n As Integer = 5", result.Text);
    }

    [Fact]
    public async Task ReportsTheClosingOfABlockJustOpened()
    {
        const string code = """
            Public Class Probe
                Sub M()
                    If x > 0 Then
            """;

        var (provider, document) = await OpenAsync(code);

        var closing = await provider.Formatting!.GetBlockClosingAsync(document, lineIndex: 2);

        Assert.Equal("End If", closing);
    }

    [Fact]
    public async Task NamesASyntaxDefinitionForColouring()
    {
        var (provider, _) = await OpenAsync("Public Class Probe\nEnd Class");

        Assert.Equal("VB", provider.Highlighting!.BuiltInDefinitionName);
    }

    [Fact]
    public async Task CompilesThroughTheBackend()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                End Sub
            End Class
            """;

        var (provider, document) = await OpenAsync(code);

        var result = await provider.Compiler!.CompileAsync(
            document.ProjectPath!, new CompilationTarget("osx-arm64") { Optimise = false });

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        Assert.True(result.Succeeded, string.Join("\n", errors));
        Assert.NotNull(result.OutputPath);
    }

    public void Dispose()
    {
        _provider?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
