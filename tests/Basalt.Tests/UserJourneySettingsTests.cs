using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Ai;

namespace Basalt.Tests;

/// <summary>
/// User journeys through a real <see cref="MainWindow"/> for the Settings
/// dialog, the AI assistant panel, the welcome screen's recent list and the
/// Output panel: the clicks a person would make, not calls into one method.
///
/// The assistant journeys replace the key store and the backend, so no
/// keychain is written and no model provider is called. Settings-touching
/// tests share <see cref="SettingsFileCollection"/> and point
/// BASALT_SETTINGS at a file of their own.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneySettingsTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-settings-journey-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-settings-journey-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    private readonly ThemeVariant? _previousVariant =
        Application.Current?.RequestedThemeVariant;

    public UserJourneySettingsTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        if (Application.Current is { } application && _previousVariant is { } variant)
            application.RequestedThemeVariant = variant;

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string L(string key) => Localizer.Get(key);

    /// <summary>A main window that closes itself, without the save prompt, when the test ends.</summary>
    private sealed class JourneyShell : IDisposable
    {
        public JourneyShell(string? solution = null, AiAssistantServices? assistant = null)
        {
            Window = new MainWindow(solution, assistant);
            Window.Show();
        }

        public MainWindow Window { get; }

        public MainWindowViewModel ViewModel => (MainWindowViewModel)Window.DataContext!;

        /// <summary>Several layout passes with short pauses, so panels built over more than one pass exist.</summary>
        public async Task SettleAsync(int passes = 5)
        {
            for (var i = 0; i < passes; i++)
            {
                Window.UpdateLayout();
                await Task.Delay(60);
            }

            Window.UpdateLayout();
        }

        public void Dispose()
        {
            Window.SkipSaveChangesPromptForTests = true;
            Window.Close();
        }
    }

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        if (hit!.Action is { } action) action();
        else hit.Command!.Execute(null);
    }

    /// <summary>The first window the main window owns, once it has opened.</summary>
    private static async Task<Window?> WaitForOwnedWindowAsync(JourneyShell shell)
    {
        Window? dialog = null;

        for (var i = 0; i < 80 && dialog is null; i++)
        {
            await shell.SettleAsync(1);
            dialog = shell.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    private static async Task<SettingsWindow> OpenSettingsAsync(JourneyShell shell)
    {
        InvokeMenu(shell.Window, L(StringKeys.MenuTools), L(StringKeys.MenuToolsSettings));

        var dialog = await WaitForOwnedWindowAsync(shell);
        return Assert.IsType<SettingsWindow>(dialog);
    }

    /// <summary>The editor of a labelled row on the page being shown: the control after its label.</summary>
    private static Control Field(SettingsWindow settings, string label)
    {
        var text = settings.CurrentPage!.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == label);

        var row = Assert.IsType<StackPanel>(text.Parent);

        return (Control)row.Children[row.Children.IndexOf(text) + 1];
    }

    private static void ShowCategory(SettingsWindow settings, string name)
    {
        var index = settings.Categories.ToList().IndexOf(name);

        Assert.True(index >= 0, "no settings page called " + name);
        settings.SelectCategoryForTests(index);
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonWithContent(Visual scope, string content) =>
        scope.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content is string text && text == content);

    private static async Task<bool> WaitUntilAsync(JourneyShell shell, Func<bool> condition, int attempts = 200)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition()) return true;

            await shell.SettleAsync(1);
        }

        return condition();
    }

    private void WriteSettings(Action<IdeSettings> change)
    {
        var store = new SettingsStore(_settings);
        var settings = store.Load();

        change(settings);
        store.Save(settings);
    }

    private static string WriteConsoleProject(string root, string projectName = "App")
    {
        var projectPath = Path.Combine(root, $"{projectName}.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        return projectPath;
    }

    // ------------------------------------------------------------------
    // Journey 1: Settings
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EverySettingsPageOpensFromTheToolsMenu()
    {
        using var shell = new JourneyShell();
        await shell.SettleAsync(5);

        var settings = await OpenSettingsAsync(shell);

        try
        {
            Assert.NotEmpty(settings.Categories);

            for (var index = 0; index < settings.Categories.Count; index++)
            {
                settings.SelectCategoryForTests(index);
                await shell.SettleAsync(1);

                var page = Assert.IsAssignableFrom<Control>(settings.CurrentPage);

                // A page that opened blank is a page the user cannot use.
                Assert.NotEmpty(page.GetVisualDescendants().OfType<Control>().Where(c => c is not ContentPresenter));
            }
        }
        finally
        {
            settings.Close();
        }
    }

    [AvaloniaFact]
    public async Task EditorAndAppearanceChangesReachTheOpenEditorAndTheSettingsFile()
    {
        var project = WriteConsoleProject(_root);
        var program = Path.Combine(_root, "Program.vb");

        using var shell = new JourneyShell(project);
        await shell.SettleAsync(10);

        await shell.ViewModel.OpenFileAsync(program);
        await shell.SettleAsync(5);

        var editor = Assert.IsType<CodeEditor>(shell.Window.CodeEditorForTests(program));
        var text = editor.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();

        var settings = await OpenSettingsAsync(shell);

        try
        {
            ShowCategory(settings, L(StringKeys.SettingsEditor));
            var fontSize = Assert.IsType<NumericUpDown>(Field(settings, "Font size"));
            fontSize.Value = 18;

            var indentation = Assert.IsType<NumericUpDown>(Field(settings, "Indentation size"));
            indentation.Value = 2;

            var formatOnSave = settings.CurrentPage!.GetVisualDescendants().OfType<CheckBox>()
                .First(c => c.Content is string text && text == "Format when saving");
            formatOnSave.IsChecked = false;

            await shell.SettleAsync(2);

            // Applied to the editor that is already open, not the next one.
            Assert.Equal(18, text.FontSize);
            Assert.Equal(2, text.Options.IndentationSize);
            Assert.False(shell.ViewModel.FormatOnSave);

            ShowCategory(settings, L(StringKeys.SettingsAppearance));
            var theme = Assert.IsType<ComboBox>(Field(settings, "Theme"));
            theme.SelectedItem = IdeThemes.DisplayName(AppTheme.Dark);

            await shell.SettleAsync(2);

            Assert.Equal(IdeThemes.VariantFor(AppTheme.Dark), Application.Current!.RequestedThemeVariant);
        }
        finally
        {
            settings.Close();
        }

        // Read back from disk the way the next launch would read it.
        var saved = new SettingsStore(_settings).Load();

        Assert.Equal(18, saved.Editor.FontSize);
        Assert.Equal(2, saved.Editor.IndentationSize);
        Assert.False(saved.Editor.FormatOnSave);
        Assert.Equal(AppTheme.Dark, saved.Appearance.Theme);
    }

    [AvaloniaFact]
    public async Task FormatOnSaveSwitchedOffInTheFileIsHonouredWhenTheWindowOpens()
    {
        WriteSettings(s => s.Editor.FormatOnSave = false);

        using var shell = new JourneyShell();
        await shell.SettleAsync(5);

        // A window opened on that file must not format on save, without the
        // user having to touch the Settings dialog first.
        Assert.False(shell.ViewModel.FormatOnSave);
    }

    // ------------------------------------------------------------------
    // Journey 2: AI assistant panel
    // ------------------------------------------------------------------

    /// <summary>An in-memory key store: nothing reaches the keychain or the key file.</summary>
    private sealed class MemoryKeyStore : IApiKeyStore
    {
        private readonly Dictionary<AiVendor, string> _keys = [];

        public bool UsesPlatformKeychain => false;

        /// <summary>How many times the key was read: the panel asks only when it needs to.</summary>
        public int Reads { get; private set; }

        public string? Get(AiVendor vendor)
        {
            Reads++;
            return _keys.GetValueOrDefault(vendor);
        }

        public bool Set(AiVendor vendor, string apiKey)
        {
            if (apiKey.Length == 0) return Remove(vendor);

            _keys[vendor] = apiKey;
            return true;
        }

        public bool Remove(AiVendor vendor) => _keys.Remove(vendor);

        public bool Has(AiVendor vendor)
        {
            Reads++;
            return _keys.ContainsKey(vendor);
        }
    }

    /// <summary>A backend that answers with the question it was asked, and records each turn.</summary>
    private sealed class EchoBackend : IAiChatBackend
    {
        public List<AiTurnRequest> Turns { get; } = [];

        public async IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Turns.Add(request);

            var asked = request.Messages.Last(m => m.Role == AiRole.User).Text;
            var reply = "Echo: " + asked;

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            yield return new AiTextDelta(reply);
            yield return new AiTurnEnd(new AiMessage(AiRole.Assistant, [new AiTextPart(reply)]), AiStopReason.EndTurn);
        }

        public void Dispose() { }
    }

    private const string FakeKey = "not-a-real-key";

    [AvaloniaFact]
    public async Task AssistantPanelSaysItIsNotConfiguredUntilAProviderAndKeyAreChosen()
    {
        var keys = new MemoryKeyStore();
        using var shell = new JourneyShell(null, new AiAssistantServices(keys, (_, _, _) => new EchoBackend()));
        await shell.SettleAsync(5);

        var panel = Assert.IsType<AiChatPanel>(shell.Window.AssistantForTests);

        Assert.Equal(L(StringKeys.AiNoAssistant), panel.StatusText);
        Assert.Contains("Settings", panel.StatusText);
    }

    [AvaloniaFact]
    public async Task ChangingAnUnrelatedSettingDoesNotAskTheKeyStoreButChangingTheVendorDoes()
    {
        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.Claude, FakeKey);
        WriteSettings(s => s.Ai.Vendor = AiVendor.Claude.ToString());

        using var shell = new JourneyShell(null, new AiAssistantServices(keys, (_, _, _) => new EchoBackend()));
        await shell.SettleAsync(5);

        var panel = shell.Window.AssistantForTests!;
        var readsBefore = keys.Reads;

        // A font change sends one settings push per spinner step; none of them
        // is about the assistant, so the key store is not asked.
        WriteSettings(s => s.Editor.FontSize = 20);
        shell.Window.ApplySettingsForTests(new SettingsStore(_settings).Load());
        shell.Window.ApplySettingsForTests(new SettingsStore(_settings).Load());
        await shell.SettleAsync(1);

        Assert.Equal(readsBefore, keys.Reads);

        // Changing the vendor does refresh what the panel says.
        var chatGpt = AiConversation.ProviderFor(AiVendor.ChatGpt);
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());
        shell.Window.ApplySettingsForTests(new SettingsStore(_settings).Load());
        await shell.SettleAsync(1);

        Assert.True(keys.Reads > readsBefore);
        Assert.Contains(chatGpt.DisplayName, panel.StatusText);
    }

    [AvaloniaFact]
    public async Task ChoosingAProviderAndKeyInSettingsThenAskingShowsTheReply()
    {
        var keys = new MemoryKeyStore();
        var backend = new EchoBackend();
        var factoryCalls = new List<(AiVendor Vendor, string Key, string Model)>();

        var assistant = new AiAssistantServices(keys, (provider, key, model) =>
        {
            factoryCalls.Add((provider.Vendor, key, model));
            return backend;
        });

        using var shell = new JourneyShell(null, assistant);
        await shell.SettleAsync(5);

        var panel = shell.Window.AssistantForTests!;
        var claude = AiConversation.ProviderFor(AiVendor.Claude);

        var settings = await OpenSettingsAsync(shell);

        try
        {
            ShowCategory(settings, L(StringKeys.SettingsAssistant));

            var chooser = Assert.IsType<ComboBox>(Field(settings, "Assistant"));
            chooser.SelectedItem = claude.DisplayName;

            // Picking a provider shows the key box for it on the same page,
            // without the user having to leave the page and come back.
            var keyBox = settings.CurrentPage!.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.PasswordChar != '\0');

            Assert.NotNull(keyBox);

            keyBox!.Text = FakeKey;
            Click(ButtonWithContent(settings.CurrentPage!, "Save key"));

            Assert.True(keys.Has(AiVendor.Claude));
            Assert.Equal(FakeKey, keys.Get(AiVendor.Claude));
        }
        finally
        {
            settings.Close();
        }

        // The panel reflects the choice at once: no longer "not configured".
        await shell.SettleAsync(2);
        Assert.NotEqual(L(StringKeys.AiNoAssistant), panel.StatusText);
        Assert.StartsWith(claude.DisplayName, panel.StatusText);

        panel.InputText = "What does this do?";
        Click(panel.SendButton);

        var answered = await WaitUntilAsync(shell, () =>
            !panel.IsReplying
            && panel.Entries.Any(e => !e.IsUser && e.Text.Contains("Echo: ", StringComparison.Ordinal)));

        Assert.True(answered, "the fake assistant never replied");

        var reply = panel.Entries.Last(e => !e.IsUser && e.Text.Length > 0);
        Assert.Contains("What does this do?", reply.Text);

        // The fake was the one used, with the key the user entered.
        var call = Assert.Single(factoryCalls);
        Assert.Equal(AiVendor.Claude, call.Vendor);
        Assert.Equal(FakeKey, call.Key);
        Assert.Single(backend.Turns);
    }

    // ------------------------------------------------------------------
    // Journey 3: Start page
    // ------------------------------------------------------------------

    private static IEnumerable<Button> RecentButtons(MainWindow window) =>
        window.FindControl<StackPanel>("WelcomeRecent")!.Children.OfType<Button>();

    private string MakeSolution(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return WriteConsoleProject(folder, name);
    }

    [AvaloniaFact]
    public async Task RecentSolutionIsListedAndOpensWhenClicked()
    {
        var project = MakeSolution("Recent");
        WriteSettings(s => s.Recent.Solutions.Add(project));

        using var shell = new JourneyShell();
        await shell.SettleAsync(5);

        var button = Assert.Single(RecentButtons(shell.Window));
        Assert.Equal(project, button.GetValue(ToolTip.TipProperty) as string);

        Click(button);

        var opened = await WaitUntilAsync(shell, () => shell.ViewModel.SolutionPath is not null);

        Assert.True(opened, "the recent solution did not open");
        Assert.True(PathIdentity.Same(project, shell.ViewModel.SolutionPath!));
    }

    [AvaloniaFact]
    public async Task RecentEntryWhoseFolderIsGoneIsNotListedAndOpensNothing()
    {
        var gone = Path.Combine(_root, "Moved", "Gone.vbproj");
        var present = MakeSolution("Present");

        WriteSettings(s =>
        {
            s.Recent.Solutions.Add(gone);
            s.Recent.Solutions.Add(present);
        });

        using var shell = new JourneyShell();
        await shell.SettleAsync(5);

        // Only the one that is still there is offered, and the list does not
        // break on the one that is not.
        var buttons = RecentButtons(shell.Window).ToList();
        Assert.Single(buttons);
        Assert.Equal(present, buttons[0].GetValue(ToolTip.TipProperty) as string);

        Assert.Null(shell.ViewModel.SolutionPath);
    }

    [AvaloniaFact]
    public async Task RecentEntryDeletedAfterTheWelcomeScreenWasShownDoesNotThrow()
    {
        var project = MakeSolution("Vanished");
        WriteSettings(s => s.Recent.Solutions.Add(project));

        using var shell = new JourneyShell();
        await shell.SettleAsync(5);

        var button = Assert.Single(RecentButtons(shell.Window));

        // Removed while the window is open: clicking it reports the failure
        // in the output and leaves the window as it was, rather than throwing.
        Directory.Delete(Path.GetDirectoryName(project)!, recursive: true);

        Click(button);
        await shell.SettleAsync(10);

        Assert.Null(shell.ViewModel.SolutionPath);

        // Taken off the list, so the next click is not a dead end either.
        Assert.Empty(RecentButtons(shell.Window));
        Assert.DoesNotContain(project, new SettingsStore(_settings).Load().Recent.Solutions);
    }

    [AvaloniaFact]
    public async Task NewSolutionLinkOpensTheNewSolutionDialog()
    {
        using var shell = new JourneyShell();
        await shell.SettleAsync(5);

        var link = shell.Window.FindControl<Button>("WelcomeNewProjectButton")!;
        Click(link);

        var dialog = await WaitForOwnedWindowAsync(shell);

        try
        {
            Assert.NotNull(dialog);
        }
        finally
        {
            dialog?.Close();
        }
    }

    // ------------------------------------------------------------------
    // Journey 4: Output panel
    // ------------------------------------------------------------------

    private static OutputPanel OutputOf(MainWindow window)
    {
        var contents = typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window) as IDictionary;

        return Assert.Single(contents!.Values.OfType<OutputPanel>());
    }

    /// <summary>View > Output, as a user opens the panel: a dock tab is only in the visual tree once shown.</summary>
    private static async Task ShowOutputAsync(JourneyShell shell)
    {
        InvokeMenu(shell.Window, L(StringKeys.MenuView), L(StringKeys.ToolOutput));
        await shell.SettleAsync(5);
    }

    private static Button ClearButton(OutputPanel panel) =>
        panel.GetVisualDescendants().OfType<Button>()
            .First(b => ToolTip.GetTip(b) as string == L(StringKeys.OutputClear));

    [AvaloniaFact]
    public async Task BuildOutputAppearsInTheOutputPanel()
    {
        var project = WriteConsoleProject(_root);

        using var shell = new JourneyShell(project);
        await shell.SettleAsync(10);
        await ShowOutputAsync(shell);

        var built = await shell.ViewModel.BuildProjectForTestsAsync();
        await shell.SettleAsync(5);

        var panel = OutputOf(shell.Window);

        Assert.NotEmpty(shell.ViewModel.BuildOutput);
        Assert.False(panel.IsShowingEmptyState);
        Assert.True(built, string.Join("\n", shell.ViewModel.BuildOutput.TakeLast(20)));
    }

    [AvaloniaFact]
    public async Task ClearEmptiesTheOutputPanel()
    {
        using var shell = new JourneyShell();
        await shell.SettleAsync(5);
        await ShowOutputAsync(shell);

        var panel = OutputOf(shell.Window);

        shell.ViewModel.WriteOutput("first line");
        shell.ViewModel.WriteOutput("second line");
        await shell.SettleAsync(3);

        Assert.False(panel.IsShowingEmptyState);

        Click(ClearButton(panel));
        await shell.SettleAsync(3);

        Assert.Empty(shell.ViewModel.BuildOutput);
        Assert.True(panel.IsShowingEmptyState);
    }

    [AvaloniaFact]
    public async Task TenThousandLinesFromTheBuildDoNotFreezeThePanel()
    {
        using var shell = new JourneyShell();
        await shell.SettleAsync(5);
        await ShowOutputAsync(shell);

        var panel = OutputOf(shell.Window);
        var model = shell.ViewModel;
        var clock = Stopwatch.StartNew();

        // The build reports from a worker thread; that is the path the lines take.
        // The view model is read on the interface thread only, so it is taken before.
        await Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
                model.WriteOutput($"line {i:D5}");
        });

        await WaitUntilAsync(shell, () => shell.ViewModel.BuildOutput.Count > 0 && shell.ViewModel.BuildOutput[^1] == "line 09999");
        await shell.SettleAsync(5);

        clock.Stop();

        // Generous: the point is that it finishes, not how fast a CI machine is.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"took {clock.Elapsed}");

        Assert.Equal("line 09999", shell.ViewModel.BuildOutput[^1]);

        var shown = panel.Lines.GetVisualDescendants().OfType<SelectableTextBlock>()
            .Select(t => t.Text)
            .ToList();

        Assert.Contains("line 09999", shown);
    }
}
