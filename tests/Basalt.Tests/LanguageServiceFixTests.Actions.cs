using System.Globalization;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The quick actions a caret offers: the interface fix and the language of its titles.
/// </summary>
/// <remarks>
/// Run against a real solution, since the fixes are Roslyn's own providers
/// and what they offer depends on the bound code.
/// </remarks>
[Collection("Localizer culture")]
public sealed class LanguageServiceFixActionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-langfix-actions-").FullName;
    private readonly CultureInfo _culture = Localizer.Culture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        Localizer.Culture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<RoslynLanguageService> OpenAsync(string code)
    {
        File.WriteAllText(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(_root, "Program.vb"), code.ReplaceLineEndings("\n"));

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        return service;
    }

    private static async Task<IReadOnlyList<string>> TitlesAsync(RoslynLanguageService service, string path, int position) =>
        (await service.GetQuickActionsAsync(path, position)).Select(action => action.Title).ToList();

    [Fact]
    public async Task CodeFixOffersImplementInterfaceWhenTheCaretIsOnTheClassName()
    {
        // The class does not implement IShape. Visual Basic reports that on the
        // Implements line, while the caret is on the class name: the fix must
        // still be offered there, not only "Rename symbol".
        const string code = """
            Public Interface IShape
                ReadOnly Property Name As String
                Function Area() As Double
            End Interface

            Public Class Circle
                Implements IShape
            End Class

            Module Program
                Sub Main()
                End Sub
            End Module
            """;

        using var service = await OpenAsync(code);

        var titles = await TitlesAsync(
            service,
            Path.Combine(_root, "Program.vb"),
            code.IndexOf("Circle", StringComparison.Ordinal) + "Circle".Length);

        Assert.Contains(titles, title => title.Contains("Implement", StringComparison.Ordinal));
    }

    /// <summary>The titles offered at the call, with the operating system and the interface set to the given cultures.</summary>
    private async Task<IReadOnlyList<string>> TitlesWithCulturesAsync(string osCulture, string interfaceCulture)
    {
        const string code = """
            Module Program
                Sub Main()
                    Dim r = ComputeTotal(1, 2)
                End Sub
            End Module
            """;

        CultureInfo.CurrentUICulture = new CultureInfo(osCulture);
        Localizer.Culture = new CultureInfo(interfaceCulture);

        using var service = await OpenAsync(code);

        return await TitlesAsync(
            service,
            Path.Combine(_root, "Program.vb"),
            code.IndexOf("ComputeTotal", StringComparison.Ordinal) + "ComputeTotal".Length);
    }

    [Fact]
    public async Task CodeFixTitlesFollowTheInterfaceLanguageNotTheOperatingSystem()
    {
        // The machine is Italian. Whatever languages ship, the titles are those of the interface language, so the
        // titles under an Italian system must equal the titles under the interface language itself. This does not
        // assume which language that is: it is English today, and a translation would change both sides alike.
        Localizer.Culture = new CultureInfo("it-IT");
        var interfaceLanguage = Localizer.InterfaceLanguage;

        var underItalianSystem = await TitlesWithCulturesAsync("it-IT", "it-IT");
        var underInterfaceLanguage = await TitlesWithCulturesAsync(interfaceLanguage, interfaceLanguage);

        Assert.NotEmpty(underItalianSystem);
        Assert.Equal(underInterfaceLanguage, underItalianSystem);
    }
}
