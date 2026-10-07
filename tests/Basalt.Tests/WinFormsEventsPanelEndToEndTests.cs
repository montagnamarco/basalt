using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Drives the WinForms Events view of the Properties panel the way a user
/// would: a real <see cref="MainWindow"/> opened through
/// <see cref="MainWindowViewModel.OpenFileAsync"/>, a real pointer click to
/// select a control on the design surface, and the Properties panel's own
/// lightning toggle to switch to Events — not <see cref="WinFormsEventPanel"/>
/// driven directly, which <c>WinFormsEventPanelTests</c> already covers.
///
/// See docs/planning/PANELS-AUDIT.md, session 3 item 9: a headless probe
/// once reported 0 event rows for a selected Button even though the panel's
/// own mechanics were proven in isolation. This test settles that question
/// for the full path.
/// </summary>
public sealed class WinFormsEventsPanelEndToEndTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-winforms-events-e2e-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    // A minimal, realistic split: WithEvents declared in the Designer file,
    // handlers written into the code-behind — the same split a real "Add
    // Windows Form" template produces. Button1 has no handler yet so the
    // first double-click is proved to create one, not merely navigate to an
    // existing one (WinFormsDesignerDefaultEventTests already covers that
    // case for the surface's own default-event gesture).
    private const string DesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.Button1.Location = New System.Drawing.Point(40, 40)
                Me.Button1.Size = New System.Drawing.Size(90, 30)
                Me.Button1.Text = "Button1"
                Me.Controls.Add(Me.Button1)
                Me.ClientSize = New System.Drawing.Size(300, 300)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub

            Friend WithEvents Button1 As System.Windows.Forms.Button
        End Class
        """;

    // A handler that already exists before any designer edit, so step 3
    // (typing an existing method name into another event row) can prove a
    // *bind*, not a creation.
    private const string CodeSource = """
        Partial Class Form1
            Private Sub SharedHandler(sender As Object, e As System.EventArgs)
            End Sub
        End Class
        """;

    [AvaloniaFact]
    public async Task DoubleClickCreatesAHandlerAndTypingAnExistingNameBindsItBothUndoably()
    {
        var designerPath = Path.Combine(_root, "Form1.Designer.vb");
        var codePath = Path.Combine(_root, "Form1.vb");
        await File.WriteAllTextAsync(designerPath, DesignerSource);
        await File.WriteAllTextAsync(codePath, CodeSource);

        using var window = new TestWindow();
        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await window.SettleAsync();

        var surface = Assert.Single(window.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null);

        // 1. A real pointer click selects Button1 on the design surface.
        var canvas = (Canvas)typeof(WinFormsDesignSurface)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(surface)!;
        // 12: the surface's gutter. 4, 23: Form1's own default frame
        // (Sizable border, captioned since it has a non-empty Text) - its
        // client area starts that far past the gutter, with Button1 at
        // (40,40)-(130,70) (mirrors ScreenshotProbe.OpenWinFormsDesignerAsync,
        // which clicks the same fixture's Button1 the same way).
        var point = canvas.TranslatePoint(new Point(12 + 4 + 50, 12 + 23 + 50), window.Window)!.Value;
        window.Window.MouseDown(point, MouseButton.Left);
        window.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Button1", surface.SelectedControl?.Name);

        // Switch the Properties panel to Events with its own lightning
        // toggle — the same control a user clicks, not a direct call into
        // WinFormsEventPanel.
        var properties = Assert.Single(window.Window.GetVisualDescendants().OfType<WinFormsPropertyPanel>());
        var lightning = Assert.Single(properties.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "PropertyGridEvents");
        lightning.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await window.SettleAsync();

        var events = Assert.Single(window.Window.GetVisualDescendants().OfType<WinFormsEventPanel>());

        // 1. Event rows are listed for the selected Button.
        var labels = events.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains("Click", labels);
        Assert.Contains("MouseDown", labels);

        // 2. Double-clicking the Click row creates the handler in the
        // code-behind, opens/activates its editor with the caret inside the
        // new method, and the row now shows the generated name.
        var clickRow = (Grid)events.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "Click").GetVisualParent()!;
        var rowBounds = Raster.BoundsIn(clickRow, window.Window);
        var rowSpot = new Point(rowBounds.X + 10, rowBounds.Y + rowBounds.Height / 2.0);
        window.Window.MouseDown(rowSpot, MouseButton.Left);
        window.Window.MouseUp(rowSpot, MouseButton.Left);
        window.Window.MouseDown(rowSpot, MouseButton.Left);
        window.Window.MouseUp(rowSpot, MouseButton.Left);

        var code = await WaitForDocumentAsync(vm, codePath, text => text.Contains("Button1_Click", StringComparison.Ordinal));
        Assert.Contains("Handles Button1.Click", code.Text);
        var createdText = code.Text;

        Assert.Equal(codePath, vm.ActiveDocument?.FilePath);
        var codeEditor = Assert.Single(window.Window.GetVisualDescendants().OfType<CodeEditor>(),
            editor => editor.FilePath == codePath);
        var expectedCaret = WinFormsEventHandlers.HandlerOffset(createdText, "Form1", "Button1_Click");
        await WaitUntilAsync(() => codeEditor.CaretOffsetForTests == expectedCaret);

        // The Events tool dock's content tracks the active *document*
        // (MainWindow.DesignerToolDocks.cs) and goes blank while the code
        // tab is in front — the same thing Visual Studio does switching a
        // form's Designer view to its Code view. Returning to the Designer
        // tab (the same OpenFileAsync a user takes clicking that tab) is
        // what brings the panel, now showing the generated name, back.
        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await window.SettleAsync();

        events = Assert.Single(window.Window.GetVisualDescendants().OfType<WinFormsEventPanel>());
        clickRow = (Grid)events.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "Click").GetVisualParent()!;
        var clickEditor = Assert.Single(clickRow.GetVisualDescendants().OfType<TextBox>());
        Assert.Equal("Button1_Click", clickEditor.Text);

        // 3. Typing an existing method name into another event row binds it:
        // the Handles clause gains the event, as one undo step.
        var mouseDownRow = (Grid)events.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "MouseDown").GetVisualParent()!;
        var mouseDownEditor = Assert.Single(mouseDownRow.GetVisualDescendants().OfType<TextBox>());
        mouseDownEditor.Focus();
        mouseDownEditor.Text = "SharedHandler";
        mouseDownEditor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        code = await WaitForDocumentAsync(vm, codePath, text => text.Contains("Handles Button1.MouseDown", StringComparison.Ordinal));
        Assert.Contains("Handles Button1.MouseDown", code.Text);

        // Back to the Designer tab again: the Undo button lives in the
        // design surface's own toolbar, not the code editor's.
        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await window.SettleAsync();
        var designerView = Assert.Single(window.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());

        void ClickUndo()
        {
            var undo = Assert.Single(designerView.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        // One undo step removes exactly the MouseDown binding, back to the
        // state right after the handler was created.
        ClickUndo();
        await WaitUntilAsync(() => (vm.OpenDocuments.Single(d => d.FilePath == codePath)).Text == createdText);

        // 4. A second undo step removes the whole generated handler: the
        // method and its Handles clause disappear in one step, matching
        // Visual Studio (undoing a designer-generated stub is one action,
        // not one per line it inserted).
        ClickUndo();
        await WaitUntilAsync(() => (vm.OpenDocuments.Single(d => d.FilePath == codePath)).Text == CodeSource);
    }

    private static async Task<EditorDocumentViewModel> WaitForDocumentAsync(
        MainWindowViewModel vm, string path, Func<string, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            var document = vm.OpenDocuments.FirstOrDefault(d => d.FilePath == path);
            if (document is not null && ready(document.Text)) return document;
            await Task.Delay(10);
        }

        Assert.Fail($"The code-behind at {path} never reached the expected state.");
        throw new InvalidOperationException();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
