using System.Diagnostics;
using System.Globalization;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Workspace;
using Basalt.Workspace.Testing;

namespace Basalt.Tests;

/// <summary>
/// The dotnet processes the IDE starts answer in the IDE's language.
///
/// MSBuild and the compilers use the operating system's language unless
/// DOTNET_CLI_UI_LANGUAGE says otherwise, so an English IDE on an Italian
/// machine listed "'y' non è dichiarato" in Problems and "Compilazione
/// riuscita" in Output.
/// </summary>
[Collection("Localizer culture")]
public sealed class DotnetProcessLanguageTests : IDisposable
{
    private readonly CultureInfo _culture = Localizer.Culture;
    private readonly string? _userLanguage = Environment.GetEnvironmentVariable(DotnetProcess.LanguageVariable);
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-dotnetlang-").FullName;

    public DotnetProcessLanguageTests() =>
        // A developer machine may set the variable itself; the tests that
        // expect the IDE's language start from an environment that does not.
        Environment.SetEnvironmentVariable(DotnetProcess.LanguageVariable, null);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DotnetProcess.LanguageVariable, _userLanguage);
        Localizer.Culture = _culture;
        try { Directory.Delete(_root, true); } catch { }
    }

    private string MissingProject => Path.Combine(_root, "Missing.vbproj");

    private static string? Language(ProcessStartInfo? start) =>
        start?.Environment.TryGetValue(DotnetProcess.LanguageVariable, out var value) == true ? value : null;

    [Fact]
    public void TheInterfaceLanguageIsTheOneTheIdeIsDisplayedIn()
    {
        // The OS language starts as the culture, but only English ships: the
        // strings are English, so the tools must be too.
        Localizer.Culture = new CultureInfo("it-IT");

        Assert.Equal("en", Localizer.InterfaceLanguage);
        Assert.Equal("en", Language(DotnetProcess.CreateStartInfo()));
    }

    [Fact]
    public void ABuildStartsDotnetInTheInterfaceLanguage()
    {
        Localizer.Culture = new CultureInfo("it-IT");
        var service = new MsBuildBuildService();

        service.BuildAsync(MissingProject).GetAwaiter().GetResult();

        Assert.Equal("en", Language(service.LastStartInfo));
    }

    [Fact]
    public void ARestoreStartsDotnetInTheInterfaceLanguage()
    {
        Localizer.Culture = new CultureInfo("it-IT");
        var service = new MsBuildBuildService();

        service.RestoreAsync(MissingProject).GetAwaiter().GetResult();

        Assert.Equal("en", Language(service.LastStartInfo));
    }

    [Fact]
    public void ARunStartsDotnetInTheInterfaceLanguage()
    {
        Localizer.Culture = new CultureInfo("it-IT");
        using var service = new ProcessRunService();

        service.Start(MissingProject);
        service.Stop();

        Assert.Equal("en", Language(service.LastStartInfo));
    }

    [Fact]
    public void ATestRunStartsDotnetInTheInterfaceLanguage()
    {
        Localizer.Culture = new CultureInfo("it-IT");
        var service = new DotnetTestService(new Basalt.Workspace.BuildGate());

        // The project does not exist, so the listing fails and discovery says so; dotnet was still started.
        Assert.Throws<InvalidOperationException>(() => service.DiscoverAsync(MissingProject).GetAwaiter().GetResult());

        Assert.Equal("en", Language(service.LastStartInfo));
    }

    [Fact]
    public void ALanguageTheUserAlreadySetInTheEnvironmentIsKept()
    {
        Localizer.Culture = new CultureInfo("it-IT");
        Environment.SetEnvironmentVariable(DotnetProcess.LanguageVariable, "de");

        Assert.Equal("de", Language(DotnetProcess.CreateStartInfo()));

        var service = new MsBuildBuildService();
        service.BuildAsync(MissingProject).GetAwaiter().GetResult();
        Assert.Equal("de", Language(service.LastStartInfo));
    }

    [Fact]
    public void CleanAndRebuildStartDotnetInTheInterfaceLanguage()
    {
        Localizer.Culture = new CultureInfo("it-IT");
        using var viewModel = new Basalt.Shell.ViewModels.MainWindowViewModel { StartupProject = MissingProject };

        viewModel.CleanAsync().GetAwaiter().GetResult();
        Assert.Equal("en", Language(viewModel.LastTargetStartInfo));

        Environment.SetEnvironmentVariable(DotnetProcess.LanguageVariable, "fr");
        viewModel.CleanAsync().GetAwaiter().GetResult();
        Assert.Equal("fr", Language(viewModel.LastTargetStartInfo));
    }

    [Fact]
    public void TheParserStillReadsDiagnosticsWhoseTextIsInTheSystemLanguage()
    {
        // A project built before the language was set, or by a tool started
        // outside the IDE, can still print Italian: the keyword and code are
        // the stable part, and the summary lines must not become problems.
        const string output = """
             1>/p/Program.vb(4,27): error BC30451: 'y' non è dichiarato. [/p/App.vbproj]
            /p/Program.vb(9,1): warning BC42024: Variabile locale non usata: 'x'. [/p/App.vbproj]
            Compilazione NON riuscita.
                1 Avviso/i
                1 Errore/i
            Tempo trascorso 00:00:01.23
            """;

        var diagnostics = MsBuildBuildService.ParseDiagnostics(output);

        Assert.Equal(["BC30451", "BC42024"], diagnostics.Select(d => d.Id));
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostics[1].Severity);
    }
}
