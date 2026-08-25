using System.Globalization;
using Basalt.Core.Localization;

namespace Basalt.Tests;

/// <summary>
/// The resource file is the single source of user-visible text. These tests
/// guard the wiring: a resx that fails to embed returns the key itself, which
/// would surface in the UI as raw identifiers.
/// </summary>
public class LocalizationTests
{
    [Fact]
    public void ResolvesStringsFromTheResourceFile()
    {
        Assert.Equal("File", Localizer.Get(StringKeys.MenuFile));
        Assert.Equal("Solution Explorer", Localizer.Get(StringKeys.ToolSolutionExplorer));
        Assert.Equal("Build Solution", Localizer.Get(StringKeys.MenuBuildBuildSolution));
    }

    [Fact]
    public void FillsInPlaceholders()
    {
        Assert.Equal("Terminal 3", Localizer.Get(StringKeys.ToolTerminalNumbered, 3));
        Assert.Equal("Saved Program.cs", Localizer.Get(StringKeys.StatusSaved, "Program.cs"));
    }

    [Fact]
    public void EveryDeclaredKeyResolvesToRealText()
    {
        // A key that resolves to itself means the string is missing from the resx.
        var keys = typeof(StringKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(keys);

        var missing = keys.Where(k => Localizer.Get(k) == k).ToList();
        Assert.True(missing.Count == 0, $"Keys missing from Strings.resx: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EnglishIsAvailableAndListedFirst()
    {
        Assert.NotEmpty(Localizer.AvailableCultures);
        Assert.Equal("en", Localizer.AvailableCultures[0].TwoLetterISOLanguageName);
    }

    [Fact]
    public void FallsBackToEnglishForACultureWithoutTranslations()
    {
        var original = Localizer.Culture;
        try
        {
            // No satellite assembly exists for this culture yet.
            Localizer.Culture = new CultureInfo("it");
            Assert.Equal("File", Localizer.Get(StringKeys.MenuFile));
        }
        finally
        {
            Localizer.Culture = original;
        }
    }

    [Fact]
    public void SignalsCultureChangesSoOpenViewsCanRefresh()
    {
        var original = Localizer.Culture;
        var raised = 0;
        void Handler(object? sender, EventArgs e) => raised++;

        Localizer.CultureChanged += Handler;
        try
        {
            Localizer.Culture = new CultureInfo("fr");
            Assert.Equal(1, raised);

            // Setting the same culture again must not raise the event.
            Localizer.Culture = new CultureInfo("fr");
            Assert.Equal(1, raised);
        }
        finally
        {
            Localizer.CultureChanged -= Handler;
            Localizer.Culture = original;
        }
    }
}
