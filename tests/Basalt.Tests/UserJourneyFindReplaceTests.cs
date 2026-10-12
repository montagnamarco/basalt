using System.Text;
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
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Search;

namespace Basalt.Tests;

/// <summary>
/// Finding and replacing text the way a person at the keyboard does it: a real
/// solution on disk, a real <see cref="MainWindow"/>, real key presses and
/// clicks. The editor's own find bar (Ctrl+F, F3, Ctrl+H in one document) and
/// the Search panel (Find in Files, Replace in Files, scopes) are both driven.
/// </summary>
public sealed class UserJourneyFindReplaceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-findreplace-").FullName;

    private string HelperPath => Path.Combine(_root, "Helper.vb");
    private string ProgramPath => Path.Combine(_root, "Program.vb");
    private string NotesPath => Path.Combine(_root, "Notes.txt");
    private string BlobPath => Path.Combine(_root, "Blob.vb");
    private string LegacyPath => Path.Combine(_root, "Legacy.vb");
    private string SlowPath => Path.Combine(_root, "Slow.vb");

    // Greet is on line 2 of Helper.vb and on line 3 of Program.vb.
    private const string HelperSource =
        "Module Helper\n    Sub Greet()\n        System.Console.WriteLine(\"hi\")\n    End Sub\nEnd Module\n";

    private const string ProgramSource =
        "Module Program\n    Sub Main()\n        Helper.Greet()\n    End Sub\nEnd Module\n";

    /// <summary>
    /// The solution the journeys search: two code files, a notes file, a binary
    /// file that contains the term, and build output that contains it too.
    /// </summary>
    private string WriteSolution()
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
        File.WriteAllText(NotesPath, "Greet from the notes.\n");
        File.WriteAllBytes(BlobPath, Encoding.UTF8.GetBytes("Greet\0 binary payload\n"));

        Directory.CreateDirectory(Path.Combine(_root, "bin", "Debug"));
        File.WriteAllText(Path.Combine(_root, "bin", "Debug", "Stale.vb"), "Module Stale\n    Sub Greet()\n    End Sub\nEnd Module\n");
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        File.WriteAllText(Path.Combine(_root, "obj", "Generated.vb"), "' Greet, generated\n");

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

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
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
    /// A click once the control has a place on screen. A press at a control that
    /// has not been laid out lands at the window's corner and does nothing.
    /// </summary>
    private static async Task ClickLaidOutAsync(TestWindow host, Visual target)
    {
        Assert.True(await WaitUntilAsync(host, () => target.Bounds.Width > 0), "A control was never laid out.");
        await ClickAsync(host, target);
    }

    /// <summary>
    /// The double-click the list acts on. The headless window sends single
    /// presses only, so the double tap is raised on the list after the press,
    /// as the navigation journeys do.
    /// </summary>
    private static void DoubleTap(Interactive list) =>
        list.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

    /// <summary>Opens the Search panel with Ctrl+Shift+F, types the term, and clicks Find.</summary>
    private static async Task<SearchPanel> SearchForAsync(TestWindow host, string term, string? extensions = null)
    {
        var w = host.Window;
        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);

        var search = Field<SearchPanel>(w, "_search");
        search.TermBox.Text = term;
        if (extensions is not null) search.ExtensionsBox.Text = extensions;

        await ClickLaidOutAsync(host, search.FindButton);
        await WaitForSearchToFinishAsync(host, search);
        return search;
    }

    /// <summary>
    /// A search is over when Find is back: the panel hides Find while a search
    /// runs. The summary is not a reliable signal, because it shows progress
    /// from the first file on and would read as finished too early.
    /// </summary>
    private static async Task WaitForSearchToFinishAsync(TestWindow host, SearchPanel search)
    {
        Assert.True(await WaitUntilAsync(host, () => search.FindButton.IsVisible), "The search never finished.");
    }

    private static IReadOnlyList<string> FileNamesWithHits(SearchPanel search) =>
        search.Results.Where(r => r.IsFileHeader).Select(r => Path.GetFileName(r.FilePath)).Order().ToList();

    private static IReadOnlyList<SearchResultRow> HitRows(SearchPanel search) =>
        search.Results.Where(r => !r.IsFileHeader).ToList();

    private static EditorFindBar BarFor(TextEditor editor) =>
        EditorFindBar.For(editor) ?? throw new InvalidOperationException("The editor has no find bar.");

    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(o)!;

    // ---- Find in Files ------------------------------------------------

    /// <summary>
    /// Find in Files lists each file once, with its matching lines under it,
    /// and every line carries its own number.
    /// </summary>
    [AvaloniaFact]
    public async Task FindInFilesGroupsEachMatchUnderItsFileWithItsLineNumber()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var search = await SearchForAsync(host, "Greet");

        Assert.Equal(new[] { "Helper.vb", "Notes.txt", "Program.vb" }, FileNamesWithHits(search));

        var helper = HitRows(search).Single(r => r.FilePath == HelperPath);
        Assert.Equal(2, helper.Hit!.Line);
        Assert.Equal("    2: Sub Greet()", helper.Display);

        var program = HitRows(search).Single(r => r.FilePath == ProgramPath);
        Assert.Equal(3, program.Hit!.Line);
        Assert.Equal("    3: Helper.Greet()", program.Display);

        Assert.Contains(search.Results, r => r.IsFileHeader && r.FilePath == HelperPath && r.MatchCount == 1);
    }

    /// <summary>
    /// Picking a match opens its file with the matched text selected on the
    /// line the result shows.
    /// </summary>
    [AvaloniaFact]
    public async Task ClickingAMatchOpensItsFileAtTheLineWithTheMatchSelected()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var search = await SearchForAsync(host, "Greet");

        var hit = HitRows(search).Single(r => r.FilePath == ProgramPath);
        search.ResultsList.ItemsSource = search.Results;
        search.ResultsList.SelectedItem = hit;
        DoubleTap(search.ResultsList);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == ProgramPath),
            "Picking the match did not open Program.vb.");

        // The document is active before its editor control is realised.
        Assert.True(await WaitUntilAsync(host, () =>
                w.CodeEditorForTests(ProgramPath)?.GetVisualDescendants().OfType<TextEditor>().Any() == true),
            "Program.vb opened without an editor.");

        var editor = TextEditorFor(w, ProgramPath);
        Assert.True(await WaitUntilAsync(host, () => editor.SelectedText == "Greet"),
            $"The matched text was not selected; the selection is '{editor.SelectedText}'.");
        Assert.Equal(3, editor.Document.GetLineByOffset(editor.SelectionStart).LineNumber);
    }

    /// <summary>
    /// The file type box takes a glob as people write it: "*.vb" limits the
    /// search to Visual Basic files, the same as ".vb" does.
    /// </summary>
    /// <remarks>
    /// Before the fix, SearchPanel's extension parser turned "*.vb" into
    /// ".*.vb", which no file name ends with, so the search found nothing.
    /// </remarks>
    [AvaloniaFact]
    public async Task AStarVbFileTypeLimitsTheSearchToVisualBasicFiles()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var search = await SearchForAsync(host, "Greet", extensions: "*.vb");

        Assert.Equal(new[] { "Helper.vb", "Program.vb" }, FileNamesWithHits(search));
    }

    /// <summary>
    /// A binary file that contains the term, and the build output folders,
    /// are never listed. Their matches would be noise a person cannot act on.
    /// </summary>
    [AvaloniaFact]
    public async Task BinaryFilesAndBuildOutputAreNeverListed()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var search = await SearchForAsync(host, "Greet");

        var names = FileNamesWithHits(search);
        Assert.DoesNotContain("Blob.vb", names);
        Assert.DoesNotContain("Stale.vb", names);
        Assert.DoesNotContain("Generated.vb", names);
    }

    /// <summary>
    /// Cancel stops a search over a large solution while it runs, and the
    /// panel then says what it found rather than "Searching…" forever.
    /// </summary>
    /// <remarks>
    /// Before the fix there was no Cancel: a search could only be abandoned by
    /// starting another one, and a stopped search left its summary unchanged.
    /// </remarks>
    [AvaloniaFact]
    public async Task CancelStopsALargeSearchAndSaysWhatWasFound()
    {
        const int fileCount = 300;
        var project = WriteSolution();
        var large = Path.Combine(_root, "Large");
        Directory.CreateDirectory(large);
        for (var i = 0; i < fileCount; i++)
            File.WriteAllText(Path.Combine(large, $"File{i:D3}.vb"), $"' Greet {i}\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);
        var search = Field<SearchPanel>(w, "_search");

        // SearchForAsync returns at its first wait for the disk. The search is
        // still running here, so Cancel is pressed before the next file is read.
        var running = search.SearchForAsync("Greet");
        Assert.False(running.IsCompleted, "The search finished before it could be cancelled.");
        Assert.True(search.CancelButton.IsVisible, "No Cancel button is offered while the search runs.");

        search.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(await WaitUntilAsync(host, () => running.IsCompleted),
            "The cancelled search never returned.");
        Assert.False(search.CancelButton.IsVisible, "Cancel is still offered after the search stopped.");
        Assert.True(search.FindButton.IsVisible, "Find did not come back after the search stopped.");

        // The summary says what the rows on screen are, not a progress line from
        // before the stop, nor "Searching…".
        var found = HitRows(search).Count;
        var foundFiles = FileNamesWithHits(search).Count;
        Assert.Equal(Localizer.Get(StringKeys.SearchCancelled, found, foundFiles), search.Summary);
        Assert.True(FileNamesWithHits(search).Count < fileCount,
            "Cancelling did not stop the search before it had read every file.");
    }

    /// <summary>
    /// A pattern that does not parse says why, rather than reporting no matches.
    /// </summary>
    [AvaloniaFact]
    public async Task AnInvalidRegularExpressionInFindInFilesSaysWhatIsWrong()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);
        var search = Field<SearchPanel>(w, "_search");

        search.TermBox.Text = "Greet(";
        await ClickLaidOutAsync(host, search.RegexToggle);
        await ClickLaidOutAsync(host, search.FindButton);

        Assert.Contains("Greet(", search.Summary);
        Assert.NotEqual(Localizer.Get(StringKeys.SearchNoResults), search.Summary);
        Assert.Empty(search.Results);
    }

    /// <summary>
    /// Current project limits Find in Files to the project of the file in front;
    /// Whole solution searches every project.
    /// </summary>
    [AvaloniaFact]
    public async Task TheCurrentProjectScopeSearchesOnlyTheProjectInFront()
    {
        var project = WriteSolution();
        var lib = Path.Combine(_root, "Lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "Lib.vbproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Library</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
        var libFile = Path.Combine(lib, "Sub.vb");
        File.WriteAllText(libFile, "' Greet from the library\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(libFile);
        await host.SettleAsync(5);

        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);
        var search = Field<SearchPanel>(w, "_search");

        search.ScopeBox.SelectedIndex = 1;
        var scoped = await SearchForAsync(host, "Greet");
        Assert.Equal(new[] { "Sub.vb" }, FileNamesWithHits(scoped));

        scoped.ScopeBox.SelectedIndex = 0;
        var whole = await SearchForAsync(host, "Greet");
        Assert.Contains("Helper.vb", FileNamesWithHits(whole));
        Assert.Contains("Sub.vb", FileNamesWithHits(whole));
    }

    // ---- Replace in Files ---------------------------------------------

    /// <summary>
    /// Replace All changes an open file in its editor, as one edit the user can
    /// undo, and leaves that file on disk alone until the user saves. A file
    /// that is not open is rewritten on disk at once.
    /// </summary>
    [AvaloniaFact]
    public async Task ReplaceInFilesUpdatesAnOpenEditorAndLeavesTheDiskUntilSave()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        var search = await SearchForAsync(host, "Greet");
        search.ReplacementBox.Text = "Hi";
        await ClickLaidOutAsync(host, search.ReplaceAllButton);

        var editor = TextEditorFor(w, HelperPath);
        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains("Sub Hi()", StringComparison.Ordinal)),
            "The open editor did not take the replacement.");
        Assert.Equal(HelperSource, File.ReadAllText(HelperPath));

        Assert.True(await WaitUntilAsync(host, () => File.ReadAllText(NotesPath).Contains("Hi from", StringComparison.Ordinal)),
            "Replace in Files did not rewrite the closed Notes.txt.");
        Assert.Contains("Helper.Hi()", File.ReadAllText(ProgramPath));

        editor.Undo();
        Assert.Equal(HelperSource, editor.Text);
        editor.Redo();
        Assert.Contains("Sub Hi()", editor.Text);

        await vm.OpenDocuments.Single(d => d.FilePath == HelperPath).SaveAsync();
        Assert.Contains("Sub Hi()", File.ReadAllText(HelperPath));
    }

    /// <summary>
    /// An open file with unsaved edits keeps them: the replacement reaches the
    /// editor next to the edit, and the file on disk is not overwritten under
    /// the user until they save.
    /// </summary>
    [AvaloniaFact]
    public async Task ReplaceInFilesKeepsAnOpenFilesUnsavedEdits()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, HelperPath);
        editor.Focus();
        editor.CaretOffset = 0;
        w.KeyTextInput("' unsaved note\n");
        await host.SettleAsync(3);
        Assert.Contains("' unsaved note", editor.Text);

        var search = await SearchForAsync(host, "Greet");
        search.ReplacementBox.Text = "Hi";
        await ClickLaidOutAsync(host, search.ReplaceAllButton);

        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains("Sub Hi()", StringComparison.Ordinal)),
            "The replacement did not reach the open editor.");
        Assert.Contains("' unsaved note", editor.Text);
        Assert.Equal(HelperSource, File.ReadAllText(HelperPath));

        await vm.OpenDocuments.Single(d => d.FilePath == HelperPath).SaveAsync();
        var saved = File.ReadAllText(HelperPath);
        Assert.Contains("' unsaved note", saved);
        Assert.Contains("Sub Hi()", saved);
    }

    /// <summary>
    /// Replace All on a Windows-1252 file keeps its accented letters: the
    /// replacement changes the matched word and every other byte stays as it was.
    /// </summary>
    /// <remarks>
    /// Before the fix the file was read as UTF-8, so the accented byte became
    /// U+FFFD and was written back as three bytes, corrupting a file the replace
    /// did not otherwise need to change.
    /// </remarks>
    [AvaloniaFact]
    public async Task ReplaceInFilesKeepsALegacyEncodedFileIntact()
    {
        var project = WriteSolution();
        const string legacyText = "' Saluti è gli amici\r\nSub Greet()\r\nEnd Sub\r\n";
        var legacyBytes = Encoding.Latin1.GetBytes(legacyText);
        File.WriteAllBytes(LegacyPath, legacyBytes);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var search = await SearchForAsync(host, "Greet");
        search.ReplacementBox.Text = "Hi";
        await ClickLaidOutAsync(host, search.ReplaceAllButton);

        var expected = Encoding.Latin1.GetBytes("' Saluti è gli amici\r\nSub Hi()\r\nEnd Sub\r\n");
        Assert.True(await WaitUntilAsync(host, () => File.ReadAllBytes(LegacyPath).SequenceEqual(expected)),
            $"The legacy file came out as {Convert.ToHexString(File.ReadAllBytes(LegacyPath))}, expected {Convert.ToHexString(expected)}.");
    }

    // ---- regular expressions ------------------------------------------

    /// <summary>
    /// A pattern that backtracks past its time limit ends the search of that
    /// one file. The panel still finishes and the other files still show.
    /// </summary>
    /// <remarks>
    /// Before the fix, the time-out escaped SearchEngine into the Find click
    /// handler and took the application down. The file is a 40-character line
    /// that the pattern can never end, so matching it takes exponential time.
    /// </remarks>
    [AvaloniaFact]
    public async Task ACatastrophicPatternDoesNotBringTheSearchDown()
    {
        var project = WriteSolution();
        File.WriteAllText(SlowPath, new string('a', 40) + "x\n");
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);
        var search = Field<SearchPanel>(w, "_search");

        search.TermBox.Text = "^(a+)+$";
        await ClickLaidOutAsync(host, search.RegexToggle);
        Assert.True(search.RegexToggle.IsChecked == true, "The regular expression toggle did not turn on.");

        await ClickLaidOutAsync(host, search.FindButton);
        await WaitForSearchToFinishAsync(host, search);

        Assert.DoesNotContain("Slow.vb", FileNamesWithHits(search));
    }

    // ---- the editor's own find bar ------------------------------------

    /// <summary>Ctrl+F opens the editor's find bar with the word at the caret in it, and leaves the Search panel shut.</summary>
    [AvaloniaFact]
    public async Task CtrlFOpensTheFindBarInTheEditorWithTheWordAtTheCaret()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = ProgramSource.IndexOf("Greet", StringComparison.Ordinal) + 2;
        editor.Focus();
        await host.SettleAsync(2);

        PressKey(w, Key.F, RawInputModifiers.Control, PhysicalKey.F);
        await host.SettleAsync(5);

        var bar = BarFor(editor);
        Assert.True(bar.IsOpen, "Ctrl+F did not open the find bar in the editor.");
        Assert.Equal("Greet", bar.Term);
    }

    /// <summary>F3 moves to the next match and Shift+F3 to the previous, and both wrap at the ends.</summary>
    [AvaloniaFact]
    public async Task F3AndShiftF3MoveBetweenMatchesAndWrapAtBothEnds()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.Text = "Greet a Greet b Greet";
        editor.Select(0, 5);
        PressKey(w, Key.F, RawInputModifiers.Control, PhysicalKey.F);
        await host.SettleAsync(5);
        Assert.Equal("Greet", BarFor(editor).Term);

        PressKey(w, Key.F3, RawInputModifiers.None, PhysicalKey.F3);
        await host.SettleAsync(3);
        Assert.Equal(8, editor.SelectionStart);

        PressKey(w, Key.F3, RawInputModifiers.None, PhysicalKey.F3);
        await host.SettleAsync(3);
        Assert.Equal(16, editor.SelectionStart);

        PressKey(w, Key.F3, RawInputModifiers.None, PhysicalKey.F3);
        await host.SettleAsync(3);
        Assert.Equal(0, editor.SelectionStart);

        PressKey(w, Key.F3, RawInputModifiers.Shift, PhysicalKey.F3);
        await host.SettleAsync(3);
        Assert.Equal(16, editor.SelectionStart);
    }

    /// <summary>Match case, Whole word and Regular expression each change which matches the bar counts.</summary>
    [AvaloniaFact]
    public async Task MatchCaseWholeWordAndRegexChangeTheMatchesInTheFile()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.Text = "Greet greeting Greets Greet";
        editor.CaretOffset = 0;
        PressKey(w, Key.F, RawInputModifiers.Control, PhysicalKey.F);
        await host.SettleAsync(5);

        var bar = BarFor(editor);
        bar.TermBox.Text = "Greet";
        Assert.Equal(Localizer.Get(StringKeys.SearchFileMatches, 4), bar.Status);

        await ClickLaidOutAsync(host, bar.MatchCaseToggle);
        Assert.Equal(Localizer.Get(StringKeys.SearchFileMatches, 3), bar.Status);

        await ClickLaidOutAsync(host, bar.WholeWordToggle);
        Assert.Equal(Localizer.Get(StringKeys.SearchFileMatches, 2), bar.Status);

        await ClickLaidOutAsync(host, bar.MatchCaseToggle);
        await ClickLaidOutAsync(host, bar.WholeWordToggle);
        await ClickLaidOutAsync(host, bar.RegexToggle);
        bar.TermBox.Text = "greet(ing|s)";
        Assert.True(bar.Status == Localizer.Get(StringKeys.SearchFileMatches, 2),
            $"term='{bar.TermBox.Text}' regex={bar.RegexToggle.IsChecked} case={bar.MatchCaseToggle.IsChecked} word={bar.WholeWordToggle.IsChecked} status='{bar.Status}'");
    }

    /// <summary>Ctrl+H adds the replace row: Replace changes one match at a time, Replace All changes the rest as one edit that a single Undo reverts.</summary>
    [AvaloniaFact]
    public async Task CtrlHReplacesOneThenAllAndOneUndoRevertsReplaceAll()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.Text = "Greet a Greet b Greet";
        editor.CaretOffset = 0;
        PressKey(w, Key.H, RawInputModifiers.Control, PhysicalKey.H);
        await host.SettleAsync(5);

        var bar = BarFor(editor);
        Assert.True(bar.IsOpen, "Ctrl+H did not open the find bar.");
        bar.ReplacementBox.Text = "Hi";

        // The first click finds the first match; the second replaces it.
        await ClickLaidOutAsync(host, bar.ReplaceOneButton);
        await ClickLaidOutAsync(host, bar.ReplaceOneButton);
        Assert.Equal("Hi a Greet b Greet", editor.Text);

        await ClickLaidOutAsync(host, bar.ReplaceAllButton);
        Assert.Equal("Hi a Hi b Hi", editor.Text);

        editor.Undo();
        Assert.Equal("Hi a Greet b Greet", editor.Text);
    }

    /// <summary>An invalid pattern in the editor's bar is named in the bar, not left as a silent "no matches".</summary>
    [AvaloniaFact]
    public async Task AnInvalidRegularExpressionInTheEditorSaysWhatIsWrong()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = 0;
        PressKey(w, Key.F, RawInputModifiers.Control, PhysicalKey.F);
        await host.SettleAsync(5);

        var bar = BarFor(editor);
        bar.TermBox.Text = "Greet(";
        await ClickLaidOutAsync(host, bar.RegexToggle);

        Assert.Contains("Greet(", bar.Status);
        Assert.NotEqual(Localizer.Get(StringKeys.SearchNoMatches), bar.Status);
    }

    /// <summary>Esc closes the find bar and gives the keyboard back to the editor's text.</summary>
    [AvaloniaFact]
    public async Task EscClosesFindAndReturnsFocusToTheEditor()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = 0;
        PressKey(w, Key.F, RawInputModifiers.Control, PhysicalKey.F);
        await host.SettleAsync(5);

        var bar = BarFor(editor);
        Assert.True(bar.IsOpen);

        PressKey(w, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        await host.SettleAsync(5);

        Assert.False(bar.IsOpen, "Esc did not close the find bar.");
        Assert.True(editor.TextArea.IsFocused, "Esc did not return the focus to the editor.");
    }

    /// <summary>
    /// Text typed into an open file and not saved is found by Find in Files, and
    /// Replace in Files changes it in the editor while the disk stays as it was.
    /// </summary>
    /// <remarks>
    /// Before the fix both searched the disk only, so the new line was neither
    /// listed nor replaced.
    /// </remarks>
    [AvaloniaFact]
    public async Task FindAndReplaceReadTheUnsavedTextOfAnOpenFile()
    {
        var project = WriteSolution();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, HelperPath);
        editor.Focus();
        editor.CaretOffset = 0;
        w.KeyTextInput("' Greet again\n");
        await host.SettleAsync(3);
        Assert.Contains("' Greet again", editor.Text);

        var search = await SearchForAsync(host, "Greet");
        Assert.Contains(HitRows(search), r => r.FilePath == HelperPath && r.Hit!.Line == 1);

        search.ReplacementBox.Text = "Hi";
        await ClickLaidOutAsync(host, search.ReplaceAllButton);

        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains("' Hi again", StringComparison.Ordinal)),
            "Replace in Files did not change the unsaved line in the editor.");
        Assert.Contains("Sub Hi()", editor.Text);
        Assert.Equal(HelperSource, File.ReadAllText(HelperPath));
    }

    // ---- keyboard schemes ---------------------------------------------

    /// <summary>
    /// Ctrl+F, Ctrl+H and Ctrl+Shift+F mean the same thing in every keyboard
    /// scheme, so no scheme leaves a person without a way to find or replace.
    /// </summary>
    [Fact]
    public void FindReplaceAndFindInFilesKeepTheirShortcutsInEveryScheme()
    {
        foreach (var scheme in Enum.GetValues<KeyboardScheme>())
        {
            var gestures = IdeCommands.Definitions(scheme).ToDictionary(d => d.Id, d => d.DefaultGesture);

            Assert.Equal("Ctrl+F", gestures[IdeCommands.EditFind]);
            Assert.Equal("Ctrl+H", gestures[IdeCommands.EditReplace]);
            Assert.Equal("Ctrl+Shift+F", gestures[IdeCommands.EditFindInFiles]);
        }
    }
}
