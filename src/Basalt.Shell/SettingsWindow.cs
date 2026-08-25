using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using Basalt.Core.Localization;
using Basalt.Core.Commands;
using Basalt.Shell.Controls;
using Basalt.Core.Settings;
using Basalt.Workspace.Ai;
using Basalt.Core.Services;
using Basalt.Extensibility;

namespace Basalt.Shell;

/// <summary>
/// The settings window.
///
/// Built in code rather than XAML because the per-language pages are not known
/// until the languages have registered: a markup file would have to be edited
/// every time one was added, which is exactly what the extensible architecture
/// exists to avoid.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly IdeSettings _settings;
    private readonly LanguageRegistry? _languages;

    /// <summary>Where the assistants' API keys are kept.</summary>
    private readonly ApiKeyStore _keys = new();

    /// <summary>
    /// The commands and their shortcuts.
    ///
    /// Rebuilt when the scheme changes, so the page shows the defaults the
    /// chosen scheme brings.
    /// </summary>
    private CommandRegistry? _commands;

    private readonly ContentControl _pageHost;
    private readonly TreeView _categories;
    private readonly TextBox _search;
    private readonly TextBlock _status;

    /// <summary>Every page the window can show.</summary>
    private readonly List<SettingsPage> _pages;

    /// <summary>The page being shown, so a rebuild can keep it selected.</summary>
    private SettingsPage? _current;

    /// <summary>
    /// Raised whenever a setting changes.
    ///
    /// Every change is applied as it is made rather than on closing: a font
    /// size the user cannot see until they close the window is a font size
    /// they have to guess at.
    /// </summary>
    public event EventHandler<IdeSettings>? SettingsChanged;

    public SettingsWindow() : this(new SettingsStore(), null) { }

    public SettingsWindow(SettingsStore store, LanguageRegistry? languages)
    {
        _store = store;
        _languages = languages;
        _settings = store.Load();

        Title = Localizer.Get(StringKeys.SettingsTitle);
        Width = 820;
        Height = 600;

        // Small enough to fit a laptop screen, large enough that the tree and
        // a page still both fit.
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _status = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };

        _pageHost = new ContentControl { Margin = new Thickness(20, 16, 20, 16) };

        _pages = BuildPages();

        _search = new TextBox
        {
            PlaceholderText = "Search settings",
            Margin = new Thickness(0, 0, 0, 8)
        };

        _search.TextChanged += (_, _) => FillTree(_search.Text);

        _categories = new TreeView();

        _categories.SelectionChanged += (_, _) =>
        {
            if (_categories.SelectedItem is TreeViewItem { Tag: SettingsPage page })
                ShowPage(page);
        };

        Content = BuildLayout();

        FillTree(null);
        ShowPage(_pages[0]);

        // Focused when the window opens: searching is how a settings
        // window with this many pages is actually used.
        Opened += (_, _) => _search.Focus();
    }

    /// <summary>The settings being edited, exposed for tests.</summary>
    internal IdeSettings Settings => _settings;

    internal IReadOnlyList<string> Categories => CategoryNames();

    internal void SelectCategoryForTests(int index) => ShowPage(_pages[index]);

    /// <summary>Filters the tree as typing in the search box would.</summary>
    internal void SearchForTests(string? term) => FillTree(term);

    /// <summary>The pages the tree is showing, by group, for tests.</summary>
    internal IReadOnlyList<(string Group, string Name)> VisiblePagesForTests() =>
    [
        .. (_categories.ItemsSource ?? Array.Empty<object>())
            .OfType<TreeViewItem>()
            .SelectMany(group => (group.ItemsSource ?? Array.Empty<object>())
                .OfType<TreeViewItem>()
                .Select(page => (
                    Group: group.Header?.ToString() ?? "",
                    Name: page.Header?.ToString() ?? "")))
    ];

    /// <summary>What the footer is saying, for tests.</summary>
    internal string StatusForTests => _status.Text ?? "";

    internal Control? CurrentPage => _pageHost.Content as Control;

    /// <summary>
    /// One page of settings, under the group it belongs to.
    ///
    /// The page carries its own builder rather than being found by index: a
    /// tree that filters as you type has no stable index to look up.
    /// </summary>
    /// <param name="Group">The heading it sits under.</param>
    /// <param name="Name">The name shown in the tree.</param>
    /// <param name="Build">Makes the page when it is chosen.</param>
    /// <param name="Keywords">
    /// What the page is about, for the search box: a user looking for "font"
    /// should find the Editor page even though the word is not its name.
    /// </param>
    private sealed record SettingsPage(
        string Group,
        string Name,
        Func<Control> Build,
        string Keywords = "",
        string? SettingsGroup = null);

    /// <summary>Every page, in the order the tree shows them.</summary>
    private List<SettingsPage> BuildPages()
    {
        var pages = new List<SettingsPage>
        {
            new("General", Localizer.Get(StringKeys.SettingsEditor), EditorPage,
                "font size tabs indentation wrap whitespace brackets completion format",
                nameof(IdeSettings.Editor)),
            new("General", Localizer.Get(StringKeys.SettingsAppearance), AppearancePage,
                "theme dark light colour color density language solarized contrast",
                nameof(IdeSettings.Appearance)),
            new("General", "Keyboard", KeyboardPage,
                "shortcut key binding gesture scheme visual studio code",
                nameof(IdeSettings.Keyboard)),
            new("General", "Toolbar", ToolbarPage,
                "button toolbar order icons build run debug",
                nameof(IdeSettings.Toolbar)),
            new("Tools", Localizer.Get(StringKeys.SettingsTerminal), TerminalPage,
                "shell profile command prompt",
                nameof(IdeSettings.Terminal)),
            new("Tools", Localizer.Get(StringKeys.SettingsDebug), DebugPage,
                "breakpoint step debugger console",
                nameof(IdeSettings.Debug)),
            new("Tools", Localizer.Get(StringKeys.SettingsAssistant), AssistantPage,
                "ai model api key anthropic openai vendor chat",
                nameof(IdeSettings.Ai))
        };

        // Languages contribute their own pages, so a language added later
        // appears here without this class knowing about it.
        if (_languages is not null)
        {
            for (var i = 0; i < _languages.Providers.Count; i++)
            {
                var index = i;
                var identity = _languages.Providers[i].Identity;

                pages.Add(new SettingsPage(
                    "Languages", identity.DisplayName,
                    () => LanguagePage(index),
                    identity.DisplayName));
            }
        }

        return pages;
    }

    private IReadOnlyList<string> CategoryNames() => [.. _pages.Select(p => p.Name)];

    private Control BuildLayout()
    {
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var close = new Button { Content = Localizer.Get(StringKeys.DialogClose), IsDefault = true };
        close.Click += (_, _) => Close();

        Grid.SetColumn(_status, 0);
        Grid.SetColumn(close, 1);

        footer.Children.Add(_status);
        footer.Children.Add(close);

        var footerBar = new Border
        {
            Padding = new Thickness(20, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footer
        };

        // The tree column is resizable, and the window with it: a shortcut
        // list or a long language name does not fit a fixed 190 pixels.
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,Auto,*")
        };

        var sidebar = new DockPanel { Margin = new Thickness(12, 12, 6, 12) };
        DockPanel.SetDock(_search, Avalonia.Controls.Dock.Top);
        sidebar.Children.Add(_search);
        sidebar.Children.Add(_categories);

        var splitter = new GridSplitter { Width = 4, Background = Brushes.Transparent };

        // The page scrolls, so a long list of settings stays reachable in a
        // small window.
        var scroller = new ScrollViewer { Content = _pageHost };

        Grid.SetColumn(sidebar, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(scroller, 2);

        body.Children.Add(sidebar);
        body.Children.Add(splitter);
        body.Children.Add(scroller);

        var layout = new DockPanel();
        DockPanel.SetDock(footerBar, Avalonia.Controls.Dock.Bottom);
        layout.Children.Add(footerBar);
        layout.Children.Add(body);

        return layout;
    }

    private void ShowPage(SettingsPage page)
    {
        _current = page;
        _pageHost.Content = WithResetHeader(page);
    }

    /// <summary>Whether a page holds anything changed from the default.</summary>
    private bool IsChanged(SettingsPage page) =>
        page.SettingsGroup is { } group
        && SettingsComparison.HasChangesIn(_settings, group);

    /// <summary>
    /// A page, with a line above it saying what has been changed.
    ///
    /// Only when something has: a reset button on a page holding nothing but
    /// defaults is a button that does nothing.
    /// </summary>
    private Control WithResetHeader(SettingsPage page)
    {
        var content = page.Build();

        if (page.SettingsGroup is not { } group) return content;

        var changed = SettingsComparison.ChangedIn(_settings, group);

        if (changed.Count == 0) return content;

        var summary = new TextBlock
        {
            Text = changed.Count == 1
                ? "1 setting differs from the default"
                : $"{changed.Count} settings differ from the default",
            FontSize = 12,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };

        var reset = new Button
        {
            Content = "Reset this page",
            HorizontalAlignment = HorizontalAlignment.Right
        };

        reset.Click += (_, _) =>
        {
            SettingsComparison.ResetGroup(_settings, group);

            Apply(() => { });

            // Rebuilt so the controls show the values they were put back to,
            // and the tree drops the mark.
            FillTree(_search.Text);
            ShowPage(page);
        };

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        Grid.SetColumn(summary, 0);
        Grid.SetColumn(reset, 1);

        bar.Children.Add(summary);
        bar.Children.Add(reset);

        var header = new Border
        {
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Gray, 0.12),
            Child = bar
        };

        return new StackPanel { Children = { header, content } };
    }

    /// <summary>
    /// Fills the tree, keeping only what matches the search.
    ///
    /// A group with nothing left in it is dropped rather than shown empty,
    /// and a search that matches nothing says so instead of showing a blank
    /// panel that reads as a broken window.
    /// </summary>
    private void FillTree(string? search)
    {
        var term = search?.Trim() ?? "";

        var matching = term.Length == 0
            ? _pages
            : [.. _pages.Where(p => Matches(p, term))];

        var items = new List<TreeViewItem>();

        foreach (var group in matching.GroupBy(p => p.Group))
        {
            var node = new TreeViewItem
            {
                Header = group.Key,
                IsExpanded = true,
                ItemsSource = group.Select(page => new TreeViewItem
                {
                    // A dot marks a page holding something changed from the
                    // default, so it can be found without opening each one.
                    Header = IsChanged(page) ? $"{page.Name}  •" : page.Name,
                    Tag = page
                }).ToList()
            };

            items.Add(node);
        }

        _categories.ItemsSource = items;

        _status.Text = matching.Count == 0 && term.Length > 0
            ? $"Nothing matches '{term}'."
            : "";
    }

    /// <summary>Whether a page answers to what was typed.</summary>
    private static bool Matches(SettingsPage page, string term) =>
        page.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || page.Group.Contains(term, StringComparison.OrdinalIgnoreCase)
        || page.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase);

    // Pages

    private Control EditorPage() => Page(
        Text("Font family", _settings.Editor.FontFamily,
             v => Apply(() => _settings.Editor.FontFamily = v)),

        Number("Font size", _settings.Editor.FontSize, 6, 48,
               v => Apply(() => _settings.Editor.FontSize = v)),

        Number("Indentation size", _settings.Editor.IndentationSize, 1, 16,
               v => Apply(() => _settings.Editor.IndentationSize = (int)v)),

        Switch("Insert spaces instead of tabs", _settings.Editor.ConvertTabsToSpaces,
               v => Apply(() => _settings.Editor.ConvertTabsToSpaces = v)),

        Switch("Show line numbers", _settings.Editor.ShowLineNumbers,
               v => Apply(() => _settings.Editor.ShowLineNumbers = v)),

        Switch("Wrap long lines", _settings.Editor.WordWrap,
               v => Apply(() => _settings.Editor.WordWrap = v)),

        Switch("Apply conventions while typing", _settings.Editor.FormatWhileTyping,
               v => Apply(() => _settings.Editor.FormatWhileTyping = v),
               "Corrects keyword casing, spacing and indentation as you write, "
               + "as Visual Basic does on Windows."),

        Switch("Format when saving", _settings.Editor.FormatOnSave,
               v => Apply(() => _settings.Editor.FormatOnSave = v)),

        Switch("Close brackets and quotes", _settings.Editor.AutoCloseBrackets,
               v => Apply(() => _settings.Editor.AutoCloseBrackets = v)),

        Switch("Suggest completions automatically", _settings.Editor.CompleteAutomatically,
               v => Apply(() => _settings.Editor.CompleteAutomatically = v),
               "When off, completion appears only on Ctrl+Space."));

    /// <summary>
    /// Which buttons the toolbar shows.
    ///
    /// The candidates are the commands that have an icon on the toolbar,
    /// named by their registry id so the choice survives a button being
    /// retitled or moved.
    /// </summary>
    private Control ToolbarPage()
    {
        var chosen = _settings.Toolbar.Buttons;

        var rows = new StackPanel { Spacing = 2 };

        // In the order they are shown in, so the list reads as the toolbar
        // does; the ones not chosen follow.
        var ordered = chosen.Count == 0
            ? ToolbarCandidates()
            : [.. ToolbarCandidates()
                .OrderBy(c => chosen.IndexOf(c.Id) is var at && at >= 0 ? at : int.MaxValue)];

        string? lastGroup = null;

        foreach (var (id, title) in ordered)
        {
            var group = ToolbarGroupOf(id);

            if (group is not null && group != lastGroup)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = group,
                    FontSize = 11,
                    Opacity = 0.7,
                    Margin = new Thickness(0, rows.Children.Count == 0 ? 0 : 8, 0, 2)
                });

                lastGroup = group;
            }

            var captured = id;

            var box = new CheckBox
            {
                Content = title,
                // Nothing chosen means the toolbar shows its defaults, so
                // every candidate is ticked rather than none.
                IsChecked = chosen.Count == 0 || chosen.Contains(id),
                VerticalAlignment = VerticalAlignment.Center
            };

            box.IsCheckedChanged += (_, _) => Apply(() =>
            {
                var list = EnsureExplicitToolbarList();

                if (box.IsChecked == true)
                {
                    if (!list.Contains(captured)) list.Add(captured);
                }
                else
                {
                    list.Remove(captured);
                }
            });

            var up = new Button { Content = "↑", Padding = new Thickness(6, 0), Tag = captured };
            var down = new Button { Content = "↓", Padding = new Thickness(6, 0), Tag = captured };

            up.Click += (_, _) => MoveToolbarButton(captured, -1);
            down.Click += (_, _) => MoveToolbarButton(captured, 1);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

            Grid.SetColumn(box, 0);
            Grid.SetColumn(up, 1);
            Grid.SetColumn(down, 2);

            row.Children.Add(box);
            row.Children.Add(up);
            row.Children.Add(down);

            rows.Children.Add(row);
        }

        return Page(
            Hint("The toolbar shows these buttons, in this order."),
            new ScrollViewer { Content = rows, Height = 280 },
            Switch("Show the build configuration and startup project",
                   _settings.Toolbar.ShowChoosers,
                   v => Apply(() => _settings.Toolbar.ShowChoosers = v),
                   "The two lists at the end of the toolbar."));

        // Putting the buttons back is the page's reset, which the header
        // above the page already offers once anything here has changed.
    }

    /// <summary>
    /// The chosen list, written out in full if it was still empty.
    ///
    /// An empty list means the defaults, so the first change has to say what
    /// those were: otherwise removing one button would read as choosing only
    /// the others.
    /// </summary>
    private List<string> EnsureExplicitToolbarList()
    {
        var list = _settings.Toolbar.Buttons;

        if (list.Count == 0) list.AddRange(ToolbarCandidates().Select(c => c.Id));

        return list;
    }

    /// <summary>
    /// Moves a button one place along the toolbar.
    ///
    /// The page is rebuilt afterwards so the list reads in the new order,
    /// which is the whole point of having moved it.
    /// </summary>
    private void MoveToolbarButton(string id, int by)
    {
        Apply(() =>
        {
            var list = EnsureExplicitToolbarList();

            var at = list.IndexOf(id);

            if (at < 0) return;

            var to = at + by;

            if (to < 0 || to >= list.Count) return;

            (list[at], list[to]) = (list[to], list[at]);
        });

        if (_current is { } page) ShowPage(page);
    }

    /// <summary>
    /// The commands that can sit on the toolbar.
    ///
    /// Named here rather than read from the window, because the settings
    /// window is opened on its own in tests and has no toolbar to ask.
    /// </summary>
    private static IReadOnlyList<(string Id, string Title)> ToolbarCandidates() =>
        [.. ToolbarGroups.SelectMany(g => g.Buttons.Select(b => (b.Id, b.Title)))];

    /// <summary>
    /// The toolbar's buttons, under the group each belongs to.
    ///
    /// The groups are the kinds of work rather than an arbitrary split, and
    /// they are what the separators on the toolbar stand for: a line appears
    /// where the group changes.
    /// </summary>
    internal static IReadOnlyList<(string Name, IReadOnlyList<(string Id, string Title)> Buttons)>
        ToolbarGroups { get; } =
    [
        ("File", [
            (IdeCommands.FileNewSolution, "New Solution"),
            (IdeCommands.FileOpenSolution, "Open Solution"),
            (IdeCommands.FileSave, "Save")
        ]),
        ("Edit", [
            (IdeCommands.EditUndo, "Undo"),
            (IdeCommands.EditRedo, "Redo"),
            (IdeCommands.EditFind, "Find")
        ]),
        ("Navigate", [
            (IdeCommands.NavigateGoToFile, "Go to File"),
            (IdeCommands.NavigateBack, "Navigate Back"),
            (IdeCommands.NavigateForward, "Navigate Forward")
        ]),
        ("Build", [
            (IdeCommands.BuildSolution, "Build Solution"),
            (IdeCommands.DebugStartWithout, "Run without Debugging"),
            (IdeCommands.DebugStop, "Stop")
        ]),
        ("Debug", [
            (IdeCommands.DebugStart, "Start Debugging"),
            (IdeCommands.DebugStepOver, "Step Over"),
            (IdeCommands.DebugStepInto, "Step Into"),
            (IdeCommands.DebugStepOut, "Step Out")
        ]),
        ("Test", [
            (IdeCommands.TestRunAll, "Run All Tests")
        ]),
        ("Git", [
            (IdeCommands.GitCommit, "Commit")
        ]),
        ("Tools", [
            (IdeCommands.ViewTerminal, "Terminal"),
            (IdeCommands.ToolsSettings, "Settings")
        ])
    ];

    /// <summary>The group a button belongs to, or null when it is not one.</summary>
    internal static string? ToolbarGroupOf(string id) =>
        ToolbarGroups
            .FirstOrDefault(g => g.Buttons.Any(b => b.Id == id))
            .Name;

    /// <summary>Moves a toolbar button, for tests.</summary>
    internal void MoveToolbarButtonForTests(string id, int by) => MoveToolbarButton(id, by);

    /// <summary>The toolbar buttons that can be chosen, for tests.</summary>
    internal static IReadOnlyList<(string Id, string Title)> ToolbarCandidatesForTests() =>
        ToolbarCandidates();

    private Control AppearancePage() => Page(
        // Shown by name rather than by enum value, so "High Contrast Dark"
        // does not appear as "HighContrastDark".
        Choice("Theme",
               [.. Enum.GetValues<AppTheme>().Select(IdeThemes.DisplayName)],
               IdeThemes.DisplayName(_settings.Appearance.Theme),
               v => Apply(() => _settings.Appearance.Theme = ThemeNamed(v))),

        Choice("Density", Enum.GetNames<InterfaceDensity>(),
               _settings.Appearance.Density.ToString(),
               v => Apply(() => _settings.Appearance.Density = Enum.Parse<InterfaceDensity>(v))),

        Choice("Language", Localizer.AvailableLanguages, _settings.Appearance.Language,
               v => Apply(() => _settings.Appearance.Language = v),
               "Only English is translated so far."));

    private Control TerminalPage() => Page(
        Text("Shell", _settings.Terminal.Shell,
             v => Apply(() => _settings.Terminal.Shell = v),
             "Empty uses your login shell."),

        Number("Font size", _settings.Terminal.FontSize, 6, 32,
               v => Apply(() => _settings.Terminal.FontSize = v)),

        Number("Scrollback lines", _settings.Terminal.ScrollbackLines, 100, 100000,
               v => Apply(() => _settings.Terminal.ScrollbackLines = (int)v)));

    private Control DebugPage() => Page(
        Text("Debugger path", _settings.Debug.AdapterPath,
             v => Apply(() => _settings.Debug.AdapterPath = v),
             "Empty looks for netcoredbg beside the IDE and on the PATH."),

        Switch("Stop as soon as the program starts", _settings.Debug.StopAtEntry,
               v => Apply(() => _settings.Debug.StopAtEntry = v)),

        Switch("Step into code without debug symbols", _settings.Debug.StepIntoExternalCode,
               v => Apply(() => _settings.Debug.StepIntoExternalCode = v)));

    /// <summary>
    /// The shortcuts, and how to change them.
    ///
    /// Searchable because there are sixty of them: an unsearchable list of
    /// sixty is one nobody reads to the end.
    /// </summary>
    private Control KeyboardPage()
    {
        var rows = new StackPanel { Spacing = 2 };

        var search = new TextBox
        {
            PlaceholderText = "Search commands…",
            Margin = new Thickness(0, 0, 0, 8)
        };

        void Fill()
        {
            rows.Children.Clear();

            var current = _commands ?? IdeCommands.CreateRegistry(CurrentScheme);

            foreach (var command in current.Search(search.Text ?? ""))
                rows.Children.Add(ShortcutRow(current, command));
        }

        search.TextChanged += (_, _) => Fill();

        Fill();

        var scheme = Choice(
            "Scheme",
            Enum.GetNames<KeyboardScheme>(),
            _settings.Keyboard.Scheme,
            v => Apply(() =>
            {
                _settings.Keyboard.Scheme = v;

                // The defaults change with the scheme, so the registry is
                // rebuilt; what the user assigned by hand is kept.
                var rebuilt = IdeCommands.CreateRegistry(Enum.Parse<KeyboardScheme>(v));
                rebuilt.ApplyCustomisations(Customisations());

                _commands = rebuilt;
                Fill();
            }),
            "The shortcuts a command comes with. Anything you change yourself is kept.");

        var resetAll = new Button { Content = "Reset all shortcuts", FontSize = 11 };

        resetAll.Click += (_, _) => Apply(() =>
        {
            _commands?.ResetAll();
            _settings.Keyboard.Shortcuts.Clear();
            Fill();
        });

        return Page(
            scheme,
            resetAll,
            search,
            new ScrollViewer { Content = rows, Height = 320 });
    }

    /// <summary>One command, its shortcut, and what to do about it.</summary>
    private Control ShortcutRow(CommandRegistry registry, IdeCommand command)
    {
        var recorder = new ShortcutRecorder
        {
            Gesture = registry.GestureFor(command.Id) ?? "",
            Width = 160
        };

        var conflict = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.OrangeRed,
            VerticalAlignment = VerticalAlignment.Center
        };

        recorder.Recorded += (_, gesture) =>
        {
            // A shortcut already taken is not refused — the user may well mean
            // it — but they are told what it will displace.
            var taken = registry.Conflicts(gesture, command.Id);

            conflict.Text = taken.Count == 0
                ? ""
                : $"also {string.Join(", ", taken.Select(c => c.Title))}";

            Apply(() =>
            {
                registry.Assign(command.Id, gesture);
                _settings.Keyboard.Shortcuts[command.Id] = gesture;
            });
        };

        var reset = new Button
        {
            Content = "↺",
            FontSize = 11,
            IsEnabled = registry.IsCustomised(command.Id)
        };

        reset.Click += (_, _) => Apply(() =>
        {
            registry.ResetToDefault(command.Id);
            _settings.Keyboard.Shortcuts.Remove(command.Id);

            recorder.Gesture = registry.GestureFor(command.Id) ?? "";
            conflict.Text = "";
            reset.IsEnabled = false;
        });

        var name = new StackPanel { Spacing = 1, Width = 260 };

        name.Children.Add(new TextBlock { Text = command.Title, FontSize = 12 });
        name.Children.Add(new TextBlock
        {
            Text = command.Category.ToString(),
            FontSize = 10,
            Opacity = 0.6
        });

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { name, recorder, reset, conflict }
        };
    }

    /// <summary>The scheme currently chosen, or the default when unreadable.</summary>
    private KeyboardScheme CurrentScheme =>
        Enum.TryParse<KeyboardScheme>(_settings.Keyboard.Scheme, out var scheme)
            ? scheme
            : KeyboardScheme.Basalt;

    /// <summary>What the user has assigned, as the registry wants it.</summary>
    private Dictionary<string, string?> Customisations() =>
        _settings.Keyboard.Shortcuts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Length == 0 ? null : pair.Value,
            StringComparer.Ordinal);

    /// <summary>
    /// Choosing an assistant and giving it a key.
    ///
    /// The key box shows nothing of a stored key: displaying it would put a
    /// credential on screen for anyone passing to read.
    /// </summary>
    private Control AssistantPage()
    {
        var vendors = AiConversation.Providers.Select(p => p.DisplayName).ToList();

        var chosen = AiConversation.Providers
            .FirstOrDefault(p => p.Vendor.ToString() == _settings.Ai.Vendor);

        var rows = new List<Control>
        {
            Choice("Assistant",
                   ["(none)", .. vendors],
                   chosen?.DisplayName ?? "(none)",
                   v => Apply(() =>
                   {
                       var provider = AiConversation.Providers
                           .FirstOrDefault(p => p.DisplayName == v);

                       _settings.Ai.Vendor = provider?.Vendor.ToString() ?? "";
                       _settings.Ai.Model = provider?.Models[0].Id ?? "";
                   }),
                   "Restart the window after changing this to see the models.")
        };

        if (chosen is not null)
        {
            rows.Add(Choice("Model",
                [.. chosen.Models.Select(m => m.DisplayName)],
                chosen.Models.FirstOrDefault(m => m.Id == _settings.Ai.Model)?.DisplayName
                    ?? chosen.Models[0].DisplayName,
                v => Apply(() =>
                    _settings.Ai.Model = chosen.Models.First(m => m.DisplayName == v).Id)));

            rows.Add(ApiKeyRow(chosen));
        }

        rows.Add(Switch("Send the open file with the question",
            _settings.Ai.SendFileContext,
            v => Apply(() => _settings.Ai.SendFileContext = v),
            "When code is selected, only the selection is sent."));

        rows.Add(Switch("Send the current errors with the question",
            _settings.Ai.SendErrors,
            v => Apply(() => _settings.Ai.SendErrors = v)));

        return Page([.. rows]);
    }

    /// <summary>The API key box, which never shows what is already stored.</summary>
    private Control ApiKeyRow(IAiProvider provider)
    {
        var stored = _keys.Has(provider.Vendor);

        var box = new TextBox
        {
            PasswordChar = '•',
            PlaceholderText = stored ? "A key is stored. Type to replace it." : "Paste the API key"
        };

        var status = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.65,
            Text = stored
                ? $"Stored {(_keys.UsesPlatformKeychain ? "in the system keychain" : "in ~/.basalt/keys")}."
                : $"No key yet. Get one at {provider.ApiKeyUrl}"
        };

        var save = new Button { Content = "Save key", FontSize = 11 };

        save.Click += (_, _) =>
        {
            var key = (box.Text ?? "").Trim();

            if (key.Length == 0) return;

            status.Text = _keys.Set(provider.Vendor, key)
                ? "Saved."
                : "The key could not be stored.";

            box.Text = "";
        };

        var remove = new Button { Content = "Remove", FontSize = 11, IsEnabled = stored };

        remove.Click += (_, _) =>
        {
            _keys.Remove(provider.Vendor);
            status.Text = "Removed.";
            remove.IsEnabled = false;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { save, remove }
        };

        var stack = new StackPanel { Spacing = 4 };

        stack.Children.Add(new TextBlock { Text = "API key", FontSize = 12 });
        stack.Children.Add(box);
        stack.Children.Add(buttons);
        stack.Children.Add(status);

        return stack;
    }

    /// <summary>
    /// A page for one language's own settings.
    ///
    /// Values are kept loosely, so a language can offer settings this class
    /// has never heard of.
    /// </summary>
    private Control LanguagePage(int languageIndex)
    {
        var languages = _languages?.Providers.ToList() ?? [];

        if (languageIndex < 0 || languageIndex >= languages.Count) return Page();

        var identity = languages[languageIndex].Identity;

        return Page(
            new TextBlock
            {
                Text = $"{identity.DisplayName} — {string.Join(" ", identity.FileExtensions)}",
                FontWeight = FontWeight.SemiBold
            },

            Number("Indentation size",
                   double.TryParse(_settings.GetLanguageSetting(identity.Id, "IndentSize"), out var indent)
                       ? indent
                       : _settings.Editor.IndentationSize,
                   1, 16,
                   v => Apply(() => _settings.SetLanguageSetting(
                       identity.Id, "IndentSize", ((int)v).ToString()))),

            Switch("Format this language while typing",
                   _settings.GetLanguageSetting(identity.Id, "FormatWhileTyping", "true") == "true",
                   v => Apply(() => _settings.SetLanguageSetting(
                       identity.Id, "FormatWhileTyping", v ? "true" : "false"))));
    }

    /// <summary>
    /// Saves a change and tells the IDE about it.
    ///
    /// Saving on every change rather than on closing means a crash cannot lose
    /// the user's settings, and the window has no state to reconcile.
    /// </summary>
    private void Apply(Action change)
    {
        change();

        _status.Text = _store.Save(_settings)
            ? Localizer.Get(StringKeys.SettingsSaved)
            : Localizer.Get(StringKeys.SettingsSaveFailed);

        SettingsChanged?.Invoke(this, _settings);
    }

    // Field builders

    /// <summary>
    /// Stacks the rows of a settings page.
    ///
    /// The gap depends on what is next to what: a fixed 16 between two check
    /// boxes about 20 high left almost an empty line between them, and the
    /// page read as a list of unrelated things. Two switches belong close
    /// together; a labelled field needs room so its label reads as belonging
    /// to the field below it and not to the row above.
    /// </summary>
    private static Control Page(params Control[] rows)
    {
        var panel = new StackPanel();

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];

            if (i > 0) row.Margin = new Thickness(0, GapBefore(rows[i - 1], row), 0, 0);

            panel.Children.Add(row);
        }

        return panel;
    }

    /// <summary>
    /// How much room a row needs above it.
    ///
    /// Two bare switches sit close. Anything else — a labelled field, or a
    /// switch carrying a hint — gets the wider gap, and so does the row after
    /// one: seen rendered, a hint sat nearer the switch below it than the one
    /// it belonged to, and read as explaining the wrong control.
    /// </summary>
    private static double GapBefore(Control previous, Control row) =>
        IsBareSwitch(previous) && IsBareSwitch(row) ? Spacing.Small : Spacing.Large;

    /// <summary>
    /// Whether a row is a check box on its own.
    ///
    /// A switch with a hint under it arrives as a panel, not a check box, and
    /// is a group rather than a single line.
    /// </summary>
    private static bool IsBareSwitch(Control row) => row is CheckBox;

    private static Control Text(string label, string value, Action<string> apply, string? hint = null)
    {
        var box = new TextBox { Text = value };

        // Applied on losing focus, so a half-typed value is never saved.
        box.LostFocus += (_, _) => apply(box.Text ?? "");

        return Labelled(label, box, hint);
    }

    private static Control Number(
        string label, double value, double minimum, double maximum,
        Action<double> apply, string? hint = null)
    {
        var box = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)minimum,
            Maximum = (decimal)maximum,
            Increment = 1
        };

        box.ValueChanged += (_, _) =>
        {
            if (box.Value is { } chosen) apply((double)chosen);
        };

        return Labelled(label, box, hint);
    }

    /// <summary>The theme a shown name stands for.</summary>
    private static AppTheme ThemeNamed(string displayName) =>
        Enum.GetValues<AppTheme>()
            .FirstOrDefault(t => IdeThemes.DisplayName(t) == displayName);

    private static Control Choice(
        string label, IReadOnlyList<string> options, string value,
        Action<string> apply, string? hint = null)
    {
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = value,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string chosen) apply(chosen);
        };

        return Labelled(label, combo, hint);
    }

    private static Control Switch(string label, bool value, Action<bool> apply, string? hint = null)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) => apply(box.IsChecked == true);

        return hint is null
            ? box
            : new StackPanel { Spacing = 2, Children = { box, Hint(hint) } };
    }

    private static Control Labelled(string label, Control editor, string? hint)
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(editor);

        if (hint is not null) panel.Children.Add(Hint(hint));

        return panel;
    }

    private static Control Hint(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.65,
        TextWrapping = TextWrapping.Wrap
    };
}
