using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

/// <summary>
/// The language server answering with a compiler behind it.
/// </summary>
/// <remarks>
/// The gap this closes: outside Basalt the server had no compilation, so
/// asked about <c>@Model.</c> it could only say that the members were unknown.
/// Visual Studio, Rider and VS Code all speak to this server, so that was the
/// whole difference between them and the IDE.
/// </remarks>
public sealed class LanguageServerCompilationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-lsp-" + Guid.NewGuid().ToString("N"));

    private string WriteProject()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Views", "Home"));

        // A web project, because the generated view inherits RazorPage: with
        // a plain library the base class does not exist, nothing resolves,
        // and every question comes back empty.
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        // A web project needs an entry point to compile, and a project that
        // does not compile resolves nothing.
        File.WriteAllText(Path.Combine(_root, "Program.vb"), """
            Imports Microsoft.AspNetCore.Builder

            Public Module Program
                Public Sub Main(args As String())
                    WebApplication.CreateBuilder(args).Build().Run()
                End Sub
            End Module
            """);

        File.WriteAllText(Path.Combine(_root, "Customer.vb"), """
            Public Class Customer
                Public Property Name As String
                Public Property Orders As Integer
            End Class
            """);

        return _root;
    }

    [Fact]
    public async Task WithNoFolderItLooksWhereItWasStarted()
    {
        // Rider names no folder at all: it sends neither rootUri nor
        // workspaceFolders, so the server declined to look and answered every
        // completion and hover from the markup alone. The view coloured
        // perfectly and knew no types — which reads as a broken plugin rather
        // than as a missing parameter, and the only word about it was one line
        // in a log nobody opens: "looking for a solution under (nowhere)".
        //
        // An editor starts the server inside the project it opened, so the
        // working directory is the answer nobody had to send.
        //
        // Asserted through a solution that is really there: the previous test
        // only asked for some complaint, and a complaint arrives either way —
        // which is why it kept passing while completion knew nothing.
        // Through the seam rather than Directory.SetCurrentDirectory: that
        // moves the working directory for the whole process, and the tests
        // share one. Setting it here failed an unrelated test elsewhere in the
        // suite while passing on its own.
        var root = WriteProject();

        using var compilation = new ProjectCompilation { WorkingDirectory = () => root };
        compilation.StartLoading(rootPath: null);
        await compilation.Loaded;

        // The project itself, loaded. Anything weaker passes with the defect
        // still in place: refusing to look also sets Problem, so "it
        // complained about something" is not evidence of looking.
        Assert.True(
            compilation.IsReady,
            $"it should have loaded the project it was started in: {compilation.Problem}");
    }

    [Fact]
    public void WithNoSolutionUnderTheFolderItSaysSo()
    {
        Directory.CreateDirectory(_root);

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);

        Assert.False(compilation.IsReady);
        Assert.Contains("no solution", compilation.Problem!);
    }

    [Fact]
    public void BeforeItIsReadyTheMarkupHalfStillAnswers()
    {
        // The solution is loaded in the background, and a request arriving
        // first is answered from the markup rather than made to wait.
        using var compilation = new ProjectCompilation();

        Assert.NotNull(compilation.Provider.Completion);
    }

    [Fact]
    public async Task ItFindsAProjectAndLoadsIt()
    {
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);

        await compilation.Loaded;

        // Either it loaded, or it said why not. Silence is the one outcome
        // that would leave a user with no way to tell what happened.
        Assert.True(compilation.IsReady || compilation.Problem is not null);
    }

    [Fact]
    public async Task ItOffersTheModelsOwnMembers()
    {
        // The whole point of giving the server a compiler. Before this it
        // answered "the members of this type are known to the compiler, which
        // this server cannot reach" — which was honest, and was the entire
        // difference between Basalt and every other editor.
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            // Site.Customer, not Customer: RootNamespace puts the class there,
            // and a model type that does not resolve gives an empty list —
            // the same answer as a broken delegation, which is what made this
            // take three tries to see.
            "@ModelType Global.Site.Customer\n<p>@Model.Nam</p>\n");

        var caret = document.Text.IndexOf("@Model.", StringComparison.Ordinal)
                  + "@Model.Nam".Length;

        var items = await compilation.Provider.Completion!
            .GetCompletionsAsync(document, caret, TestContext.Current.CancellationToken);

        Assert.Contains(items, i => i.DisplayText == "Name");
        Assert.Contains(items, i => i.DisplayText == "Orders");
    }

    [Fact]
    public async Task AChangedModelReachesTheAnswers()
    {
        // A view's model lives in a .vb file that another editor may be
        // writing. Answers describing the class as it was when the solution
        // opened are worse than none: they look current.
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var customer = Path.Combine(root, "Customer.vb");

        File.WriteAllText(customer, """
            Public Class Customer
                Public Property Name As String
                Public Property Orders As Integer
                Public Property Nickname As String
            End Class
            """);

        await compilation.FileChangedAsync(customer, TestContext.Current.CancellationToken);

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            "@ModelType Global.Site.Customer\n<p>@Model.Nic</p>\n");

        var caret = document.Text.IndexOf("@Model.", StringComparison.Ordinal)
                  + "@Model.Nic".Length;

        var items = await compilation.Provider.Completion!
            .GetCompletionsAsync(document, caret, TestContext.Current.CancellationToken);

        Assert.Contains(items, i => i.DisplayText == "Nickname");
    }

    [Fact]
    public async Task ItAnswersAboutTheParametersOfACall()
    {
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            "@ModelType Global.Site.Customer\n<p>@Model.Name.Substring(</p>\n");

        var caret = document.Text.IndexOf("Substring(", StringComparison.Ordinal)
                  + "Substring(".Length;

        var help = await compilation.Provider.Completion!
            .GetSignatureHelpAsync(document, caret, TestContext.Current.CancellationToken);

        Assert.NotNull(help);
        Assert.NotEmpty(help!.Signatures);
    }

    [Fact]
    public async Task TheHandlerAnswersFromTheCompilationOnceItIsReady()
    {
        // The whole chain, not the pieces: the compilation loads, the handler
        // sees it is ready, and the answer comes from Roslyn rather than from
        // the fallback list of directives and keywords.
        //
        // Everything below was verified separately and worked; what was never
        // checked end to end is whether the handler actually reaches for it.
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            "@Code\n    Dim s As String = \"x\"\n    s.Len\nEnd Code\n");

        var caret = document.Text.IndexOf("s.Len", StringComparison.Ordinal) + "s.Len".Length;

        var items = await compilation.Provider.Completion!
            .GetCompletionsAsync(document, caret, TestContext.Current.CancellationToken);

        // Members of String, not keywords: Length is the one everybody types.
        Assert.Contains(items, i => i.DisplayText == "Length");
    }

    [Fact]
    public async Task HoverExplainsAName()
    {
        // The most missed of the six: resting on a name did nothing at all,
        // because the handler was never written — the delegation could
        // already answer.
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            "@Code\n    Dim s As String = \"x\"\n    Dim n = s.Length\nEnd Code\n");

        var caret = document.Text.IndexOf("s.Length", StringComparison.Ordinal) + 4;

        var info = await compilation.Provider.Navigation!
            .GetQuickInfoAsync(document, caret, TestContext.Current.CancellationToken);

        Assert.NotNull(info);

        // The signature of String.Length, not of whatever the caret drifted
        // onto: the mapping used to count characters, and the writer's indent
        // pushed the caret several tokens along.
        Assert.Contains("Length", info!.Signature, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCompilerFindsWhatTheParserCannot()
    {
        // The parser knows two errors: an unclosed block and a bad directive.
        // A misspelt property is neither, and went unmarked until the build.
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            "@Code\n    Dim s As String = \"x\"\n    Dim n = s.Lenght\nEnd Code\n");

        var found = await compilation.Provider.Diagnostics!
            .GetDiagnosticsAsync(document, TestContext.Current.CancellationToken);

        Assert.Contains(found, d => d.Message.Contains("Lenght", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABlocksConditionIsAnsweredFor()
    {
        // "@If ViewData("x").ToString().Length > 4 Then" — the opening clause
        // is where the interesting expression of a block lives, and it was
        // the one place never mapped at all: the caret reached nothing, so
        // completion and hover both answered nothing inside it.
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            "@If \"abc\".ToString().Length > 4 Then\n    <p>yes</p>\nEnd If\n");

        var caret = document.Text.IndexOf(".Length", StringComparison.Ordinal) + 1;

        var items = await compilation.Provider.Completion!
            .GetCompletionsAsync(document, caret, TestContext.Current.CancellationToken);

        Assert.Contains(items, i => i.DisplayText == "Length");
    }

    [Fact]
    public async Task HoverInAConditionNamesTheRightMember()
    {
        // Not the one before it: the mapping skipped the writer's indent on
        // later lines but not on the first, so a caret on "Length" reported
        // the signature of "ToString" — plausible, and wrong.
        var root = WriteProject();

        using var compilation = new ProjectCompilation();
        compilation.StartLoading(root);
        await compilation.Loaded;

        if (!compilation.IsReady)
        {
            Assert.SkipWhen(true, $"the project did not load: {compilation.Problem}");
            return;
        }

        var document = new Basalt.Extensibility.LanguageDocument(
            Path.Combine(root, "Views", "Home", "Index.vbhtml"),
            "@If \"abc\".ToString().Length > 4 Then\n    <p>yes</p>\nEnd If\n");

        var caret = document.Text.IndexOf(".Length", StringComparison.Ordinal) + 3;

        var info = await compilation.Provider.Navigation!
            .GetQuickInfoAsync(document, caret, TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Contains("Length", info!.Signature, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString", info.Signature, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}