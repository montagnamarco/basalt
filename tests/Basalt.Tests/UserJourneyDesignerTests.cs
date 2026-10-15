using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Drives the WinForms designer the way a real user does: a real VB WinForms project on
/// disk, opened in a real <see cref="MainWindow"/> through <see cref="TestWindow"/>, with
/// real headless pointer gestures (MouseDown/MouseMove/MouseUp) and real keyboard input
/// (KeyPress) on the design surface and the Properties grid - not direct method calls or
/// events raised on a control in isolation, which the many other <c>WinFormsDesigner*</c>
/// test files already cover unit by unit.
///
/// Shares the clipboard collection with <see cref="WinFormsDesignerClipboardInteractionTests"/>
/// because Cut/Copy/Paste here touches the same process-wide clipboard state.
/// </summary>
[Collection(nameof(WinFormsDesignerClipboardCollection))]
public sealed class UserJourneyDesignerTests : IDisposable
{
    public UserJourneyDesignerTests() => WinFormsDesignerView.ResetClipboardForTests();
    public void Dispose() => WinFormsDesignerView.ResetClipboardForTests();

    // A richer fixture than the other end-to-end tests use: four controls, spaced apart
    // enough that a rubber band, a multi-select, or a resize of one never brushes another.
    //   Button1:     (40,40)   90x30  -> (130,70)
    //   Label1:      (40,100)  90x23  -> (130,123)
    //   PictureBox1: (40,160)  60x60  -> (100,220)
    //   ListBox1:    (200,40) 120x95  -> (320,135)
    private const string DesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.Button1.Location = New System.Drawing.Point(40, 40)
                Me.Button1.Size = New System.Drawing.Size(90, 30)
                Me.Button1.Text = "Button1"
                Me.Controls.Add(Me.Button1)
                Me.Label1 = New System.Windows.Forms.Label()
                Me.Label1.Location = New System.Drawing.Point(40, 100)
                Me.Label1.Size = New System.Drawing.Size(90, 23)
                Me.Label1.Text = "Label1"
                Me.Controls.Add(Me.Label1)
                Me.PictureBox1 = New System.Windows.Forms.PictureBox()
                Me.PictureBox1.Location = New System.Drawing.Point(40, 160)
                Me.PictureBox1.Size = New System.Drawing.Size(60, 60)
                Me.Controls.Add(Me.PictureBox1)
                Me.ListBox1 = New System.Windows.Forms.ListBox()
                Me.ListBox1.Location = New System.Drawing.Point(200, 40)
                Me.ListBox1.Size = New System.Drawing.Size(120, 95)
                Me.ListBox1.Items.Add("Alpha")
                Me.Controls.Add(Me.ListBox1)
                Me.ClientSize = New System.Drawing.Size(500, 400)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub

            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Label1 As System.Windows.Forms.Label
            Friend WithEvents PictureBox1 As System.Windows.Forms.PictureBox
            Friend WithEvents ListBox1 As System.Windows.Forms.ListBox
        End Class
        """;

    private const string CodeSource = """
        Partial Class Form1
        End Class
        """;

    // An empty form, for the toolbox-drop journeys: nothing already on it to collide
    // with whatever lands at the fixed drop point every case below shares.
    private const string EmptyDesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.ClientSize = New System.Drawing.Size(500, 400)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub
        End Class
        """;

    private sealed class Fixture : IDisposable
    {
        public required TestWindow Host { get; init; }
        public required MainWindowViewModel Vm { get; init; }
        public required WinFormsDesignSurface Surface { get; init; }
        public required Canvas Canvas { get; init; }
        public required string DesignerPath { get; init; }
        public required string CodePath { get; init; }
        public required string Root { get; init; }

        /// <summary>
        /// Window coordinates for a point expressed in the root form's client
        /// coordinates - 12 for the surface's gutter, 4/23 for Form1's own default
        /// frame (Sizable border, captioned since it has a non-empty Text), the same
        /// offsets <c>ScreenshotProbe</c> and the other end-to-end designer tests use.
        /// </summary>
        public Point ClientPoint(double x, double y) =>
            Canvas.TranslatePoint(new Point(12 + 4 + x, 12 + 23 + y), Host.Window)!.Value;

