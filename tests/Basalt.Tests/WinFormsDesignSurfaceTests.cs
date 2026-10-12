using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

public sealed class WinFormsDesignSurfaceTests
{
    private static (Window Window, WinFormsDesignSurface Surface, Forms.Form Form, Forms.Panel Panel)
        Open()
    {
        var form = new Forms.Form
        {
            ClientSize = new Drawing.Size(220, 160),
            BackColor = Drawing.Color.White,
            // This fixture is about hit-testing, dragging and resizing children,
            // not the root form's own frame (covered by WinFormsDesignerFrameTests):
            // None keeps every "OnForm" pixel exactly where it always was.
            FormBorderStyle = Forms.FormBorderStyle.None,
        };
        var panel = new Forms.Panel
        {
            Bounds = new Drawing.Rectangle(35, 30, 80, 50),
            BackColor = Drawing.Color.Red,
        };
        form.Controls.Add(panel);

        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Width = 320, Height = 250, Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, surface, form, panel);
    }

    private static Point OnForm(double x, double y) => new(x + 12, y + 12);

    [AvaloniaFact]
    public void NestedControlCanBeSelectedWithoutRunningItsClickHandler()
    {
        var (window, surface, _, panel) = Open();
        var button = new Forms.Button
        {
            Bounds = new Drawing.Rectangle(10, 10, 40, 25),
            Text = "Run",
        };
        var clicked = 0;
        button.Click += (_, _) => clicked++;
        panel.Controls.Add(button);
        surface.RefreshForm();

        var at = OnForm(35 + 10 + 20, 30 + 10 + 12);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(button, surface.SelectedControl);
        Assert.Equal(0, clicked);
    }

    [AvaloniaFact]
    public void DragCommitsOneOldAndNewBoundsEvent()
    {
        var (window, surface, _, panel) = Open();
        var edits = new List<(Drawing.Rectangle Before, Drawing.Rectangle After)>();
        surface.BoundsEdited += (control, before, after) =>
        {
            Assert.Same(panel, control);
            edits.Add((before, after));
        };

        var from = OnForm(70, 55);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(14, 9), RawInputModifiers.Alt);
        window.MouseUp(from + new Vector(14, 9), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Drawing.Rectangle(49, 39, 80, 50), panel.Bounds);
        Assert.Equal((new Drawing.Rectangle(35, 30, 80, 50), panel.Bounds), Assert.Single(edits));
    }

    [AvaloniaFact]
    public void ResizeHandleAndKeyboardNudgeEditBounds()
    {
        var (window, surface, _, panel) = Open();
        var edits = new List<(Drawing.Rectangle Before, Drawing.Rectangle After)>();
        surface.BoundsEdited += (_, before, after) => edits.Add((before, after));

        var centre = OnForm(70, 55);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var corner = OnForm(115, 80);
        window.MouseDown(corner, MouseButton.Left);
        window.MouseMove(corner + new Vector(12, 8));
        window.MouseUp(corner + new Vector(12, 8), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Drawing.Size(92, 58), panel.Size);
        Assert.Single(edits);

        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(36, panel.Left);
        Assert.Equal(2, edits.Count);
    }

    [AvaloniaFact]
    public void DraggingANestedControlUsesItsParentCoordinates()
    {
        var (window, surface, _, panel) = Open();
        var button = new Forms.Button
        {
            Bounds = new Drawing.Rectangle(10, 10, 40, 25),
            Text = "Nested",
        };
        panel.Controls.Add(button);
        surface.RefreshForm();

        var from = OnForm(65, 52);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(13, 7), RawInputModifiers.Alt);
        window.MouseUp(from + new Vector(13, 7), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(button, surface.SelectedControl);
        Assert.Equal(new Drawing.Rectangle(23, 17, 40, 25), button.Bounds);
    }

    [AvaloniaFact]
    public void DraggingTwoSelectedSiblingsEmitsOneGroupEdit()
    {
        var (window, surface, form, first) = Open();
        var second = new Forms.Panel { Bounds = new Drawing.Rectangle(140, 95, 45, 35) };
        form.Controls.Add(second);
        surface.RefreshForm();
        var groupEdits = new List<IReadOnlyList<WinFormsBoundsEdit>>();
        var singleEdits = 0;
        surface.BoundsEdits += changes => groupEdits.Add(changes);
        surface.BoundsEdited += (_, _, _) => singleEdits++;

        var firstAt = OnForm(70, 55);
        var secondAt = OnForm(160, 110);
        window.MouseDown(firstAt, MouseButton.Left);
        window.MouseUp(firstAt, MouseButton.Left);
        window.MouseDown(secondAt, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(secondAt, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([first, second], surface.SelectedControls);

        window.MouseDown(firstAt, MouseButton.Left);
        window.MouseMove(firstAt + new Vector(12, 7), RawInputModifiers.Alt);
        window.MouseUp(firstAt + new Vector(12, 7), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Drawing.Point(47, 37), first.Location);
        Assert.Equal(new Drawing.Point(152, 102), second.Location);
        Assert.Equal(2, Assert.Single(groupEdits).Count);
        Assert.Equal(0, singleEdits);

        window.MouseDown(OnForm(59, 47), MouseButton.Left);
        window.MouseUp(OnForm(59, 47), MouseButton.Left);
        Assert.Equal([first], surface.SelectedControls);
    }

    [AvaloniaFact]
    public void ArrowKeyNudgesTheSelectedGroupInOneEdit()
    {
        var (window, surface, form, first) = Open();
        var second = new Forms.Panel { Bounds = new Drawing.Rectangle(140, 95, 45, 35) };
        form.Controls.Add(second);
        surface.RefreshForm();
        surface.Select(first);
        var secondAt = OnForm(160, 110);
        window.MouseDown(secondAt, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(secondAt, MouseButton.Left);
        var groupEdits = new List<IReadOnlyList<WinFormsBoundsEdit>>();
        surface.BoundsEdits += changes => groupEdits.Add(changes);

        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(36, first.Left);
        Assert.Equal(141, second.Left);
        Assert.Equal(2, Assert.Single(groupEdits).Count);
    }

    [AvaloniaFact]
    public void APrimaryResizeHandleNarrowsTheGroupBeforeResizing()
    {
        var (window, surface, form, first) = Open();
        var second = new Forms.Panel { Bounds = new Drawing.Rectangle(140, 95, 45, 35) };
        form.Controls.Add(second);
        surface.RefreshForm();
        surface.Select(first);
        var secondAt = OnForm(160, 110);
        window.MouseDown(secondAt, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(secondAt, MouseButton.Left);
        var edits = new List<Forms.Control>();
        surface.BoundsEdited += (control, _, _) => edits.Add(control);

        var corner = OnForm(185, 130);
        window.MouseDown(corner, MouseButton.Left);
        window.MouseMove(corner + new Vector(10, 5));
        window.MouseUp(corner + new Vector(10, 5), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([second], surface.SelectedControls);
        Assert.Equal(new Drawing.Size(55, 40), second.Size);
        Assert.Equal(new Drawing.Size(80, 50), first.Size);
        Assert.Same(second, Assert.Single(edits));
    }

    [AvaloniaFact]
    public void RestoringASelectionGroupKeepsOnePrimaryAndRejectsDetachedOrMixedParentControls()
    {
        var (window, surface, form, first) = Open();
        var second = new Forms.Panel { Bounds = new Drawing.Rectangle(140, 95, 45, 35) };
        var nested = new Forms.Button { Bounds = new Drawing.Rectangle(5, 5, 20, 15) };
        var detached = new Forms.Button();
        form.Controls.Add(second);
        first.Controls.Add(nested);
        var changed = new List<Forms.Control?>();
        surface.SelectionChanged += changed.Add;

        surface.SelectControls([first, second, first, detached], first);

        Assert.Equal([second, first], surface.SelectedControls);
        Assert.Same(first, surface.SelectedControl);
        Assert.Equal([first], changed);

        surface.SelectControls([first, nested]);
        Assert.Equal([first], surface.SelectedControls);
        Assert.Equal(2, changed.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void AFramedContainerOffsetsNestedHitAndResizeHandles()
    {
        var (window, surface, _, panel) = Open();
        panel.BorderStyle = Forms.BorderStyle.FixedSingle;
        var button = new Forms.Button
        {
            Bounds = new Drawing.Rectangle(10, 10, 40, 25),
            Text = "Nested",
        };
        panel.Controls.Add(button);
        surface.RefreshForm();

        var inside = OnForm(35 + 1 + 10 + 20, 30 + 1 + 10 + 12);
        window.MouseDown(inside, MouseButton.Left);
        window.MouseUp(inside, MouseButton.Left);
        Assert.Same(button, surface.SelectedControl);

        var corner = OnForm(35 + 1 + 10 + 40, 30 + 1 + 10 + 25);
        window.MouseDown(corner, MouseButton.Left);
        window.MouseMove(corner + new Vector(8, 5));
        window.MouseUp(corner + new Vector(8, 5), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Drawing.Size(48, 30), button.Size);
    }

    [AvaloniaFact]
    public void ReplacingThePreviewCancelsAStartedDragAndReleasesItsCapture()
    {
        var (window, surface, _, panel) = Open();
        var edits = 0;
        surface.BoundsEdited += (_, _, _) => edits++;
        var from = OnForm(70, 55);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(20, 10), RawInputModifiers.Alt);
        Assert.NotEqual(new Drawing.Point(35, 30), panel.Location);

        surface.RootForm = new Forms.Form { ClientSize = new Drawing.Size(180, 130) };
        window.MouseUp(from + new Vector(20, 10), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Drawing.Point(35, 30), panel.Location);
        Assert.Equal(0, edits);
        Assert.Null(surface.SelectedControl);
    }

    [AvaloniaFact]
    public void TheFormCanBeResizedWithoutOpeningANativeWindow()
    {
        var (window, surface, form, _) = Open();
        surface.Select(form);
        var edits = new List<(Drawing.Rectangle Before, Drawing.Rectangle After)>();
        surface.BoundsEdited += (control, before, after) =>
        {
            Assert.Same(form, control);
            edits.Add((before, after));
        };

        var corner = OnForm(220, 160);
        window.MouseDown(corner, MouseButton.Left);
        window.MouseMove(corner + new Vector(20, 15));
        window.MouseUp(corner + new Vector(20, 15), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new Drawing.Size(240, 175), form.ClientSize);
        Assert.Single(edits);
        Assert.False(form.Visible);
    }

    [AvaloniaFact]
    public void DeleteRequestsRemovalOfTheSelectedControl()
    {
        var (window, surface, _, panel) = Open();
        Forms.Control? requested = null;
        surface.DeleteRequested += control => requested = control;

        var at = OnForm(70, 55);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        Assert.Same(panel, requested);
    }

    [AvaloniaFact]
    public void AlignAndDistributeSelectedSiblingsEmitOneUndoableGroupPerCommand()
    {
        var (window, surface, form, first) = Open();
        first.Bounds = new Drawing.Rectangle(10, 20, 20, 20);
        var middle = new Forms.Panel { Bounds = new Drawing.Rectangle(50, 50, 20, 20) };
        var last = new Forms.Panel { Bounds = new Drawing.Rectangle(110, 80, 20, 20) };
        form.Controls.Add(middle);
        form.Controls.Add(last);
        surface.RefreshForm();
        var transactions = new List<IReadOnlyList<WinFormsBoundsEdit>>();
        surface.BoundsEdits += transactions.Add;

        window.MouseDown(OnForm(20, 30), MouseButton.Left);
        window.MouseUp(OnForm(20, 30), MouseButton.Left);
        window.MouseDown(OnForm(60, 60), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(OnForm(60, 60), MouseButton.Left);
        window.MouseDown(OnForm(120, 90), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(OnForm(120, 90), MouseButton.Left);
        Assert.Equal([first, middle, last], surface.SelectedControls);

        surface.ApplyLayoutCommand(WinFormsLayoutCommand.AlignTop);
        Assert.Equal(80, first.Top);
        Assert.Equal(80, middle.Top);
        Assert.Equal(2, Assert.Single(transactions).Count);

        surface.ApplyLayoutCommand(WinFormsLayoutCommand.DistributeHorizontal);
        Assert.Equal(60, middle.Left);
        Assert.Equal(2, transactions.Count);
        Assert.Single(transactions[1]);
        Assert.Equal(new Drawing.Rectangle(50, 80, 20, 20), transactions[1][0].Before);
        Assert.Equal(new Drawing.Rectangle(60, 80, 20, 20), transactions[1][0].After);
    }

    [AvaloniaFact]
    public void TabOrderModePaintsBadgesAndCommitsSiblingOrderWithoutRunningControlClicks()
    {
        var (window, surface, form, first) = Open();
        first.TabIndex = 0;
        var second = new Forms.Button
        {
            Bounds = new Drawing.Rectangle(140, 95, 45, 35), TabIndex = 1, Text = "Run",
        };
        var clicked = 0;
        second.Click += (_, _) => clicked++;
        form.Controls.Add(second);
        surface.RefreshForm();

        surface.TabOrderMode = true;
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 250));
        bitmap.Render(window);
        var pixels = new byte[320 * 250 * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 320, 250),
                handle.AddrOfPinnedObject(), pixels.Length, 320 * 4);
        }
        finally { handle.Free(); }
        var badgePixel = ((12 + 30 + 4) * 320 + (12 + 35 + 25)) * 4;
        Assert.True(pixels[badgePixel + 2] > pixels[badgePixel] + 20,
            $"the tab order badge should paint blue above the red Forms panel; " +
            $"RGBA={pixels[badgePixel]},{pixels[badgePixel + 1]},{pixels[badgePixel + 2]},{pixels[badgePixel + 3]}");

        var edits = new List<IReadOnlyList<WinFormsTabIndexEdit>>();
        surface.TabOrderEdits += edits.Add;
        window.MouseDown(OnForm(145, 100), MouseButton.Left);
        window.MouseUp(OnForm(145, 100), MouseButton.Left);

        Assert.Equal(0, second.TabIndex);
        Assert.Equal(1, first.TabIndex);
        Assert.Equal(2, Assert.Single(edits).Count);
        Assert.Equal(0, clicked);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(surface.TabOrderMode);
    }

    [AvaloniaFact]
    public void RealFormsPaintAppearsInsideTheDesignerCanvas()
    {
        var (window, _, _, _) = Open();
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 250));
        bitmap.Render(window);

        var pixels = new byte[320 * 250 * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 320, 250),
                handle.AddrOfPinnedObject(), pixels.Length, 320 * 4);
        }
        finally
        {
            handle.Free();
        }

        var pixel = ((12 + 55) * 320 + (12 + 75)) * 4;
        Assert.True(pixels[pixel] > pixels[pixel + 2] + 100,
            "the Forms panel should paint red at its actual form position");
    }

    /// <summary>
    /// A press on the empty area of a SplitContainer's panel selects the container, so the property grid can show it, and nothing throws.
    /// </summary>
    /// <remarks>
    /// Red-proof (the press selecting the hit control itself, as before): SelectedControl is the SplitterPanel and the Assert.Same fails.
    /// </remarks>
    [AvaloniaFact]
    public void APressOnAContainersPanelSelectsTheContainer()
    {
        var (window, surface, form, _) = Open();
        var container = new Forms.SplitContainer { Bounds = new Drawing.Rectangle(10, 10, 160, 120) };
        form.Controls.Add(container);
        surface.RefreshForm();

        var at = OnForm(10 + 4, 10 + 4);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(container, surface.SelectedControl);
    }

    /// <summary>A tab-order click on a container's panel is refused before any TabIndex changes: no tab edit is raised.</summary>
    /// <remarks>
    /// Red-proof (the panel guard in the tab-order click removed): the click assigns the panel a TabIndex in its container, and the
    /// Assert.Equal on the edit count fails.
    /// </remarks>
    [AvaloniaFact]
    public void ATabOrderClickOnAContainersCornerNeverGivesItsPanelATabIndex()
    {
        var (window, surface, form, _) = Open();
        var container = new Forms.SplitContainer { Bounds = new Drawing.Rectangle(10, 10, 160, 120) };
        form.Controls.Add(container);
        surface.RefreshForm();
        // Panel1 is not first in the order, so an assignment to it would change the order of the container's children.
        container.Panel1.TabIndex = 1;
        container.Panel2.TabIndex = 0;
        container.Panel1.TabStop = true;
        container.Panel2.TabStop = true;
        var panelIndex = container.Panel1.TabIndex;
        var edited = new List<Forms.Control>();
        surface.TabOrderEdits += changes => edited.AddRange(changes.Select(change => change.Control));
        surface.TabOrderMode = true;

        var at = OnForm(10 + 4, 10 + 4);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(container.Panel1, edited);
        Assert.DoesNotContain(container.Panel2, edited);
        Assert.Equal(panelIndex, container.Panel1.TabIndex);
    }

    /// <summary>The tab-order refusal: the form and a container's panel are not assigned a tab index; a control is.</summary>
    /// <remarks>
    /// Red-proof (TabOrderTarget returning the panel itself, with the panel clause removed): the Assert.Null on the panel fails.
    /// </remarks>
    [Fact]
    public void ATabOrderClickIsRefusedForAContainersPanelAndTheForm()
    {
        var form = new Forms.Form();
        var container = new Forms.SplitContainer();
        form.Controls.Add(container);

        Assert.Null(WinFormsDesignSurface.TabOrderTarget(container.Panel1, form));
        Assert.Null(WinFormsDesignSurface.TabOrderTarget(form, form));
        Assert.Null(WinFormsDesignSurface.TabOrderTarget(null, form));
        Assert.Same(container, WinFormsDesignSurface.TabOrderTarget(container, form));
    }

    /// <summary>A ToolStripPanel placed on the form is a control of its own, not a container's panel: a press selects it.</summary>
    /// <remarks>
    /// Red-proof (IsPanelOfContainer answering true for every ToolStripPanel): the press selects the form instead, and the
    /// Assert.Same on the panel fails.
    /// </remarks>
    [AvaloniaFact]
    public void AStandaloneToolStripPanelIsSelectedByAPress()
    {
        var (window, surface, form, _) = Open();
        var strip = new Forms.ToolStripPanel { Bounds = new Drawing.Rectangle(10, 10, 160, 40) };
        form.Controls.Add(strip);
        var button = new Forms.Button { Bounds = new Drawing.Rectangle(10, 60, 40, 25), TabIndex = 0 };
        form.Controls.Add(button);
        strip.TabIndex = 1;
        // An empty ToolStripPanel has no height to click, so it hosts a strip, as a designer does.
        strip.Join(new Forms.ToolStrip());
        surface.RefreshForm();

        var at = OnForm(10 + 4, 10 + 4);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(strip, surface.SelectedControl);

    }

    /// <summary>A standalone ToolStripPanel is not refused in tab-order mode: it is a control that takes a tab index.</summary>
    /// <remarks>
    /// Red-proof (IsPanelOfContainer answering true for every ToolStripPanel): TabOrderTarget returns null and the Assert.Same fails.
    /// </remarks>
    [Fact]
    public void AStandaloneToolStripPanelIsNotRefusedInTabOrderMode()
    {
        var form = new Forms.Form();
        var strip = new Forms.ToolStripPanel();
        form.Controls.Add(strip);

        Assert.Same(strip, WinFormsDesignSurface.TabOrderTarget(strip, form));
    }

    /// <summary>
    /// A click on a container's corner finds the container's badge: the badge of its own panel, drawn at the same place, does not win.
    /// </summary>
    /// <remarks>
    /// Red-proof (HitBadge keeping the panel's badge): the hit is Panel1, and the Assert.Same on the container fails.
    /// </remarks>
    [AvaloniaFact]
    public void TheContainersBadgeWinsOverItsPanelsBadge()
    {
        var form = new Forms.Form { ClientSize = new Drawing.Size(220, 160) };
        var container = new Forms.SplitContainer { Bounds = new Drawing.Rectangle(10, 10, 160, 120) };
        form.Controls.Add(container);

        var hit = WinFormsDesignerTabOrder.HitBadge(form, new Avalonia.Point(16, 16), AbsoluteBounds);

        Assert.Same(container, hit);
    }

    private static Avalonia.Rect AbsoluteBounds(Forms.Control control)
    {
        var x = 0;
        var y = 0;
        for (var current = control; current.Parent is not null; current = current.Parent)
        {
            x += current.Left;
            y += current.Top;
        }
        return new Avalonia.Rect(x, y, control.Width, control.Height);
    }
}
