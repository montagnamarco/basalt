using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Commands;
using Basalt.Core.Localization;
using Basalt.Extensibility;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Search;

namespace Basalt.Tests;

/// <summary>
/// Navigating and searching the solution, driven the way a person at the
/// keyboard and mouse would: a real project on disk, a real
/// <see cref="MainWindow"/>, real key presses and real pointer clicks.
///
/// Each test is one journey: command palette, Go to File, Go to Symbol,
/// the outline, the search panel, and Navigate Back and Forward. Where a
/// journey turned out to be broken, the fix lives next to the code it
/// changed and this file carries the regression test that fails without it.
/// </summary>
public sealed class UserJourneyNavigationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-navigation-").FullName;

    private string HelperPath => Path.Combine(_root, "Helper.vb");
    private string ProgramPath => Path.Combine(_root, "Program.vb");
    private string UtilitiesPath => Path.Combine(_root, "Utilities.vb");
    private string NotesPath => Path.Combine(_root, "Notes.txt");

    private const string HelperSource =
        "Module Helper\n    Sub Greet()\n        System.Console.WriteLine(\"hi\")\n    End Sub\nEnd Module\n";

    private const string ProgramSource =
        "Module Program\n    Sub Main()\n        Helper.Greet()\n    End Sub\nEnd Module\n";

    private const string UtilitiesSource =
        "Module Utilities\n    Function Greeting() As String\n        Return \"hi\"\n    End Function\nEnd Module\n";

    private const string NotesSource = "greet from the notes\n";

    /// <summary>Writes the project and its three files; the test then opens it.</summary>
    private string WriteProject()
    {
        var project = Path.Combine(_root, "Journey.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(HelperPath, HelperSource);
        File.WriteAllText(ProgramPath, ProgramSource);
        File.WriteAllText(UtilitiesPath, UtilitiesSource);
        File.WriteAllText(NotesPath, NotesSource);
        return project;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ---- shared helpers -----------------------------------------------

    private static TextEditor TextEditorFor(MainWindow window, string filePath)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);
        return code!.GetVisualDescendants().OfType<TextEditor>().Single();
    }

    private static void PressKey(TopLevel window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    /// <summary>
    /// A key press into a dialog. Only the press is sent: a dialog that
    /// closes on Enter or Escape is disposed by the time a release would
    /// arrive, and the headless window then refuses it.
    /// </summary>
    private static void Press(TopLevel window, Key key, RawInputModifiers modifiers, PhysicalKey physical) =>
        window.KeyPress(key, modifiers, physical, null);

    /// <summary>Whether a dialog the main window opened is still open.</summary>
    private static bool IsOpen(TestWindow host, Window dialog) => host.Window.OwnedWindows.Contains(dialog);

    /// <summary>
    /// Runs a menu entry by its label, the way choosing it from the menu does.
    /// On macOS the menu is the system menu bar, which is not part of the
    /// window's visual tree, so the entries are read from the same description
    /// the menus are built from.
    /// </summary>
    private static void ChooseMenuEntry(MainWindow window, string header)
    {
        var entry = Flatten(window.MenuEntriesForTests()).Single(e => e.Header == header);
        Assert.NotNull(entry.Action);
        entry.Action!();
    }

    private static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;

            if (entry.Children is not null)
                foreach (var child in Flatten(entry.Children)) yield return child;
        }
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 100)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    /// <summary>The first window of type <typeparamref name="T"/> the main window owns.</summary>
    private static async Task<T?> WaitForOwnedAsync<T>(TestWindow host, int maxPasses = 100)
        where T : Window
    {
        for (var i = 0; i < maxPasses; i++)
        {
            var found = host.Window.OwnedWindows.OfType<T>().FirstOrDefault();
            if (found is not null) return found;
            await host.SettleAsync(1);
        }
        return host.Window.OwnedWindows.OfType<T>().FirstOrDefault();
    }

    /// <summary>A real press and release at the middle of a control, as a mouse would send it.</summary>
    private static async Task ClickAsync(TestWindow host, Visual target)
    {
        var centre = new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
        var point = target.TranslatePoint(centre, host.Window)
            ?? throw new InvalidOperationException("The control is not on screen to click.");

        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        await host.SettleAsync(3);
    }

    /// <summary>
    /// The double-click the tree and list controls act on. The headless
    /// window sends single presses only, so the double tap is raised on the
    /// row after the real press, the same as the reference-list journey does.
    /// </summary>
    private static void DoubleTap(Interactive row) =>
        row.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

    private static async Task WaitForSearchToFinishAsync(TestWindow host, SearchPanel search)
    {
        var searching = Localizer.Get(StringKeys.SearchSearching);
        Assert.True(await WaitUntilAsync(host, () => search.Summary != searching),
            "The search never finished.");
    }

    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(o)!;

    // ---- journey 1: command palette -----------------------------------

    /// <summary>
    /// Ctrl+Shift+P opens the palette, typing narrows its list, and Enter
    /// runs the command at the top of it: here Toggle Comment on the caret's
    /// line of the document in front.
    /// </summary>
    [AvaloniaFact]
    public async Task CommandPaletteOpensNarrowsAndEnterRunsTheChosenCommand()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = ProgramSource.IndexOf("Helper.Greet()", StringComparison.Ordinal);
        editor.TextArea.Focus();

        PressKey(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P);

        var palette = await WaitForOwnedAsync<CommandPalette>(host);
        Assert.NotNull(palette);

        palette!.KeyTextInput("toggle comment");
        await host.SettleAsync(3);

        Assert.NotEmpty(palette.Matches);
        Assert.All(palette.Matches, c =>
            Assert.Contains("toggle comment", c.SearchText, StringComparison.Ordinal));
        Assert.True(palette.Matches.Count < IdeCommands.CreateRegistry().All.Count,
            "Typing did not narrow the palette's list.");
        Assert.Equal(IdeCommands.EditToggleComment, palette.Matches[palette.SelectedIndex].Id);

        Press(palette, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);

        Assert.True(await WaitUntilAsync(host, () => !IsOpen(host, palette)), "Enter did not close the palette.");
        Assert.Equal(IdeCommands.EditToggleComment, palette.Chosen?.Id);

        var caretLine = editor.Document.GetLineByOffset(editor.CaretOffset);
        Assert.True(await WaitUntilAsync(host,
                () => editor.Document.GetText(caretLine.Offset, caretLine.Length).TrimStart().StartsWith('\'')),
            $"Enter did not run Toggle Comment on the caret's line:\n{editor.Text}");
    }

    /// <summary>Escape closes the palette and runs nothing, however far the user had typed.</summary>
    [AvaloniaFact]
    public async Task EscapeClosesThePaletteWithoutRunningTheCommand()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = ProgramSource.IndexOf("Helper.Greet()", StringComparison.Ordinal);
        editor.TextArea.Focus();

        PressKey(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P);

        var palette = await WaitForOwnedAsync<CommandPalette>(host);
        Assert.NotNull(palette);

        palette!.KeyTextInput("toggle comment");
        await host.SettleAsync(3);
        Press(palette, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);

        Assert.True(await WaitUntilAsync(host, () => !IsOpen(host, palette)), "Escape did not close the palette.");
        await host.SettleAsync(5);

        Assert.Null(palette.Chosen);
        Assert.Equal(ProgramSource, editor.Text);
    }

    // ---- journey 2: Go to File ----------------------------------------

    /// <summary>
    /// Ctrl+Shift+O opens Go to File: typing part of a file name lists the
    /// file, and Enter opens it and makes its tab the active one.
    /// </summary>
    [AvaloniaFact]
    public async Task GoToFileListsAFileByPartOfItsNameAndEnterOpensIt()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        PressKey(w, Key.O, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.O);

        var dialog = await WaitForOwnedAsync<GoToFileDialog>(host);
        Assert.NotNull(dialog);
        Assert.Contains(dialog!.Choices, c => c.FilePath == HelperPath);
        Assert.Contains(dialog.Choices, c => c.FilePath == UtilitiesPath);

        dialog.KeyTextInput("util");
        await host.SettleAsync(3);

        Assert.Single(dialog.Choices);
        Assert.Equal(UtilitiesPath, dialog.Choices[0].FilePath);

        Press(dialog, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == UtilitiesPath),
            "Enter did not open the chosen file and make its tab active.");
        Assert.Contains(vm.OpenDocuments, d => d.FilePath == UtilitiesPath);
    }

    // ---- journey 3: Go to Symbol --------------------------------------

    /// <summary>
    /// Ctrl+T finds a Sub in another file by typing its name, and Enter opens
    /// that file with the caret on the declaration.
    /// </summary>
    [AvaloniaFact]
    public async Task GoToSymbolFindsASubInAnotherFileAndEnterPlacesTheCaretOnIt()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        PressKey(w, Key.T, RawInputModifiers.Control, PhysicalKey.T);

        var dialog = await WaitForOwnedAsync<GoToSymbolDialog>(host);
        Assert.NotNull(dialog);

        dialog!.KeyTextInput("Greet");

        Assert.True(await WaitUntilAsync(host,
                () => dialog.Choices.Any(c => c.Name == "Greet" && c.FilePath == HelperPath)),
            "Typing the name of a Sub in another file found no symbol called that.");

        Press(dialog, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == HelperPath),
            "Enter did not open the file declaring the Sub.");

        var declaration = TextEditorFor(w, HelperPath);
        Assert.True(await WaitUntilAsync(host,
                () => declaration.Document.GetLineByOffset(declaration.CaretOffset).LineNumber == 2),
            "The caret is not on the Sub declaration.");
    }

    // ---- journey 4: outline -------------------------------------------

    /// <summary>
    /// The outline lists the members of the active file, follows the active
    /// document when the user switches tabs, and a double-click on a member
    /// moves the caret to it.
    /// </summary>
    [AvaloniaFact]
    public async Task OutlineListsTheActiveFileFollowsTheTabAndDoubleClickNavigates()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        ChooseMenuEntry(w, Localizer.Get(StringKeys.ToolOutline));
        var outline = Field<OutlinePanel>(w, "_outline");

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        Assert.True(await WaitUntilAsync(host, () => outline.Symbols.Count > 0),
            "The outline lists nothing for the active file.");
        Assert.Equal("Helper", outline.Symbols.Single().Name);
        Assert.Equal("Greet", outline.Symbols.Single().Children.Single().Name);

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        Assert.True(await WaitUntilAsync(host, () => outline.Symbols.Count > 0 && outline.Symbols[0].Name == "Program"),
            "The outline did not follow the active document when the tab changed.");
        Assert.Equal("Main", outline.Symbols.Single().Children.Single().Name);

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);
        Assert.True(await WaitUntilAsync(host, () => outline.Symbols.Count > 0 && outline.Symbols[0].Name == "Helper"));

        var editor = TextEditorFor(w, HelperPath);
        editor.CaretOffset = 0;
        editor.TextArea.Focus();
        await host.SettleAsync(3);

        // A real double-click: the press selects Greet, the second tap is the double tap.
        var greet = await FindOutlineItemAsync(host, outline, "Greet");
        await ClickAsync(host, greet);
        DoubleTap(greet);
        await host.SettleAsync(5);

        Assert.True(await WaitUntilAsync(host,
                () => editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber == 2),
            "Double-clicking Greet did not move the caret to its declaration.");
    }

    /// <summary>
    /// Double-clicking a member again after the caret has moved away must
    /// navigate again. The first click selected the member; the second one
    /// selects nothing new, so the outline must not wait for a selection change.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>OutlinePanel</c> navigated only when the selection
    /// changed. Clicking the member that was already selected did nothing,
    /// and a double-click on it looked broken to the user.
    /// </remarks>
    [AvaloniaFact]
    public async Task DoubleClickingTheSameMemberAgainNavigatesAfterTheCaretMoved()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        ChooseMenuEntry(w, Localizer.Get(StringKeys.ToolOutline));
        var outline = Field<OutlinePanel>(w, "_outline");

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);
        Assert.True(await WaitUntilAsync(host, () => outline.Symbols.Count > 0));

        var editor = TextEditorFor(w, HelperPath);
        editor.CaretOffset = 0;
        editor.TextArea.Focus();

        var greet = await FindOutlineItemAsync(host, outline, "Greet");
        await ClickAsync(host, greet);
        Assert.True(await WaitUntilAsync(host,
                () => editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber == 2),
            "A click on Greet did not navigate to it.");

        editor.CaretOffset = 0;
        await host.SettleAsync(3);

        DoubleTap(await FindOutlineItemAsync(host, outline, "Greet"));
        await host.SettleAsync(5);

        Assert.True(await WaitUntilAsync(host,
                () => editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber == 2),
            "Double-clicking Greet again, with the selection unchanged, did not navigate to it.");
    }

    // ---- journey 5: search panel --------------------------------------

    /// <summary>
    /// Ctrl+Shift+F opens the Search panel, scoped to the solution folder.
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlShiftFOpensTheSearchPanelOverTheSolution()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);

        var search = Field<SearchPanel>(w, "_search");
        Assert.True(search.IsEffectivelyVisible, "Ctrl+Shift+F did not open the Search panel.");
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(search.SearchRoot!));
    }

    /// <summary>Runs a search the way the user does: type the term, click Find.</summary>
    private async Task<SearchPanel> SearchForAsync(TestWindow host, string term)
    {
        var w = host.Window;
        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);

        var search = Field<SearchPanel>(w, "_search");
        search.TermBox.Focus();
        search.TermBox.Text = "";
        w.KeyTextInput(term);
        await host.SettleAsync(2);

        await ClickAsync(host, search.FindButton);
        await WaitForSearchToFinishAsync(host, search);
        return search;
    }

    private static IReadOnlyList<string> FileNamesWithHits(SearchPanel search) =>
        search.Results.Where(r => r.IsFileHeader).Select(r => Path.GetFileName(r.FilePath)).Order().ToList();

    private static IReadOnlyList<SearchResultRow> HitRows(SearchPanel search) =>
        search.Results.Where(r => !r.IsFileHeader).ToList();

    /// <summary>
    /// Match case narrows a search to the exact letters typed. A click on the
    /// toggle sets it, and the same term then finds fewer files.
    /// </summary>
    [AvaloniaFact]
    public async Task MatchCaseNarrowsTheSearchToTheLettersTyped()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var search = await SearchForAsync(host, "Greet");
        Assert.Equal(new[] { "Helper.vb", "Notes.txt", "Program.vb", "Utilities.vb" }, FileNamesWithHits(search));

        await ClickAsync(host, search.MatchCaseToggle);
        Assert.True(search.MatchCaseToggle.IsChecked == true, "The Match case toggle did not turn on.");

        await ClickAsync(host, search.FindButton);
        await WaitForSearchToFinishAsync(host, search);

        Assert.Equal(new[] { "Helper.vb", "Program.vb", "Utilities.vb" }, FileNamesWithHits(search));
        Assert.DoesNotContain(HitRows(search), r => r.FilePath == NotesPath);
    }

    /// <summary>
    /// Whole word leaves Greeting out of a search for Greet, and works the
    /// same once Regex is on and the term is an alternation.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>SearchEngine.BuildPattern</c> put the word
    /// boundaries on the outside of the raw pattern, so an alternation
    /// <c>Greet|Shout</c> became <c>\bGreet|Shout\b</c>: the first branch lost
    /// its trailing boundary and matched inside Greeting.
    /// </remarks>
    [AvaloniaFact]
    public async Task WholeWordWithRegexKeepsAnAlternationInsideTheWordBoundaries()
    {
        var project = WriteProject();
        await File.WriteAllTextAsync(UtilitiesPath,
            "Module Utilities\n    Sub Shout()\n    End Sub\n    Function Greeting() As String\n        Return \"hi\"\n    End Function\nEnd Module\n");
        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var search = await SearchForAsync(host, "Greet|Shout");

        // Match case keeps the lower-case notes file out, so only the code is searched.
        await ClickAsync(host, search.MatchCaseToggle);
        await ClickAsync(host, search.WholeWordToggle);
        await ClickAsync(host, search.RegexToggle);
        await ClickAsync(host, search.FindButton);
        await WaitForSearchToFinishAsync(host, search);

        Assert.True(search.MatchCaseToggle.IsChecked == true && search.WholeWordToggle.IsChecked == true
                    && search.RegexToggle.IsChecked == true);
        Assert.DoesNotContain(HitRows(search), r => r.Hit!.LineText.Contains("Greeting", StringComparison.Ordinal));
        Assert.Contains(HitRows(search), r => r.Hit!.LineText.Contains("Sub Shout", StringComparison.Ordinal));
        Assert.Contains(HitRows(search), r => r.Hit!.LineText.Contains("Sub Greet", StringComparison.Ordinal));
        Assert.Equal(3, HitRows(search).Count);
    }

    /// <summary>Regex switches a term from literal text to a pattern.</summary>
    [AvaloniaFact]
    public async Task RegexTurnsTheTermIntoAPattern()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var literal = await SearchForAsync(host, @"Sub \w+\(\)");
        Assert.Empty(HitRows(literal));

        await ClickAsync(host, literal.RegexToggle);
        await ClickAsync(host, literal.FindButton);
        await WaitForSearchToFinishAsync(host, literal);

        Assert.Equal(new[] { "Helper.vb", "Program.vb" }, FileNamesWithHits(literal));
    }

    /// <summary>
    /// The file types box limits the search: ".txt" finds only the notes, and
    /// "vb" without a dot means the same as ".vb".
    /// </summary>
    [AvaloniaFact]
    public async Task TheExtensionsFilterLimitsTheSearchToThoseFileTypes()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        var search = await SearchForAsync(host, "greet");
        Assert.Equal(new[] { "Helper.vb", "Notes.txt", "Program.vb", "Utilities.vb" }, FileNamesWithHits(search));

        search.ExtensionsBox.Focus();
        w.KeyTextInput(".txt");
        await ClickAsync(host, search.FindButton);
        await WaitForSearchToFinishAsync(host, search);
        Assert.Equal(new[] { "Notes.txt" }, FileNamesWithHits(search));

        // Clicking Find moved the focus to its button, so the box is focused again.
        search.ExtensionsBox.Focus();
        search.ExtensionsBox.SelectAll();
        w.KeyTextInput("vb");
        await ClickAsync(host, search.FindButton);
        await WaitForSearchToFinishAsync(host, search);
        Assert.Equal(new[] { "Helper.vb", "Program.vb", "Utilities.vb" }, FileNamesWithHits(search));
    }

    /// <summary>Double-clicking a hit opens its file with the caret on the matching line.</summary>
    [AvaloniaFact]
    public async Task DoubleClickingAResultOpensItsFileAtTheLine()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var search = await SearchForAsync(host, "Greet");

        var hit = HitRows(search).First(r => r.FilePath == HelperPath);
        search.ResultsList.ItemsSource = search.Results;
        search.ResultsList.SelectedItem = hit;
        DoubleTap(search.ResultsList);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == HelperPath),
            "Double-clicking a hit did not open its file.");

        var opened = TextEditorFor(w, HelperPath);
        Assert.True(await WaitUntilAsync(host,
                () => opened.Document.GetLineByOffset(opened.CaretOffset).LineNumber == hit.Hit!.Line),
            $"The file did not open with the caret on line {hit.Hit!.Line}: caret at offset {opened.CaretOffset} " +
            $"(line {opened.Document.GetLineByOffset(opened.CaretOffset).LineNumber}) of {opened.Document.TextLength}.");
    }

    /// <summary>
    /// Replace All with Match case rewrites only the letters that match the
    /// term, on disk, and leaves the other case alone.
    /// </summary>
    [AvaloniaFact]
    public async Task ReplaceAllWithMatchCaseChangesOnlyTheExactCase()
    {
        var project = WriteProject();
        await File.WriteAllTextAsync(UtilitiesPath, "Module Utilities\n    Dim greet = 1\nEnd Module\n");
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        var search = await SearchForAsync(host, "Greet");
        await ClickAsync(host, search.MatchCaseToggle);

        search.ReplacementBox.Focus();
        w.KeyTextInput("Hello");
        await ClickAsync(host, search.ReplaceAllButton);

        Assert.True(await WaitUntilAsync(host,
                () => File.ReadAllText(HelperPath).Contains("Sub Hello()", StringComparison.Ordinal)),
            "Replace All did not rewrite Helper.vb.");

        Assert.Contains("Helper.Hello()", File.ReadAllText(ProgramPath));
        Assert.Contains("Dim greet = 1", File.ReadAllText(UtilitiesPath));
        Assert.Equal("greet from the notes\n", File.ReadAllText(NotesPath));
    }

    // ---- journey 6: Navigate Back and Forward -------------------------

    /// <summary>
    /// After Go to Definition (F12), Navigate Back returns to the caret
    /// position in the file it came from, at the same offset, and Navigate
    /// Forward goes to the declaration again.
    /// </summary>
    /// <remarks>
    /// Before the fix, only the place F12 left was recorded, never the place
    /// it arrived at, so Back had nothing before it; and Back put the caret
    /// at the start of the line instead of where it had been.
    /// </remarks>
    [AvaloniaFact]
    public async Task NavigateBackAndForwardReturnToTheCaretPositionsAcrossFiles()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var origin = TextEditorFor(w, ProgramPath);
        var originOffset = ProgramSource.IndexOf("Greet", StringComparison.Ordinal) + 2;
        origin.CaretOffset = originOffset;
        origin.TextArea.Focus();

        PressKey(w, Key.F12, RawInputModifiers.None, PhysicalKey.F12);
        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == HelperPath),
            "F12 did not open the declaring file.");

        PressKey(w, Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == ProgramPath),
            "Navigate Back did not return to the file the jump came from.");
        Assert.True(await WaitUntilAsync(host, () => origin.CaretOffset == originOffset),
            $"Navigate Back put the caret at {origin.CaretOffset}, not where it was ({originOffset}).");

        PressKey(w, Key.OemMinus, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Minus);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == HelperPath),
            "Navigate Forward did not return to the declaration.");
        var declaration = TextEditorFor(w, HelperPath);
        Assert.True(await WaitUntilAsync(host,
                () => declaration.Document.GetLineByOffset(declaration.CaretOffset).LineNumber == 2),
            "Navigate Forward did not put the caret back on the declaration.");
    }

    /// <summary>
    /// Go to Line is a jump like any other: Navigate Back returns to the
    /// line the caret was on before it.
    /// </summary>
    /// <remarks>
    /// Before the fix, Ctrl+G moved the caret without recording where it had
    /// been, so Navigate Back had nothing to return to.
    /// </remarks>
    [AvaloniaFact]
    public async Task NavigateBackReturnsFromGoToLine()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, HelperPath);
        editor.CaretOffset = 0;
        editor.TextArea.Focus();

        PressKey(w, Key.G, RawInputModifiers.Control, PhysicalKey.G);

        var dialog = await WaitForOwnedAsync<Window>(host);
        Assert.NotNull(dialog);
        dialog!.GetVisualDescendants().OfType<TextBox>().First().Text = "3";
        ClickDefaultButton(dialog);
        await host.SettleAsync(5);

        Assert.Equal(3, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);

        PressKey(w, Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus);

        Assert.True(await WaitUntilAsync(host,
                () => editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber == 1),
            "Navigate Back did not return to the line Go to Line left.");
    }

    /// <summary>
    /// Navigate Back to a file whose tab was closed reopens it with the caret
    /// where it was left, not on line 1.
    /// </summary>
    /// <remarks>
    /// Before the fix, the jump's landing was recorded only when the caret moved,
    /// and the caret waits for the editor. This test closes both tabs right after
    /// the jump, which can happen before that move: the landing was then dropped,
    /// Navigate Back had only the starting place, and nothing reopened. Failed 9 of
    /// 15 runs on the unfixed code, with "Navigate Back did not reopen the closed
    /// file."; 15 of 15 after the landing is recorded with the jump itself.
    /// </remarks>
    [AvaloniaFact]
    public async Task NavigateBackToAClosedFileReopensItWithTheCaretWhereItWas()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var origin = TextEditorFor(w, ProgramPath);
        var originOffset = ProgramSource.IndexOf("Greet", StringComparison.Ordinal) + 2;
        origin.CaretOffset = originOffset;
        origin.TextArea.Focus();

        PressKey(w, Key.F12, RawInputModifiers.None, PhysicalKey.F12);
        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == HelperPath),
            "F12 did not open the declaring file.");

        // The tab the jump came from is closed, as the user would close it.
        // The declaration's tab is closed too, so the dock has no tab left to
        // lay out the reopened one beside.
        vm.CloseDocument(vm.OpenDocuments.Single(d => d.FilePath == ProgramPath));
        vm.CloseDocument(vm.OpenDocuments.Single(d => d.FilePath == HelperPath));
        await host.SettleAsync(5);
        Assert.Empty(vm.OpenDocuments);

        PressKey(w, Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == ProgramPath),
            "Navigate Back did not reopen the closed file.");

        // Looked up without creating it: CodeEditorForTests would build the
        // editor itself and hide the very delay this test is about.
        Assert.True(await WaitUntilAsync(host, () => RealisedEditorFor(w, ProgramPath) is not null),
            "The reopened file never got an editor.");
        var reopened = RealisedEditorFor(w, ProgramPath)!;

        Assert.True(await WaitUntilAsync(host, () => reopened.CaretOffset == originOffset),
            $"Navigate Back put the caret at {reopened.CaretOffset}, not where it was ({originOffset}).");
    }

    /// <summary>The text editor showing a file, if one has already been realised.</summary>
    private static TextEditor? RealisedEditorFor(MainWindow window, string filePath) =>
        window.GetVisualDescendants().OfType<CodeEditor>()
            .FirstOrDefault(e => e.FilePath == filePath)?
            .GetVisualDescendants().OfType<TextEditor>().FirstOrDefault();

    /// <summary>
    /// The words of the palette, the symbol and file dialogs, and the Go to
    /// Line prompt come from the resources, so they can be translated. The
    /// check is on the source, as SearchPanelLayoutTests does: only English ships.
    /// </summary>
    /// <remarks>
    /// Before the fix, the palette's title and placeholder, the symbol dialog's
    /// placeholder and count, and the Go to Line title and prompt were English
    /// literals in the source.
    /// </remarks>
    [Fact]
    public void NavigationWordsComeFromTheResourcesNotFromLiterals()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "src", "Basalt.Shell")))
            directory = Path.GetDirectoryName(directory);

        Assert.NotNull(directory);
        var shell = Path.Combine(directory!, "src", "Basalt.Shell");

        var literals = new List<string>();
        foreach (var file in new[] { "CommandPalette.cs", "GoToSymbolDialog.cs", "GoToFileDialog.cs" })
        {
            var source = File.ReadAllText(Path.Combine(shell, file));
            literals.AddRange(System.Text.RegularExpressions.Regex
                .Matches(source, @"(?:Title|PlaceholderText) = ""([^""]*[A-Za-z]{3,}[^""]*)""")
                .Select(m => $"{file}: {m.Groups[1].Value}"));
        }

        var mainWindow = File.ReadAllText(Path.Combine(shell, "MainWindow.axaml.cs"));
        if (mainWindow.Contains("\"Go to Line\"", StringComparison.Ordinal)) literals.Add("MainWindow: Go to Line");
        if (mainWindow.Contains("\"Line number:\"", StringComparison.Ordinal)) literals.Add("MainWindow: Line number:");

        Assert.True(literals.Count == 0, $"Literals that should be in Strings.resx: {string.Join(", ", literals)}");
    }

    private static void ClickDefaultButton(Window dialog)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().First(b => b.IsDefault);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>
    /// The outline row for a member, expanding its parents first so the row
    /// exists to be pointed at. Rows appear only after a layout pass, so the
    /// tree is settled before and after the expansion.
    /// </summary>
    private static async Task<TreeViewItem> FindOutlineItemAsync(TestWindow host, OutlinePanel outline, string name)
    {
        var tree = outline.GetVisualDescendants().OfType<TreeView>().Single();

        await host.SettleAsync(3);
        foreach (var parent in tree.GetVisualDescendants().OfType<TreeViewItem>().ToList())
            parent.IsExpanded = true;
        await host.SettleAsync(3);

        return tree.GetVisualDescendants().OfType<TreeViewItem>()
            .First(i => i.DataContext is DocumentSymbol symbol && symbol.Name == name);
    }
}
