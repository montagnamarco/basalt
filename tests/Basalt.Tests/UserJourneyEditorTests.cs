using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The VB editor and its navigation panels, driven the way a person at the
/// keyboard would: a real solution on disk, a real <see cref="MainWindow"/>,
/// real key presses landing on whichever control is focused.
///
/// Each test is one journey from the outcome's list. Where a journey turned
/// out to be broken, the fix lives next to the code it changed and this file
/// carries the regression test that fails without it.
/// </summary>
public sealed class UserJourneyEditorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-editor-").FullName;

    private string HelperPath => Path.Combine(_root, "Helper.vb");
    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private const string HelperSource =
        "Module Helper\n    Sub Greet()\n        System.Console.WriteLine(\"hi\")\n    End Sub\nEnd Module\n";

    private const string ProgramSource =
        "Module Program\n    Sub Main()\n        Helper.Greet()\n    End Sub\nEnd Module\n";

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

    private static async Task<Window?> WaitForDialogAsync(TestWindow host, int maxPasses = 100)
    {
        Window? dialog = null;
        for (var i = 0; i < maxPasses && dialog is null; i++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }
        return dialog;
    }

    private static void ClickDefaultButton(Window dialog)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().First(b => b.IsDefault);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    // ---- journeys -------------------------------------------------------

    /// <summary>Go to Definition (F12) lands on the declaration, in the other file.</summary>
    [AvaloniaFact]
    public async Task F12JumpsFromAUseToItsDeclarationInAnotherFile()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = ProgramSource.IndexOf("Greet", StringComparison.Ordinal) + 2;

        PressKey(w, Key.F12, RawInputModifiers.None, PhysicalKey.F12);

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == HelperPath),
            "F12 did not switch the active document to the declaring file.");

        var declaration = TextEditorFor(w, HelperPath);
        var line = declaration.Document.GetLineByOffset(declaration.CaretOffset);
        Assert.Equal(2, line.LineNumber);
    }

    /// <summary>
    /// Find All References (Shift+F12) lists every use, including the
    /// declaration, and double-clicking a row in another file opens it there.
    ///
    /// The user reported this misbehaving in the real application; this
    /// drives the exact keyboard path (Shift+F12, not calling the view
    /// model directly) to be sure the panel the key opens is the one that
    /// gets the results.
    /// </summary>
    [AvaloniaFact]
    public async Task ShiftF12ListsReferencesAndDoubleClickNavigatesToAnotherFile()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, HelperPath);
        editor.CaretOffset = HelperSource.IndexOf("Greet", StringComparison.Ordinal) + 2;

        PressKey(w, Key.F12, RawInputModifiers.Shift, PhysicalKey.F12);

        var references = Field<ReferencesPanel>(w, "_references");

        Assert.True(await WaitUntilAsync(host, () => references.Rows.Count > 0),
            "Shift+F12 produced no references.");

        Assert.Equal(2, references.Rows.Count);
        Assert.Contains(references.Rows, r => r.Location.FilePath == ProgramPath);
        Assert.Contains(references.Rows, r => r.Location.FilePath == HelperPath);

        var fromProgram = references.Rows.First(r => r.Location.FilePath == ProgramPath);
        references.ListForTests.ItemsSource = references.Rows;
        references.ListForTests.SelectedItem = fromProgram;
        references.ListForTests.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == ProgramPath),
            "Double-clicking a reference row did not open its file.");

        var opened = TextEditorFor(w, ProgramPath);
        var line = opened.Document.GetLineByOffset(opened.CaretOffset);
        Assert.Equal(3, line.LineNumber);
    }

    /// <summary>Rename (F2) renames the symbol everywhere, across files.</summary>
    [AvaloniaFact]
    public async Task F2RenamesTheSymbolInEveryFileThatUsesIt()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, HelperPath);
        editor.CaretOffset = HelperSource.IndexOf("Greet", StringComparison.Ordinal) + 2;

        PressKey(w, Key.F2, RawInputModifiers.None, PhysicalKey.F2);

        var nameDialog = await WaitForDialogAsync(host);
        Assert.NotNull(nameDialog);

        var box = nameDialog!.GetVisualDescendants().OfType<TextBox>().First();
        box.Text = "Hello";
        ClickDefaultButton(nameDialog);

        var previewDialog = await WaitForDialogAsync(host);
        Assert.NotNull(previewDialog);
        ClickDefaultButton(previewDialog!);

        Assert.True(await WaitUntilAsync(
            host, () => File.ReadAllText(ProgramPath).Contains("Hello", StringComparison.Ordinal)),
            "The rename never reached Program.vb.");

        Assert.Contains("Sub Hello()", File.ReadAllText(HelperPath));
        Assert.Contains("Helper.Hello()", File.ReadAllText(ProgramPath));
        Assert.DoesNotContain("Greet", File.ReadAllText(HelperPath));
        Assert.DoesNotContain("Greet", File.ReadAllText(ProgramPath));
    }

    /// <summary>Go to Line (Ctrl+G) moves the caret to the line asked for.</summary>
    [AvaloniaFact]
    public async Task ControlGMovesTheCaretToTheRequestedLine()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        PressKey(w, Key.G, RawInputModifiers.Control, PhysicalKey.G);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var box = dialog!.GetVisualDescendants().OfType<TextBox>().First();
        box.Text = "3";
        ClickDefaultButton(dialog);

        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        Assert.Equal(3, line.LineNumber);
    }

    /// <summary>
    /// Find in Files (Ctrl+Shift+F) opens the Search panel and Replace All
    /// rewrites every matching file on disk. Ctrl+H is the editor's own
    /// replace bar, as in Visual Studio (UserJourneyFindReplaceTests).
    /// </summary>
    [AvaloniaFact]
    public async Task FindInFilesOpensSearchAndReplaceAllRewritesTheFiles()
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

        search.TermBox.Text = "Greet";
        search.ReplacementBox.Text = "Hi";
        search.ReplaceAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await host.SettleAsync(10);

        Assert.True(await WaitUntilAsync(
            host, () => !File.ReadAllText(HelperPath).Contains("Greet", StringComparison.Ordinal)),
            "Replace All did not rewrite Helper.vb.");

        Assert.Contains("Sub Hi()", File.ReadAllText(HelperPath));

        // Program.vb is open, so it is changed in its editor and reaches the
        // disk when saved, as any edit does: Replace All never writes over
        // what an open editor holds.
        Assert.Contains("Helper.Hi()", vm.ActiveDocument!.Text);
        Assert.DoesNotContain("Helper.Hi()", File.ReadAllText(ProgramPath));
        await vm.SaveAllAsync();
        Assert.Contains("Helper.Hi()", File.ReadAllText(ProgramPath));
    }

    /// <summary>
    /// Completion commits with Tab through the real window, the same as it
    /// does when driven directly against a bare <see cref="CodeEditor"/>.
    /// </summary>
    [AvaloniaFact]
    public async Task CompletionCommitsWithTabThroughTheRealWindow()
    {
        const string typing = "Module Program\n    Sub Greet()\n    End Sub\n    Sub Main()\n        Gre\n    End Sub\nEnd Module\n";
        var project = WriteProject();
        var path = Path.Combine(_root, "Typing.vb");
        File.WriteAllText(path, typing);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(path);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(path);
        Assert.NotNull(code);
        var editor = code!.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.CaretOffset = typing.IndexOf("        Gre", StringComparison.Ordinal) + "        Gre".Length;
        editor.TextArea.Focus();

        await code.ShowCompletionForTestsAsync();
        Assert.Equal("Greet", code.SelectedCompletionForTests);

        PressKey(w, Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);

        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains("Greet()", StringComparison.Ordinal)),
            "Tab did not commit the completion through the real window.");
    }

    /// <summary>
    /// Save All must format every modified document exactly as a single Save
    /// does, not merely write it to disk.
    ///
    /// Before the fix, <c>MainWindowViewModel.SaveAllAsync</c> called
    /// <c>EditorDocumentViewModel.SaveAsync</c> directly: that only writes
    /// the file, so Save All skipped format-on-save (and the language
    /// service sync a single Save also does).
    /// </summary>
    [AvaloniaFact]
    public async Task SaveAllFormatsEveryModifiedDocumentLikeASingleSaveDoes()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(HelperPath);
        await host.SettleAsync(5);

        vm.FormatOnSave = true;

        const string messy = "Module Helper\nSub Greet()\nDim x As Integer=1\nEnd Sub\nEnd Module\n";
        vm.ActiveDocument!.Text = messy;
        await host.SettleAsync(5);
        Assert.True(vm.ActiveDocument!.IsModified);
        Assert.Equal(messy, vm.ActiveDocument!.Text);

        await vm.SaveAllAsync();

        var written = await File.ReadAllTextAsync(HelperPath);

        // What RoslynFormattingService does to this exact shape of input is
        // already covered by FormattingServiceTests; what matters here is
        // only that Save All ran it at all.
        Assert.Contains("    Sub Greet()", written);
        Assert.Contains("        Dim x As Integer = 1", written);
    }

    /// <summary>
    /// A form's code file and its .Designer.vb both edited: Save All saves the form's companion while
    /// saving the first one, and that save must format it too instead of writing it as typed.
    /// </summary>
    [AvaloniaFact]
    public async Task SaveAllFormatsAFormsDesignerFileSavedAlongsideItsCodeFile()
    {
        var project = WriteProject();
        var formPath = Path.Combine(_root, "Form1.vb");
        var designerPath = Path.Combine(_root, "Form1.Designer.vb");
        File.WriteAllText(formPath, "Public Class Form1\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class Form1\nEnd Class\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        vm.FormatOnSave = true;

        await vm.OpenFileAsync(formPath);
        await host.SettleAsync(3);
        var form = vm.OpenDocuments.Single(d => d.FilePath == formPath);
        await vm.OpenFileAsync(designerPath);
        await host.SettleAsync(3);
        var designer = vm.OpenDocuments.Single(d => d.FilePath == designerPath);
        Assert.False(form.OpenInDesigner || designer.OpenInDesigner, "both files must be open as text for this journey.");

        form.Text = "Public Class Form1\nSub Greet()\nDim x As Integer=1\nEnd Sub\nEnd Class\n";
        designer.Text = "Partial Class Form1\nSub Build()\nDim y As Integer=2\nEnd Sub\nEnd Class\n";
        await host.SettleAsync(3);

        await vm.SaveAllAsync();

        Assert.Contains("        Dim x As Integer = 1", await File.ReadAllTextAsync(formPath));
        Assert.Contains("        Dim y As Integer = 2", await File.ReadAllTextAsync(designerPath));
        Assert.False(form.IsModified || designer.IsModified, "Save All left a document marked as modified.");
    }

    // ---- journey: undo and redo in the code editor ---------------------

    /// <summary>
    /// Ctrl+Z undoes what was just typed in the code editor and the redo
    /// shortcut puts it back, the same as the Edit menu.
    /// </summary>
    /// <remarks>
    /// Before the fix, the window-level shortcut handler matched Ctrl+Z to
    /// Edit &gt; Undo and marked the key handled, but
    /// <c>MainWindowViewModel.Undo</c> only reached a designer: in a code
    /// editor the key was swallowed and nothing was undone.
    /// </remarks>
    [AvaloniaFact]
    public async Task ControlZUndoesTypingInTheCodeEditorAndTheRedoShortcutRestoresIt()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = ProgramSource.IndexOf("Helper.Greet()", StringComparison.Ordinal) + "Helper.Greet()".Length;
        editor.TextArea.Focus();

        w.KeyTextInput(" ' later");
        var typed = ProgramSource.Replace("Helper.Greet()", "Helper.Greet() ' later", StringComparison.Ordinal);
        Assert.True(await WaitUntilAsync(host, () => editor.Text == typed), editor.Text);

        PressKey(w, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
        Assert.True(await WaitUntilAsync(host, () => editor.Text == ProgramSource),
            $"Ctrl+Z left:\n{editor.Text}");

        PressKey(w, Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Z);
        Assert.True(await WaitUntilAsync(host, () => editor.Text == typed),
            $"Ctrl+Shift+Z left:\n{editor.Text}");
    }

    /// <summary>
    /// Editing shortcuts pressed in a text box belong to that text box: Ctrl+X
    /// in the search term cuts the term, and leaves the code editor's own
    /// selection alone.
    /// </summary>
    /// <remarks>
    /// Before the fix, the window-level shortcut handler ran Edit &gt; Cut on
    /// the active code editor whatever had the keyboard focus, so the
    /// selected code vanished and the text box kept its text.
    /// </remarks>
    [AvaloniaFact]
    public async Task EditingShortcutsInATextBoxActOnTheTextBoxNotTheCodeEditor()
    {
        var project = WriteProject();
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.Select(ProgramSource.IndexOf("Helper", StringComparison.Ordinal), "Helper".Length);

        PressKey(w, Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F);
        await host.SettleAsync(5);
        var search = Field<SearchPanel>(w, "_search");
        search.TermBox.Focus();
        search.TermBox.Text = "Greet";
        search.TermBox.SelectAll();
        await host.SettleAsync(2);

        PressKey(w, Key.X, RawInputModifiers.Control, PhysicalKey.X);
        await host.SettleAsync(5);

        Assert.Equal(ProgramSource, editor.Text);
        Assert.Equal("", search.TermBox.Text ?? "");

        w.KeyTextInput("Main");
        PressKey(w, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
        await host.SettleAsync(5);

        Assert.Equal(ProgramSource, editor.Text);
        Assert.NotEqual("Main", search.TermBox.Text);
    }

    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(o)!;
}
