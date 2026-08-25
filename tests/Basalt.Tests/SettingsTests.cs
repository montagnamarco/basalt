using Basalt.Core.Settings;

namespace Basalt.Tests;

/// <summary>Reading and writing what the IDE remembers between sessions.</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-settings", Guid.NewGuid().ToString("N"));

    private string SettingsFile => Path.Combine(_root, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void StartsFromTheDefaultsWhenNothingIsSaved()
    {
        var settings = new SettingsStore(SettingsFile).Load();

        Assert.Equal(13, settings.Editor.FontSize);
        Assert.True(settings.Editor.FormatWhileTyping);
        Assert.Equal(AppTheme.Light, settings.Appearance.Theme);
    }

    [Fact]
    public void KeepsWhatWasSaved()
    {
        var store = new SettingsStore(SettingsFile);

        var settings = store.Load();
        settings.Editor.FontSize = 16;
        settings.Appearance.Theme = AppTheme.Dark;

        Assert.True(store.Save(settings));

        var reloaded = new SettingsStore(SettingsFile).Load();

        Assert.Equal(16, reloaded.Editor.FontSize);
        Assert.Equal(AppTheme.Dark, reloaded.Appearance.Theme);
    }

    [Fact]
    public void CreatesTheFolderItNeeds()
    {
        var nested = Path.Combine(_root, "a", "b", "settings.json");

        Assert.True(new SettingsStore(nested).Save(new IdeSettings()));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void FallsBackToTheDefaultsOnAFileItCannotRead()
    {
        // Losing settings must not cost the user their editor.
        File.WriteAllText(SettingsFile, "{ this is not json");

        var settings = new SettingsStore(SettingsFile).Load();

        Assert.Equal(13, settings.Editor.FontSize);
    }

    [Fact]
    public void WritesEnumsByNameSoTheFileCanBeRead()
    {
        var store = new SettingsStore(SettingsFile);

        var settings = store.Load();
        settings.Appearance.Theme = AppTheme.Dark;
        store.Save(settings);

        Assert.Contains("\"Dark\"", File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void ReportsWhenSettingsChange()
    {
        var store = new SettingsStore(SettingsFile);

        var raised = 0;
        store.Changed += (_, _) => raised++;

        var settings = store.Load();
        store.Save(settings);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void KeepsSettingsContributedByALanguage()
    {
        // Loose values, so adding a language needs no change to the model.
        var store = new SettingsStore(SettingsFile);

        var settings = store.Load();
        settings.SetLanguageSetting("vb", "OptionStrict", "On");
        store.Save(settings);

        var reloaded = new SettingsStore(SettingsFile).Load();

        Assert.Equal("On", reloaded.GetLanguageSetting("vb", "OptionStrict"));
    }

    [Fact]
    public void GivesTheFallbackForALanguageSettingThatIsNotSet()
    {
        var settings = new IdeSettings();

        Assert.Equal("Off", settings.GetLanguageSetting("vb", "OptionStrict", "Off"));
        Assert.Equal("", settings.GetLanguageSetting("nothing", "at-all"));
    }

    [Fact]
    public void KeepsSettingsOfDifferentLanguagesApart()
    {
        var settings = new IdeSettings();

        settings.SetLanguageSetting("vb", "Indent", "4");
        settings.SetLanguageSetting("css", "Indent", "2");

        Assert.Equal("4", settings.GetLanguageSetting("vb", "Indent"));
        Assert.Equal("2", settings.GetLanguageSetting("css", "Indent"));
    }

    [Fact]
    public void PutsTheSettingsFileInTheUsersOwnFolder()
    {
        // Without the override, which the tests set so they do not write to
        // the settings of whoever runs them.
        var was = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

        Environment.SetEnvironmentVariable("BASALT_SETTINGS", null);

        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            Assert.StartsWith(home, SettingsStore.DefaultPath);
            Assert.EndsWith("settings.json", SettingsStore.DefaultPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BASALT_SETTINGS", was);
        }
    }

    [Fact]
    public void TheOverrideKeepsTheTestsOutOfTheRealSettings()
    {
        // The reason the override exists: a test that opens a window writes
        // to the settings, and the suite was filling a developer's own list
        // of recent solutions.
        var chosen = Path.Combine(_root, "elsewhere.json");

        var was = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

        Environment.SetEnvironmentVariable("BASALT_SETTINGS", chosen);

        try
        {
            Assert.Equal(chosen, SettingsStore.DefaultPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BASALT_SETTINGS", was);
        }
    }

    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        // A directory where the file should be: writing cannot succeed.
        var blocked = Path.Combine(_root, "blocked.json");
        Directory.CreateDirectory(blocked);

        Assert.False(new SettingsStore(blocked).Save(new IdeSettings()));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
