using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The small dialogs that ask for one answer: Rename (F2) and Go to Line
/// (Ctrl+G). A user presses the key and types the answer straight away.
/// </summary>
/// <remarks>
/// Defects reintroduced once, each on its own:
/// (1) the dialog selected its text box's text but never focused it, so the
/// typed answer went nowhere: both prompt tests failed on the focus assertion;
/// (2) F2 on "New Person" found Person's implicit constructor and refused with
/// "'.ctor' cannot be renamed": the rename test failed;
/// (3) a refused preview focused its disabled Apply button, so nothing had the
/// focus and Enter did not close it: the refusal test failed.
/// </remarks>
public sealed class UserJourneyPromptDialogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-prompt-").FullName;

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private const string Source =
        "Module Program\n    Sub Main()\n        Dim p As New Person\n        Console.WriteLine(p)\n    End Sub\nEnd Module\n" +
        "Public Class Person\nEnd Class\n";

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string WriteProject()
    {
        var project = Path.Combine(_root, "Journey.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Journey</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(ProgramPath, Source);
        return project;
    }

    private static void PressKey(TopLevel window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 300)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    private static async Task<(TestWindow Host, MainWindowViewModel Vm, TextEditor Editor)> OpenAsync(string project, string file)
    {
        var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        Assert.True(await WaitUntilAsync(host, () => vm.StartupProject is not null && !vm.IsBusy, 600),
            "the project never finished loading.");
        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);
        var editor = host.Window.CodeEditorForTests(file)!.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.TextArea.Focus();
        return (host, vm, editor);
    }

    private static Window? OpenDialog(MainWindow window) => window.OwnedWindows.OfType<Window>().LastOrDefault();

    private static string DescribePreview(Window preview) =>
        $"{preview.GetType().Name} '{preview.Title}' open={preview.IsVisible} focused={FocusedIn(preview)?.GetType().Name} " +
        string.Join(" | ", preview.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Take(12));

    private static object? FocusedIn(Window dialog) => dialog.FocusManager?.GetFocusedElement();

    [AvaloniaFact]
    public async Task RenameTakesTheNewNameTypedRightAfterF2()
    {
        var (host, vm, editor) = await OpenAsync(WriteProject(), ProgramPath);
        using var _ = host;
        var w = host.Window;
        editor.CaretOffset = Source.IndexOf("New Person", StringComparison.Ordinal) + "New Pe".Length;

        PressKey(w, Key.F2, RawInputModifiers.None, PhysicalKey.F2);

        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is not null), "F2 opened no dialog.");
        var dialog = OpenDialog(w)!;
        await host.SettleAsync(3);
        var box = Assert.IsType<TextBox>(FocusedIn(dialog));
        Assert.Equal("Person", box.SelectedText);

        foreach (var character in "Customer") dialog.KeyTextInput(character.ToString());
        // Only pressed: the press closes the dialog, so there is nothing left to release on.
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        // The change is previewed before it is made, with Apply focused: Enter applies it.
        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is { } preview && !ReferenceEquals(preview, dialog)),
            "no preview followed the new name.");
        var preview = OpenDialog(w)!;
        await host.SettleAsync(3);
        preview.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains("New Customer", StringComparison.Ordinal)
            && editor.Text.Contains("Class Customer", StringComparison.Ordinal)),
            $"the rename did not happen:\n{editor.Text}\nPreview: {DescribePreview(preview)}\nOutput:\n{string.Join("\n", vm.BuildOutput.TakeLast(8))}");
    }

    [AvaloniaFact]
    public async Task GoToLineTakesTheNumberTypedRightAfterCtrlG()
    {
        var (host, _, editor) = await OpenAsync(WriteProject(), ProgramPath);
        using var _ = host;
        var w = host.Window;
        editor.CaretOffset = 0;

        PressKey(w, Key.G, RawInputModifiers.Control, PhysicalKey.G);

        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is not null), "Ctrl+G opened no dialog.");
        var dialog = OpenDialog(w)!;
        await host.SettleAsync(3);
        Assert.IsType<TextBox>(FocusedIn(dialog));

        dialog.KeyTextInput("6");
        // Only pressed: the press closes the dialog, so there is nothing left to release on.
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.True(await WaitUntilAsync(host, () => editor.TextArea.Caret.Line == 6),
            $"the caret is on line {editor.TextArea.Caret.Line}, not 6.");
    }

    [AvaloniaFact]
    public async Task ARefusedRenameIsClosedFromTheKeyboard()
    {
        var (host, _, editor) = await OpenAsync(WriteProject(), ProgramPath);
        using var _ = host;
        var w = host.Window;
        editor.CaretOffset = Source.IndexOf("Console", StringComparison.Ordinal) + 3;

        PressKey(w, Key.F2, RawInputModifiers.None, PhysicalKey.F2);
        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is not null), "F2 opened no dialog.");
        var prompt = OpenDialog(w)!;
        await host.SettleAsync(3);
        foreach (var character in "Terminal") prompt.KeyTextInput(character.ToString());
        prompt.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        // Console is declared outside the solution: the preview says so and has nothing to apply.
        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is { } preview && !ReferenceEquals(preview, prompt)),
            "no preview followed the new name.");
        var refusal = OpenDialog(w)!;
        await host.SettleAsync(3);
        var focused = Assert.IsType<Button>(FocusedIn(refusal));
        Assert.True(focused.IsCancel, "the focus is not on Cancel.");

        refusal.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is null), "Enter did not close the refused preview.");
        Assert.Contains("Console.WriteLine", editor.Text, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Defect reintroduced once (the box selected only when the dialog opened):
    /// opened from the command palette, the box lost the focus while the palette
    /// closed, the selection went with it, "6" typed into "1" made "61", and the
    /// caret went to the last line.
    /// </remarks>
    [AvaloniaFact]
    public async Task GoToLineFromTheCommandPaletteReplacesTheCurrentLineNumber()
    {
        var (host, _, editor) = await OpenAsync(WriteProject(), ProgramPath);
        using var _ = host;
        var w = host.Window;
        editor.CaretOffset = 0;

        PressKey(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P);
        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is not null), "Ctrl+Shift+P opened no palette.");
        var palette = OpenDialog(w)!;
        await host.SettleAsync(3);
        foreach (var character in "go to line") palette.KeyTextInput(character.ToString());
        await host.SettleAsync(3);
        palette.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.True(await WaitUntilAsync(host, () => OpenDialog(w) is { } d && !ReferenceEquals(d, palette)),
            "the palette did not open Go to Line.");
        var dialog = OpenDialog(w)!;
        await host.SettleAsync(5);
        dialog.KeyTextInput("6");
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.True(await WaitUntilAsync(host, () => editor.TextArea.Caret.Line == 6),
            $"the caret is on line {editor.TextArea.Caret.Line}, not 6.");
    }
}
