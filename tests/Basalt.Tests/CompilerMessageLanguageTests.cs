using System.Globalization;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The compiler's messages in Problems are in the language the IDE is shown in.
/// </summary>
/// <remarks>
/// On an Italian machine the English IDE listed "'y' non è dichiarato" for the
/// errors it found while typing, next to English build errors: the compiler
/// used the operating system's language, while the build already used the
/// IDE's (DOTNET_CLI_UI_LANGUAGE). Reintroduced once (GetMessage() with no
/// culture): the test failed with the Italian message.
/// </remarks>
[Collection("Localizer culture")]
public sealed class CompilerMessageLanguageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-message-language-").FullName;
    private readonly CultureInfo _culture = Localizer.Culture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        Localizer.Culture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task AnItalianMachineShowsTheEnglishIdesMessagesInEnglish()
    {
        File.WriteAllText(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var path = Path.Combine(_root, "Program.vb");
        File.WriteAllText(path, "Module M\n    Sub Main()\n        Dim x As Integer = y\n    End Sub\nEnd Module\n");

        // The machine is Italian; only English ships, so the IDE is shown in English.
        CultureInfo.CurrentUICulture = new CultureInfo("it-IT");
        Localizer.Culture = new CultureInfo("it-IT");
        Assert.Equal("en", Localizer.InterfaceLanguage);

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
        var error = Assert.Single(await service.GetDiagnosticsAsync(path), d => d.Id == "BC30451");

        Assert.Equal("'y' is not declared. It may be inaccessible due to its protection level.", error.Message);
    }
}
