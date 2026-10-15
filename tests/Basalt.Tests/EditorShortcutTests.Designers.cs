using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The editing keys with a WinForms designer showing. The designer keeps its
/// own selection and clipboard, and a key pressed in the terminal beside it
/// must reach the terminal, not the designer.
/// </summary>
public sealed class EditorShortcutDesignerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-editor-designer-").FullName;

    public EditorShortcutDesignerTests() => WinFormsDesignerView.ResetClipboardForTests();

    public void Dispose()
    {
        WinFormsDesignerView.ResetClipboardForTests();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private const string TwoButtons = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.Button1 = New Global.System.Windows.Forms.Button()
                Me.Button2 = New Global.System.Windows.Forms.Button()
                Me.Button1.Location = New System.Drawing.Point(10, 10)
                Me.Button1.Size = New System.Drawing.Size(80, 25)
                Me.Button1.Text = "One"
                Me.Button2.Location = New System.Drawing.Point(150, 60)
                Me.Button2.Size = New System.Drawing.Size(80, 25)
                Me.Button2.Text = "Two"
                Me.Controls.Add(Me.Button1)
                Me.Controls.Add(Me.Button2)
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Text = "Main"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Button2 As System.Windows.Forms.Button
        End Class
        """;

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), because);
    }

    private static void PressCtrl(MainWindow window, Key key, PhysicalKey physical) =>
        Press(window, key, physical, RawInputModifiers.Control);

    private static void Press(MainWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    /// <summary>Opens a WinForms designer in the window, with both buttons selected through Edit &gt; Select All.</summary>
    private async Task<WinFormsDesignSurface> OpenDesignerWithBothSelectedAsync(TestWindow host)
    {
        var formsPath = Path.Combine(_root, "MainForm.Designer.vb");
        await File.WriteAllTextAsync(formsPath, TwoButtons);
        var model = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
        await model.OpenFileAsync(formsPath, inDesigner: true);
        await host.SettleAsync();

        var formsHost = Assert.Single(host.Window.GetVisualDescendants().OfType<DesignerHost>());
        var surface = Assert.Single(formsHost.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null, "The designer did not load.");

        UserJourneyItemsTests.InvokeMenu(host.Window, Localizer.Get(StringKeys.MenuEdit), Localizer.Get(StringKeys.MenuEditSelectAll));
        Assert.Equal(2, surface.SelectedControls.Count);
        return surface;
    }

    /// <summary>With the designer focused, Ctrl+C copies the selected controls, as Edit &gt; Copy does.</summary>
    [AvaloniaFact]
    public async Task CtrlCWithTheDesignerFocusedCopiesTheControls()
    {
        using var host = new TestWindow();
        var surface = await OpenDesignerWithBothSelectedAsync(host);

        surface.Focus();
        await host.SettleAsync(3);
        PressCtrl(host.Window, Key.C, PhysicalKey.C);

        await WaitUntilAsync(() => WinFormsDesignerView.ClipboardForTests is not null,
            "Ctrl+C with the designer focused did not copy the controls.");
    }

    /// <summary>
    /// A designer showing beside a focused terminal: Ctrl+C is the shell's
    /// interrupt, so the designer's clipboard and selection are left alone.
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlCInTheTerminalLeavesTheDesignerAloneWhileItIsShowing()
    {
        using var host = new TestWindow();
        var surface = await OpenDesignerWithBothSelectedAsync(host);

        var terminal = await EditorShortcutTests.OpenTerminalAsync(host);
        await WaitUntilAsync(() => terminal.ScreenText.Trim().Length > 0, "The shell printed no prompt.");

        // A command that answers shows the shell is reading the line editor, so the interrupt has a reader.
        host.Window.KeyTextInput("echo ready$((1+1))");
        Press(host.Window, Key.Enter, PhysicalKey.Enter);
        await WaitUntilAsync(() => terminal.ScreenText.Split('\n').Any(line => line.TrimEnd() == "ready2"),
            "The shell did not answer a command.");

        // A running program gives the interrupt something to stop; the terminal echoes it as ^C.
        host.Window.KeyTextInput("sleep 30");
        Press(host.Window, Key.Enter, PhysicalKey.Enter);
        await host.SettleAsync(20);

        PressCtrl(host.Window, Key.C, PhysicalKey.C);

        await WaitUntilAsync(() => terminal.ScreenText.Contains("^C", StringComparison.Ordinal),
            "Ctrl+C did not reach the terminal while a designer was showing. Focus: " +
            (host.Window.FocusManager?.GetFocusedElement()?.GetType().Name ?? "none") +
            ". Screen: " + terminal.ScreenText.Replace("\n", " | ", StringComparison.Ordinal));
        Assert.Null(WinFormsDesignerView.ClipboardForTests);
        Assert.Equal(2, surface.SelectedControls.Count);
    }
}