        public string DesignerText => Vm.OpenDocuments.Single(d => d.FilePath == DesignerPath).Text;

        public void Dispose()
        {
            Host.Dispose();
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }

    private static async Task<Fixture> OpenAsync(string? designerSource = null, string? codeSource = null)
    {
        // No .vbproj: TestWindow(project) makes MainWindow evaluate a real MSBuild
        // project, which WinFormsEventsPanelEndToEndTests and
        // WinFormsPropertyGridEllipsisPointerTests both avoid by opening a loose
        // Designer.vb/.vb pair through a solution-less TestWindow() instead - the
        // same choice here, after a project-backed version of this file left every
        // test waiting on a WinFormsDesignSurface that never appeared within 20s.
        var root = Directory.CreateTempSubdirectory("basalt-journey-designer-").FullName;
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        var codePath = Path.Combine(root, "Form1.vb");
        await File.WriteAllTextAsync(designerPath, designerSource ?? DesignerSource);
        await File.WriteAllTextAsync(codePath, codeSource ?? CodeSource);

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await host.SettleAsync(10);

        var surface = Assert.Single(host.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null);

        var canvas = (Canvas)typeof(WinFormsDesignSurface)
            .GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(surface)!;

        return new Fixture
        {
            Host = host, Vm = vm, Surface = surface, Canvas = canvas,
            DesignerPath = designerPath, CodePath = codePath, Root = root,
        };
    }

