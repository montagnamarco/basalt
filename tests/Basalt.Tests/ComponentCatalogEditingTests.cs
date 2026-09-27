using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Basalt.Workspace;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// The editor and the language server write .vbrazor components the same
/// way the build does, using the project's component catalog.
/// </summary>
/// <remarks>
/// Without a catalog the writer cannot tell a named RenderFragment parameter
/// from a component of that name, and cannot declare @context inside a typed
/// RenderFragment: &lt;Header&gt; inside &lt;Card&gt; was opened as a
/// component called Header, which does not exist, and @context was an
/// undeclared name — both compiler errors on code the build accepts without
/// complaint. Compare with <c>TypedComponentTests</c>, which proves the same
/// thing on the build's own side.
/// </remarks>
public sealed class ComponentCatalogEditingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-catalog-" + Guid.NewGuid().ToString("N"));

    private const string CardText = """
        <div>@Header</div>
        <div>@Row("x")</div>
        @Code
            <Microsoft.AspNetCore.Components.Parameter> Public Property Header As Microsoft.AspNetCore.Components.RenderFragment
            <Microsoft.AspNetCore.Components.Parameter> Public Property Row As Microsoft.AspNetCore.Components.RenderFragment(Of String)
        End Code
        """;

    private const string PageText = """
        <Card>
            <Header><b>Title</b></Header>
            <Row>@context.ToUpper()</Row>
        </Card>
        """;

    private const string GeneratedPath = "__ComponentCatalogTestGenerated.vb";

    /// <summary>
    /// A Blazor project: a plain web project resolves ComponentBase,
    /// RenderFragment and ParameterAttribute through the ASP.NET Core
    /// framework reference the Web SDK brings in, with no package of ours
    /// needed for the catalog itself.
    /// </summary>
    private string WriteProject()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Components"));

        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
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

        File.WriteAllText(Path.Combine(_root, "Components", "Card.vbrazor"), CardText);
        File.WriteAllText(Path.Combine(_root, "Components", "Page.vbrazor"), PageText);

        return _root;
    }

    /// <summary>
    /// The provider exactly as the IDE and the language server build it —
    /// through <see cref="WebLanguageProviders.VbRazorAsking"/> — with the
    /// catalog wiring present or withheld, so the two tests below differ in
    /// nothing but the one thing under test.
    /// </summary>
    private static ILanguageProvider BuildProvider(RoslynLanguageService roslyn, bool withCatalog) =>
        WebLanguageProviders.VbRazorAsking(
            ask: (_, _, _) => Task.FromResult<IReadOnlyList<CompletionItem>>([]),
            askDiagnostics: (generated, ct) => roslyn.GetDiagnosticsAsync(GeneratedPath, generated, ct),
            host: ViewHost.AspNetCore,
            askCatalog: withCatalog
                ? (templatePath, currentText, ct) => roslyn.GetComponentCatalogAsync(templatePath, currentText, ct)
                : null);

    [Fact]
    public async Task WithTheCatalogThePageCompilesCleanly()
    {
        var root = WriteProject();
        var pagePath = Path.Combine(root, "Components", "Page.vbrazor");

        using var roslyn = new RoslynLanguageService();
        await roslyn.OpenSolutionAsync(
            Path.Combine(root, "Site.vbproj"), TestContext.Current.CancellationToken);

        var provider = BuildProvider(roslyn, withCatalog: true);
        var document = new LanguageDocument(pagePath, PageText);

        var diagnostics = await provider.Diagnostics!.GetDiagnosticsAsync(
            document, TestContext.Current.CancellationToken);

        // Every diagnostic the compiler half can report is already filtered
        // to errors before it leaves VbHtmlDiagnosticProvider, so an empty
        // list is the whole claim: the generated code compiled.
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task WithoutTheCatalogTheSamePageFailsToCompile()
    {
        // The regression test for the regression test: with the catalog
        // withheld — exactly what TemplateGeneration.For did before this
        // change — the same page must fail, so the passing assertion above
        // is proof of the fix rather than proof the page was fine regardless.
        var root = WriteProject();
        var pagePath = Path.Combine(root, "Components", "Page.vbrazor");

        using var roslyn = new RoslynLanguageService();
        await roslyn.OpenSolutionAsync(
            Path.Combine(root, "Site.vbproj"), TestContext.Current.CancellationToken);

        var provider = BuildProvider(roslyn, withCatalog: false);
        var document = new LanguageDocument(pagePath, PageText);

        var diagnostics = await provider.Diagnostics!.GetDiagnosticsAsync(
            document, TestContext.Current.CancellationToken);

        // <Header> opened as an unknown component, or @context left
        // undeclared inside <Row> — either is the defect this catalog fixes.
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Header", StringComparison.Ordinal) ||
            d.Message.Contains("context", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
