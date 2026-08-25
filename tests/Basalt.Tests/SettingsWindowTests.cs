using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Settings;
using Basalt.Extensibility;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>The settings window and its categories.</summary>
public sealed class SettingsWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-settings-ui", Guid.NewGuid().ToString("N"));

    private SettingsStore Store => new(Path.Combine(_root, "settings.json"));

    public SettingsWindowTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public void OffersTheBuiltInCategories()
    {
        var window = new SettingsWindow(Store, null);

        // Named rather than counted: a count breaks every time a page is
        // added, which says nothing about whether the right ones are there.
        Assert.Contains("Editor", window.Categories);
        Assert.Contains("Appearance", window.Categories);
        Assert.Contains("Assistant", window.Categories);
        Assert.Contains("Keyboard", window.Categories);
        Assert.Contains("Toolbar", window.Categories);
    }

    [AvaloniaFact]
    public void ShowsTheFirstCategoryToBeginWith()
    {
        var window = new SettingsWindow(Store, null);

        Assert.NotNull(window.CurrentPage);
    }

    [AvaloniaFact]
    public void ShowsAnotherCategoryWhenItIsPicked()
    {
        var window = new SettingsWindow(Store, null);

        var first = window.CurrentPage;
        window.SelectCategoryForTests(1);

        Assert.NotSame(first, window.CurrentPage);
    }

    [AvaloniaFact]
    public void GivesEveryRegisteredLanguageItsOwnPage()
    {
        // A language added later must appear here without this window
        // knowing about it.
        var registry = new LanguageRegistry();
        registry.Register(new StubLanguage("brainfuck", "Brainfuck", ".bf"));

        var window = new SettingsWindow(Store, registry);

        Assert.Contains("Brainfuck", window.Categories);
    }

    [AvaloniaFact]
    public void ShowsALanguagesOwnPage()
    {
        var registry = new LanguageRegistry();
        registry.Register(new StubLanguage("brainfuck", "Brainfuck", ".bf"));

        var window = new SettingsWindow(Store, registry);

        window.SelectCategoryForTests(window.Categories.Count - 1);

        Assert.NotNull(window.CurrentPage);
    }

    [AvaloniaFact]
    public void StartsFromWhatWasSavedBefore()
    {
        var store = Store;

        var saved = store.Load();
        saved.Editor.FontSize = 19;
        store.Save(saved);

        var window = new SettingsWindow(Store, null);

        Assert.Equal(19, window.Settings.Editor.FontSize);
    }

    [AvaloniaFact]
    public void SurvivesHavingNoLanguagesRegistered()
    {
        var window = new SettingsWindow(Store, new LanguageRegistry());

        // The built-in pages are all there with no language registered; what
        // matters is that the window works, not how many there happen to be.
        Assert.Contains("Editor", window.Categories);
        Assert.Contains("Keyboard", window.Categories);
        Assert.NotEmpty(window.Categories);
    }

    /// <summary>A language that does nothing, for checking registration.</summary>
    private sealed class StubLanguage : ILanguageProvider
    {
        public StubLanguage(string id, string name, string extension) =>
            Identity = new LanguageIdentity(id, name, [extension]);

        public LanguageIdentity Identity { get; }
        public ICompletionProvider? Completion => null;
        public IDiagnosticProvider? Diagnostics => null;
        public ISyntaxHighlightProvider? Highlighting => null;
        public INavigationProvider? Navigation => null;
        public IFormattingProvider? Formatting => null;
        public ICompilerBackend? Compiler => null;

        public Task OpenSolutionAsync(string path, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [AvaloniaFact]
    public void GroupsThePagesInATree()
    {
        var window = new SettingsWindow(Store, null);

        var pages = window.VisiblePagesForTests();

        Assert.Contains(pages, p => p.Group == "General" && p.Name.StartsWith("Editor"));
        Assert.Contains(pages, p => p.Group == "Tools" && p.Name.StartsWith("Debug"));

        // Every page sits under a heading rather than loose in a flat list.
        Assert.All(pages, p => Assert.False(string.IsNullOrWhiteSpace(p.Group)));
    }

    [AvaloniaFact]
    public void FiltersThePagesToWhatWasTyped()
    {
        var window = new SettingsWindow(Store, null);

        window.SearchForTests("debug");

        var pages = window.VisiblePagesForTests();

        Assert.Contains(pages, p => p.Name.StartsWith("Debug"));
        Assert.DoesNotContain(pages, p => p.Name.StartsWith("Editor"));
    }

    [AvaloniaFact]
    public void FindsAPageByWhatItIsAboutNotOnlyByItsName()
    {
        // "font" is not the name of any page, but it is what the editor page
        // is for, and it is what a user would type.
        var window = new SettingsWindow(Store, null);

        window.SearchForTests("font");

        Assert.Contains(window.VisiblePagesForTests(), p => p.Name.StartsWith("Editor"));
    }

    [AvaloniaFact]
    public void SaysSoWhenNothingMatches()
    {
        var window = new SettingsWindow(Store, null);

        window.SearchForTests("zzzznothing");

        Assert.Empty(window.VisiblePagesForTests());
        Assert.Contains("Nothing matches", window.StatusForTests, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ShowsEveryPageAgainWhenTheSearchIsCleared()
    {
        var window = new SettingsWindow(Store, null);

        window.SearchForTests("debug");
        window.SearchForTests("");

        Assert.Equal(window.Categories.Count, window.VisiblePagesForTests().Count);
    }

    [AvaloniaFact]
    public void MarksAPageWhoseSettingsDifferFromTheDefault()
    {
        var store = Store;
        var settings = store.Load();

        settings.Editor.FontSize = 22;
        store.Save(settings);

        var window = new SettingsWindow(store, null);

        var editor = window.VisiblePagesForTests().Single(p => p.Name.StartsWith("Editor"));

        Assert.Contains("•", editor.Name, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void LeavesAnUnchangedPageUnmarked()
    {
        var window = new SettingsWindow(Store, null);

        Assert.All(window.VisiblePagesForTests(),
            p => Assert.DoesNotContain("•", p.Name, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void KeepsWorkingWhenALanguageAddsAPage()
    {
        var registry = new LanguageRegistry();
        registry.Register(new StubLanguage("brainfuck", "Brainfuck", ".bf"));

        var window = new SettingsWindow(Store, registry);

        Assert.Contains(window.VisiblePagesForTests(),
            p => p.Group == "Languages" && p.Name == "Brainfuck");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