    // ------------------------------------------------------------------
    // 1. Rubber-band multi-select: a real pointer drag from the form's own
    //    empty background, over several controls, released without ever
    //    touching one. Regression test for WinFormsDesignSurface.RubberBand.cs:
    //    before that fix, a drag started on the form's background produced no
    //    gesture at all (OnPointerPressed created one only for a hit other than
    //    the root form), so nothing was ever selected.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task RubberBandDragSelectsEveryControlItWhollyCoversAndATooSmallDragJustSelectsTheForm()
    {
        // One window carries both checks: opening a full MainWindow (terminal panels
        // included - see TestWindow's own remarks) for every single assertion in this
        // file made the whole suite pile up outstanding shell processes and stall, so
        // journeys that do not disturb each other's state share one fixture here.
        using var f = await OpenAsync();

        // Far from where the band starts below: two presses close together in time and place
        // are a double-click, which opens the form's Load handler in code, as in Visual Studio.
        var at = f.ClientPoint(400, 300);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // A click too small to count as a drag is a plain click on the form's own
        // background: it selects the form, the same as before the rubber band existed.
        Assert.True(ReferenceEquals(f.Surface.SelectedControl, f.Surface.RootForm));

        // From (10,10) to (170,135): wholly covers Button1 (40,40-130,70) and Label1
        // (40,100-130,123), but not PictureBox1 (starts at y=160) or ListBox1 (starts
        // at x=200) - a band that catches some controls and knowingly misses others.
        var from = f.ClientPoint(10, 10);
        var to = f.ClientPoint(170, 135);

        f.Host.Window.MouseDown(from, MouseButton.Left);
        f.Host.Window.MouseMove(from + new Vector(6, 6));
        f.Host.Window.MouseMove(to);
        f.Host.Window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            new[] { "Button1", "Label1" },
            f.Surface.SelectedControls.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task CtrlDraggingABandFromTheFormAddsWhatItSweepsToTheSelection()
    {
        using var f = await OpenAsync();

        var listBox = f.ClientPoint(250, 60);
        f.Host.Window.MouseDown(listBox, MouseButton.Left);
        f.Host.Window.MouseUp(listBox, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ListBox1", f.Surface.SelectedControl?.Name);

        var from = f.ClientPoint(10, 10);
        var to = f.ClientPoint(170, 135);
        f.Host.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        f.Host.Window.MouseMove(from + new Vector(6, 6), RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
        f.Host.Window.MouseMove(to, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
        f.Host.Window.MouseUp(to, MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            new[] { "Button1", "Label1", "ListBox1" },
            f.Surface.SelectedControls.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task ABandOnTheFormDoesNotPickAControlOutOfItsContainer()
    {
        const string nested = """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Private Sub InitializeComponent()
                    Me.Panel1 = New System.Windows.Forms.Panel()
                    Me.Button2 = New System.Windows.Forms.Button()
                    Me.Panel1.Location = New System.Drawing.Point(250, 200)
                    Me.Panel1.Size = New System.Drawing.Size(200, 150)
                    Me.Button2.Location = New System.Drawing.Point(10, 10)
                    Me.Button2.Size = New System.Drawing.Size(50, 20)
                    Me.Panel1.Controls.Add(Me.Button2)
                    Me.Controls.Add(Me.Panel1)
                    Me.ClientSize = New System.Drawing.Size(500, 400)
                    Me.Text = "Form1"
                    Me.ResumeLayout(False)
                End Sub

                Friend WithEvents Panel1 As System.Windows.Forms.Panel
                Friend WithEvents Button2 As System.Windows.Forms.Button
            End Class
            """;
        using var f = await OpenAsync(nested, CodeSource);

        // Wholly covers Button2 (260,210)-(310,230) inside Panel1, but not Panel1 itself.
        var from = f.ClientPoint(240, 190);
        var to = f.ClientPoint(330, 250);
        f.Host.Window.MouseDown(from, MouseButton.Left);
        f.Host.Window.MouseMove(from + new Vector(6, 6));
        f.Host.Window.MouseMove(to);
        f.Host.Window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(f.Surface.SelectedControls, control => control.Name == "Button2");
    }

    // ------------------------------------------------------------------
    // 2. Dragging every common Toolbox control onto the form - one window,
    //    every type in turn, for the same resource-pressure reason above.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DraggingEachCommonToolboxControlOntoTheFormAddsIt()
    {
        Type[] types =
        [
            typeof(Forms.Button), typeof(Forms.Label), typeof(Forms.TextBox), typeof(Forms.CheckBox),
            typeof(Forms.RadioButton), typeof(Forms.ComboBox), typeof(Forms.ListBox), typeof(Forms.PictureBox),
            typeof(Forms.Panel), typeof(Forms.GroupBox), typeof(Forms.TabControl), typeof(Forms.DataGridView),
            typeof(Forms.MenuStrip), typeof(Forms.ToolStrip), typeof(Forms.StatusStrip),
            typeof(Forms.Timer), typeof(Forms.OpenFileDialog), typeof(Forms.SaveFileDialog),
            typeof(Forms.FolderBrowserDialog), typeof(Forms.ColorDialog), typeof(Forms.FontDialog),
        ];

        // Spread across the empty 500x400 form. The containers start at Visual Studio's sizes (a Panel is 200 by 100),
        // so a later drop can land inside an earlier container and go into it, as it does in Visual Studio: the
        // control is looked for anywhere in the form, not only among the form's own children.
        using var f = await OpenAsync(EmptyDesignerSource, CodeSource);
        var column = 0;
        var row = 0;
        foreach (var controlType in types)
        {
            var position = f.ClientPoint(40 + column * 110, 30 + row * 60);
            column++;
            if (column == 4) { column = 0; row++; }

            var transfer = Transfer(controlType);
            var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, f.Host.Window, position, KeyModifiers.None);
            f.Surface.RaiseEvent(over);
            Assert.Equal(DragDropEffects.Copy, over.DragEffects);

            var drop = new DragEventArgs(DragDrop.DropEvent, transfer, f.Host.Window, position, KeyModifiers.None);
            f.Surface.RaiseEvent(drop);
            Dispatcher.UIThread.RunJobs();

            if (typeof(Forms.Control).IsAssignableFrom(controlType))
            {
                await WaitUntilAsync(() => AllControls(f.Surface.RootForm!)
                    .Any(control => control.GetType() == controlType), controlType.Name + " was not added.");
            }
            else
            {
                await WaitUntilAsync(() => new WinFormsDesignerDocument(f.DesignerText).Components
                    .Any(component => component.TypeName.EndsWith(controlType.Name, StringComparison.Ordinal)), controlType.Name + " was not added to the tray.");
            }
        }
    }

    /// <summary>Every control in the form, at any depth.</summary>
    private static IEnumerable<Forms.Control> AllControls(Forms.Control parent) =>
        parent.Controls.Cast<Forms.Control>().SelectMany(child => AllControls(child).Prepend(child));

    private static DataTransfer Transfer(Type controlType)
    {
        var item = new DataTransferItem();
        item.Set(WinFormsToolboxDrag.Format, new WinFormsToolboxItem(controlType));
        item.SetText(controlType.Name);
        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    // ------------------------------------------------------------------
    // 3. Double-clicking a Toolbox item adds the control without any drag.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DoubleClickingAToolboxItemAddsAControl()
    {
        using var f = await OpenAsync(EmptyDesignerSource, CodeSource);

        var list = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<ListBox>(),
            box => box.ItemsSource is not null && box.ItemsSource.Cast<object>()
                .OfType<WinFormsToolboxItem>().Any(item => item.DisplayName == "TextBox"));
        var row = list.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => item.DataContext is WinFormsToolboxItem toolbox && toolbox.DisplayName == "TextBox");
        row.BringIntoView();
        f.Host.Window.UpdateLayout();
        var point = Raster.BoundsIn(row, f.Host.Window);
        var at = new Point(point.X + 10, point.Y + point.Height / 2.0);

        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        await WaitUntilAsync(() => f.Surface.RootForm!.Controls.OfType<Forms.TextBox>().Any());
    }

    // ------------------------------------------------------------------
    // 4. Move via a plain pointer drag, resize via a corner grip, then nudge
    //    with the arrow keys - one Button1, three gestures, one window.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DraggingMovesResizesAndArrowKeysNudgeTheSelectedControl()
    {
        using var f = await OpenAsync();

        var inside = f.ClientPoint(40 + 10, 40 + 10); // inside Button1, away from any handle.
        f.Host.Window.MouseDown(inside, MouseButton.Left);
        f.Host.Window.MouseUp(inside, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Button1", f.Surface.SelectedControl?.Name);

        // A press right after the click, in the same place, is a double-click, which opens
        // Button1's Click handler in code as in Visual Studio; a user pauses before dragging.
        await PauseLongerThanADoubleClickAsync();

        var target = inside + new Vector(30, 20);
        f.Host.Window.MouseDown(inside, MouseButton.Left);
        f.Host.Window.MouseMove(inside + new Vector(4, 4));
        f.Host.Window.MouseMove(target);
        f.Host.Window.MouseUp(target, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        await WaitUntilAsync(() => f.Surface.SelectedControl!.Location == new System.Drawing.Point(70, 60));
        await WaitUntilAsync(() => f.DesignerText.Contains(
            "Me.Button1.Location = New System.Drawing.Point(70, 60)", StringComparison.Ordinal));

        // Button1 is now (70,60)-(160,90): its bottom-right corner, where the grip sits.
        var grip = f.ClientPoint(160, 90);
        var resizeTarget = grip + new Vector(30, 30);
        // Alt held, as in Visual Studio, so the snaplines do not pull the new bottom edge
        // (y=120) onto Label1's bottom three pixels below it.
        f.Host.Window.MouseDown(grip, MouseButton.Left, RawInputModifiers.Alt);
        f.Host.Window.MouseMove(grip + new Vector(4, 4), RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        f.Host.Window.MouseMove(resizeTarget, RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        f.Host.Window.MouseUp(resizeTarget, MouseButton.Left, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();

        await WaitUntilAsync(() => f.Surface.SelectedControl!.Size == new System.Drawing.Size(120, 60));
        await WaitUntilAsync(() => f.DesignerText.Contains(
            "Me.Button1.Size = New System.Drawing.Size(120, 60)", StringComparison.Ordinal));

        // One key at a time, as a person presses them: each nudge is written to the designer file
        // before the next one is taken.
        f.Host.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        await WaitUntilAsync(() => f.DesignerText.Contains(
            "Me.Button1.Location = New System.Drawing.Point(71, 60)", StringComparison.Ordinal), "Right did not nudge Button1.");
        await f.Host.SettleAsync(1);
        f.Host.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        await WaitUntilAsync(() => f.Surface.SelectedControl!.Location == new System.Drawing.Point(71, 61), "Down did not nudge Button1.");
    }

    // ------------------------------------------------------------------
    // 5. Ctrl+click multi-select, then Delete removes both, then Undo
    //    brings them back.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CtrlClickSelectsBothAndDeleteThenUndoRemovesAndRestoresThem()
    {
        using var f = await OpenAsync();

        var button = f.ClientPoint(40 + 10, 40 + 10);
        var label = f.ClientPoint(40 + 10, 100 + 10);
        f.Host.Window.MouseDown(button, MouseButton.Left);
        f.Host.Window.MouseUp(button, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Button1", f.Surface.SelectedControl?.Name);

        f.Host.Window.MouseDown(label, MouseButton.Left, RawInputModifiers.Control);
        f.Host.Window.MouseUp(label, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            new[] { "Button1", "Label1" },
            f.Surface.SelectedControls.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));

        f.Host.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await WaitUntilAsync(() => !f.Surface.RootForm!.Controls.OfType<Forms.Button>().Any()
            && !f.Surface.RootForm!.Controls.OfType<Forms.Label>().Any());

        ClickHistory(f, StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => f.Surface.RootForm!.Controls.OfType<Forms.Button>().Any()
            && f.Surface.RootForm!.Controls.OfType<Forms.Label>().Any());
    }

    // ------------------------------------------------------------------
    // 7. Copy and paste with real keyboard shortcuts, then undo.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CopyAndPasteDuplicateTheSelectedControlAndUndoRemovesTheCopy()
    {
        using var f = await OpenAsync();

        var at = f.ClientPoint(50, 50);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Button1", f.Surface.SelectedControl?.Name);

        f.Host.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        await WaitUntilAsync(() => f.Surface.CanPasteControls?.Invoke() == true);

        var before = f.Surface.RootForm!.Controls.OfType<Forms.Button>().Count();
        f.Host.Window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
        await WaitUntilAsync(() => f.Surface.RootForm!.Controls.OfType<Forms.Button>().Count() == before + 1);

        var pasted = f.Surface.SelectedControl!;
        Assert.NotEqual("Button1", pasted.Name);

        ClickHistory(f, StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => f.Surface.RootForm!.Controls.OfType<Forms.Button>().Count() == before);
    }

    // ------------------------------------------------------------------
    // 8. Properties: Text, the Image editor's ellipsis, the Items collection
    //    editor's ellipsis and the Enabled check box. Name is left to
    //    WinFormsDesignerRenameViewTests: renaming updates the code's references
    //    too, which needs the solution this loose-file fixture does not open.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EditingTextImageItemsAndTheEnabledCheckBoxAllUpdateTheDesignerFile()
    {
        using var f = await OpenAsync();
        f.Surface.Select(f.Surface.RootForm!.Controls.OfType<Forms.Button>().Single());

        var grid = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<PropertyGrid>());

        var textEditor = Assert.Single(FindRow(grid, "Text").GetVisualDescendants().OfType<TextBox>(),
            box => box.Name == "PropertyGridValue");
        textEditor.Text = "Save";
        textEditor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await WaitUntilAsync(() => f.DesignerText.Contains(
            "Me.Button1.Text = \"Save\"", StringComparison.Ordinal));

        // Image ellipsis, on a PictureBox.
        f.Surface.Select(f.Surface.RootForm!.Controls.OfType<Forms.PictureBox>().Single());
        grid = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<PropertyGrid>());
        var imageRow = await WaitForRowAsync(grid, "Image");
        ScrollIntoView(f.Host, imageRow);
        ClickEllipsis(f.Host, imageRow);
        await WaitUntilAsync(() => f.Host.Window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
        f.Host.Window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single().Close();
        await f.Host.SettleAsync(2);

        // Items ellipsis, on a ListBox.
        f.Surface.Select(f.Surface.RootForm!.Controls.OfType<Forms.ListBox>().Single());
        grid = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<PropertyGrid>());
        var itemsRow = await WaitForRowAsync(grid, "Items");
        ScrollIntoView(f.Host, itemsRow);
        ClickEllipsis(f.Host, itemsRow);
        await WaitUntilAsync(() => f.Host.Window.OwnedWindows.OfType<WinFormsCollectionEditor>().Any());
        f.Host.Window.OwnedWindows.OfType<WinFormsCollectionEditor>().Single().Close();
        await f.Host.SettleAsync(2);

        // Enabled, back on the button: a True/False drop-down as in Visual Studio's grid, opened
        // with a real click and set to False from the keyboard.
        f.Surface.Select(f.Surface.RootForm!.Controls.OfType<Forms.Button>().Single());
        Assert.True(f.Surface.SelectedControl!.Enabled);
        grid = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<PropertyGrid>());
        var enabledRow = FindRow(grid, "Enabled");
        ScrollIntoView(f.Host, enabledRow);
        var choice = Assert.Single(enabledRow.GetVisualDescendants().OfType<ComboBox>());
        var bounds = Raster.BoundsIn(choice, f.Host.Window);
        var centre = new Point(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
        f.Host.Window.MouseDown(centre, MouseButton.Left);
        f.Host.Window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(choice.IsDropDownOpen, "clicking the Enabled value did not open its True/False list.");
        f.Host.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        f.Host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        await WaitUntilAsync(() => !f.Surface.RootForm!.Controls.OfType<Forms.Button>().Single().Enabled);
        Assert.Contains("Me.Button1.Enabled = False", f.DesignerText, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // 10. Events tab: double-clicking the Click row creates a handler and
    //     navigates to it.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DoubleClickingAnEventRowCreatesTheHandlerAndNavigatesToIt()
    {
        using var f = await OpenAsync();

        var at = f.ClientPoint(50, 50);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Button1", f.Surface.SelectedControl?.Name);

        var properties = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsPropertyPanel>());
        var lightning = Assert.Single(properties.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "PropertyGridEvents");
        lightning.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await f.Host.SettleAsync(3);

        var events = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsEventPanel>());
        var clickRow = (Grid)events.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "Click").GetVisualParent()!;
        var bounds = Raster.BoundsIn(clickRow, f.Host.Window);
        var spot = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);

        f.Host.Window.MouseDown(spot, MouseButton.Left);
        f.Host.Window.MouseUp(spot, MouseButton.Left);
        f.Host.Window.MouseDown(spot, MouseButton.Left);
        f.Host.Window.MouseUp(spot, MouseButton.Left);

        await WaitUntilAsync(() =>
            f.Vm.OpenDocuments.FirstOrDefault(d => d.FilePath == f.CodePath)?.Text
                .Contains("Button1_Click", StringComparison.Ordinal) == true);
        await WaitUntilAsync(() => f.Vm.ActiveDocument?.FilePath == f.CodePath);
        var code = f.Vm.OpenDocuments.Single(d => d.FilePath == f.CodePath);
        Assert.Contains("Handles Button1.Click", code.Text);

        // Ready to type the handler's body, as in Visual Studio: the keyboard is in the code, not
        // back on the Events row that was double-clicked.
        await f.Host.SettleAsync(2);
        var focused = f.Host.Window.FocusManager?.GetFocusedElement() as Visual;
        Assert.True(focused is AvaloniaEdit.Editing.TextArea
            || focused?.GetVisualAncestors().OfType<AvaloniaEdit.Editing.TextArea>().Any() == true,
            $"focus is on {focused?.GetType().Name} rather than in the code editor.");
    }

    // ------------------------------------------------------------------
    // 11. F7/Shift+F7 switch between the Designer and its code-behind.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task F7ShowsCodeAndShiftF7ShowsTheDesignerAgain()
    {
        using var f = await OpenAsync();
        Assert.Equal(f.DesignerPath, f.Vm.ActiveDocument?.FilePath);

        f.Host.Window.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null);
        await WaitUntilAsync(() => f.Vm.ActiveDocument?.FilePath == f.CodePath);

        f.Host.Window.KeyPress(Key.F7, RawInputModifiers.Shift, PhysicalKey.F7, null);
        await WaitUntilAsync(() => f.Vm.ActiveDocument?.FilePath == f.DesignerPath);
    }

    // ------------------------------------------------------------------
    // 12. Save, then reopen the file from disk: round trips byte for byte.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task SavingAndReopeningTheDesignerFileRoundTrips()
    {
        using var f = await OpenAsync();

        // Edit once, so the saved file is not simply the one already on disk.
        var at = f.ClientPoint(50, 50);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        f.Host.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        await WaitUntilAsync(() => f.Surface.SelectedControl!.Left == 41);
        // The nudge moves the control at once and reaches the designer file a moment later.
        await WaitUntilAsync(() => f.DesignerText.Contains(
            "Me.Button1.Location = New System.Drawing.Point(41, 40)", StringComparison.Ordinal));

        await f.Vm.SaveCommand.ExecuteAsync(null);
        var saved = await File.ReadAllTextAsync(f.DesignerPath);
        Assert.Contains("Me.Button1.Location = New System.Drawing.Point(41, 40)", saved);

        f.Vm.CloseActiveDocument();
        await f.Host.SettleAsync(3);
        await f.Vm.OpenFileAsync(f.DesignerPath, inDesigner: true);
        await f.Host.SettleAsync(5);

        var reopened = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => reopened.RootForm is not null);
        Assert.Equal(saved, f.Vm.OpenDocuments.Single(d => d.FilePath == f.DesignerPath).Text);
        Assert.Equal(41, reopened.RootForm!.Controls.OfType<Forms.Button>().Single().Left);
    }

    // ------------------------------------------------------------------
    // Shared helpers.
    // ------------------------------------------------------------------

    private static void ClickHistory(Fixture f, string key) => f.Host.Window.GetVisualDescendants()
        .OfType<Button>().First(button => Equals(button.Content, Localizer.Get(key)))
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Border FindRow(PropertyGrid grid, string label) =>
        Assert.Single(grid.Rows.OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == label));

    private static async Task<Border> WaitForRowAsync(PropertyGrid grid, string label)
    {
        await WaitUntilAsync(() => grid.Rows.OfType<Border>().Any(row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == label)));
        return FindRow(grid, label);
    }

    /// <summary>
    /// Scrolls the Properties window to a row with the mouse wheel, as a user reaches a property
    /// below the fold.
    /// </summary>
    private static void ScrollIntoView(TestWindow window, Control row)
    {
        window.Window.UpdateLayout();
        var scroll = row.GetVisualAncestors().OfType<ScrollViewer>().First();
        var viewport = Raster.BoundsIn(scroll, window.Window);
        var over = new Point(viewport.X + viewport.Width / 2.0, viewport.Y + viewport.Height / 2.0);
        for (var notch = 0; notch < 40; notch++)
        {
            var at = Raster.BoundsIn(row, window.Window);
            if (at.Y >= viewport.Y && at.Bottom <= viewport.Bottom) break;

            // Wheel down to reach a row below the fold, up for one scrolled past.
            window.Window.MouseWheel(over, new Vector(0, at.Bottom > viewport.Bottom ? -1 : 1));
            window.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        var bounds = Raster.BoundsIn(row, window.Window);
        Assert.True(bounds.Y >= viewport.Y && bounds.Bottom <= viewport.Bottom, $"the row is still off screen at {bounds}.");
    }

    private static void ClickEllipsis(TestWindow window, Border row)
    {
        var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
        var bounds = Raster.BoundsIn(ellipsis, window.Window);
        var centre = new Point(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
        window.Window.MouseDown(centre, MouseButton.Left);
        window.Window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Task PauseLongerThanADoubleClickAsync() => Task.Delay(700);

    private static async Task WaitUntilAsync(Func<bool> condition, string? what = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not reach the expected state in time" + (what is null ? "." : ": " + what));
    }
}
