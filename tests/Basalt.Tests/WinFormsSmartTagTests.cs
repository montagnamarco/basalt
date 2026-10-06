using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Proves the Visual Studio style WinForms designer smart tag end to end: a real form opens in
/// the designer, a control is selected, the orange glyph the selection border grows at its
/// top-right corner is rasterised to prove it actually paints there (not just logically present),
/// clicking it opens the "&lt;Type&gt; Tasks" flyout, and each task writes exactly what Visual
/// Studio would to the .Designer.vb as one undo step. <see cref="WinFormsToolStripInlineTests"/>
/// and <see cref="WinFormsMenuInlineTests"/> prove the sibling "Type Here" overlays the same way.
/// </summary>
public sealed class WinFormsSmartTagTests
{
    private const string Source = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ToolStrip1 = New Global.System.Windows.Forms.ToolStrip()
                Me.MenuStrip1 = New Global.System.Windows.Forms.MenuStrip()
                Me.StatusStrip1 = New Global.System.Windows.Forms.StatusStrip()
                Me.Panel1 = New Global.System.Windows.Forms.Panel()
                Me.Button1 = New Global.System.Windows.Forms.Button()
                Me.ToolStrip1.Name = "ToolStrip1"
                Me.MenuStrip1.Name = "MenuStrip1"
                Me.StatusStrip1.Name = "StatusStrip1"
                Me.Panel1.Location = New System.Drawing.Point(20, 80)
                Me.Panel1.Size = New System.Drawing.Size(150, 100)
                Me.Panel1.Name = "Panel1"
                Me.Button1.Location = New System.Drawing.Point(200, 80)
                Me.Button1.Size = New System.Drawing.Size(75, 23)
                Me.Button1.Name = "Button1"
                Me.Controls.Add(Me.Panel1)
                Me.Controls.Add(Me.Button1)
                Me.Controls.Add(Me.ToolStrip1)
                Me.Controls.Add(Me.StatusStrip1)
                Me.Controls.Add(Me.MenuStrip1)
                Me.ClientSize = New System.Drawing.Size(600, 400)
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents ToolStrip1 As Global.System.Windows.Forms.ToolStrip
            Friend WithEvents MenuStrip1 As Global.System.Windows.Forms.MenuStrip
            Friend WithEvents StatusStrip1 As Global.System.Windows.Forms.StatusStrip
            Friend WithEvents Panel1 As Global.System.Windows.Forms.Panel
            Friend WithEvents Button1 As Global.System.Windows.Forms.Button
        End Class
        """;

    private static readonly string[] ExpectedToolStripNames =
    [
        "NewToolStripButton", "OpenToolStripButton", "SaveToolStripButton", "PrintToolStripButton",
        "ToolStripSeparator", "CutToolStripButton", "CopyToolStripButton", "PasteToolStripButton",
        "ToolStripSeparator2", "HelpToolStripButton",
    ];

    [AvaloniaFact]
    public async Task SelectingAToolStripShowsAGlyphPaintedAtItsTopRightCorner()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            surface.Select(FindControl<Forms.ToolStrip>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var glyph = Find<Border>(tag, "WinFormsSmartTagGlyph");
            // Offset from dead centre: the centre pixel is the black arrow glyph's own ink, not
            // the Orange background around it.
            var point = glyph.TranslatePoint(new Point(2, 2), window)!.Value;
            var pixel = ReadPixel(window, point);
            Assert.True(pixel.Red > 200 && pixel.Blue < 80,
                $"The smart tag glyph should paint Orange at {point}; RGB={pixel.Red},{pixel.Green},{pixel.Blue}.");
        });
    }

    [AvaloniaFact]
    public async Task ControlWithoutTasksShowsNoGlyph()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            surface.Select(FindControl<Forms.Button>(surface));
            await WaitUntilAsync(() => view.IsEnabled);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(view.GetVisualDescendants().OfType<WinFormsSmartTag>());
        });
    }

    [AvaloniaFact]
    public async Task ClickingTheGlyphListsToolStripTasksButOmitsEditItems()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            surface.Select(FindControl<Forms.ToolStrip>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var flyout = OpenFlyout(tag, window);
            Assert.Equal("ToolStrip Tasks", Find<TextBlock>(flyout, "WinFormsSmartTagHeader").Text);
            Assert.NotNull(FindOrNull<Button>(flyout, "WinFormsSmartTagAction_InsertStandardItems"));
            Assert.NotNull(FindOrNull<ComboBox>(flyout, "WinFormsSmartTagCombo_Dock"));
            Assert.Null(FindOrNull<Button>(flyout, "WinFormsSmartTagAction_EditItems"));
        });
    }

    [AvaloniaFact]
    public async Task InsertStandardItemsWritesToolStripButtonsAndSeparatorsAsOneUndoStep()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            var before = document.Text;
            surface.Select(FindControl<Forms.ToolStrip>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var flyout = OpenFlyout(tag, window);
            Click(Find<Button>(flyout, "WinFormsSmartTagAction_InsertStandardItems"));
            await WaitUntilAsync(() => ReadToolStrip(document, "ToolStrip1").Count == 10 && view.IsEnabled);

            var items = ReadToolStrip(document, "ToolStrip1");
            Assert.Equal(ExpectedToolStripNames, items.Select(item => item.Name).ToArray());
            Assert.Equal("ToolStripSeparator", items[4].TypeName);
            Assert.Equal("ToolStripSeparator", items[8].TypeName);
            Assert.Equal("ToolStripButton", items[0].TypeName);
            Assert.Equal("&New", items[0].Text);
            Assert.Contains("Friend WithEvents NewToolStripButton As Global.System.Windows.Forms.ToolStripButton",
                document.Text);

            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == before && view.IsEnabled);
        });
    }

    [AvaloniaFact]
    public async Task DockComboWritesDockStyleAndUndoes()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            var before = document.Text;
            surface.Select(FindControl<Forms.ToolStrip>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var flyout = OpenFlyout(tag, window);
            var combo = Find<ComboBox>(flyout, "WinFormsSmartTagCombo_Dock");
            combo.SelectedItem = "Fill";
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.ToolStrip1.Dock = Global.System.Windows.Forms.DockStyle.Fill", StringComparison.Ordinal)
                && view.IsEnabled);

            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == before && view.IsEnabled);
        });
    }

    [AvaloniaFact]
    public async Task PanelDockInParentContainerTogglesFillAndNone()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            var before = document.Text;
            surface.Select(FindControl<Forms.Panel>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var flyout = OpenFlyout(tag, window);
            var dockButton = Assert.Single(flyout.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Dock in Parent Container"));
            Click(dockButton);
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.Panel1.Dock = Global.System.Windows.Forms.DockStyle.Fill", StringComparison.Ordinal)
                && view.IsEnabled);
            var afterDock = document.Text;

            // Reselecting after the edit rebuilds the preview; the smart tag must now offer the
            // opposite toggle because the real Panel1.Dock is Fill.
            surface.Select(FindControl<Forms.Panel>(surface));
            var reopened = await WaitForSmartTagAsync(view, window);
            var undockFlyout = OpenFlyout(reopened, window);
            var undockButton = Assert.Single(undockFlyout.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Undock in Parent Container"));
            Click(undockButton);
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.Panel1.Dock = Global.System.Windows.Forms.DockStyle.None", StringComparison.Ordinal)
                && view.IsEnabled);

            // Each toggle is its own undo step, so each undo click must be awaited before the
            // next: RunEditAsync drops a click that arrives while the previous one is still busy.
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterDock && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == before && view.IsEnabled);
        });
    }

    [AvaloniaFact]
    public async Task TopRightResizeHandleStillWorksWithTheGlyphShown()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            var panel = FindControl<Forms.Panel>(surface);
            surface.Select(panel);
            await WaitForSmartTagAsync(view, window);

            // The selection border Visual Studio draws around Panel1 is a Border whose own size
            // matches the control's, placed at the selection box's top-left: translating its
            // local (Width, 0) point to window coordinates gives the top-right resize handle's
            // centre without duplicating WinFormsDesignSurface's private Gutter/box math here.
            var border = Assert.Single(view.GetVisualDescendants().OfType<Border>(),
                candidate => candidate.Width == panel.Width && candidate.Height == panel.Height);
            var topRight = border.TranslatePoint(new Point(panel.Width, 0), window)!.Value;

            var before = panel.Size;
            // The top-right handle moves the top edge, not the bottom one: dragging it down
            // shrinks the height even as dragging it right grows the width.
            var expected = new System.Drawing.Size(before.Width + 10, before.Height - 6);
            window.MouseDown(topRight, MouseButton.Left);
            window.MouseMove(topRight + new Vector(10, 6));
            window.MouseUp(topRight + new Vector(10, 6), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, panel.Size);

            // The drag's own mutation above is synchronous, but releasing the mouse also commits
            // a SetBoundsAsync edit that rebuilds the preview (a new Forms.Panel instance) and
            // reselects it by name; wait for that full round trip - and the smart tag it brings
            // back for the new instance - to settle before touching the glyph.
            await WaitUntilAsync(() => view.IsEnabled && FindControl<Forms.Panel>(surface).Size == expected);
            var tag = await WaitForSmartTagAsync(view, window);

            // The glyph itself, now moved clear of the handle, must still open the flyout.
            OpenFlyout(tag, window);
        });
    }

    [AvaloniaFact]
    public async Task ReselectingAControlDoesNotReopenAStaleFlyout()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            surface.Select(FindControl<Forms.Panel>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            OpenFlyout(tag, window);

            // Selecting the Button has no smart tag tasks, so the glyph (and its open flyout)
            // disappears entirely rather than Show() ever running again for it.
            surface.Select(FindControl<Forms.Button>(surface));
            await WaitUntilAsync(() => !view.GetVisualDescendants().OfType<WinFormsSmartTag>().Any());

            // Reselecting the Panel must show a freshly collapsed smart tag, not resurrect the
            // flyout left open before the selection moved away from it.
            surface.Select(FindControl<Forms.Panel>(surface));
            var reopened = await WaitForSmartTagAsync(view, window);
            var flyout = Find<Border>(reopened, "WinFormsSmartTagFlyout");
            Assert.False(flyout.IsVisible,
                "Reselecting a control must not reopen a flyout left open before selecting away from it.");
        });
    }

    [AvaloniaFact]
    public async Task MenuStripStandardItemsRoundTripAndOfferEditItems()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            var before = document.Text;
            surface.Select(FindControl<Forms.MenuStrip>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var flyout = OpenFlyout(tag, window);
            Assert.Equal("MenuStrip Tasks", Find<TextBlock>(flyout, "WinFormsSmartTagHeader").Text);
            Assert.NotNull(FindOrNull<Button>(flyout, "WinFormsSmartTagAction_EditItems"));
            Click(Find<Button>(flyout, "WinFormsSmartTagAction_InsertStandardItems"));
            // 4 top menus + File(10, including the "Print Pre&view" entry Visual Studio adds
            // after Print) + Edit(8) + Tools(2) + Help(5) = 29.
            await WaitUntilAsync(() => ReadMenu(document, "MenuStrip1").Count == 29 && view.IsEnabled);

            var items = ReadMenu(document, "MenuStrip1");
            var top = items.Where(item => item.Parent == "MenuStrip1").ToArray();
            Assert.Equal(["&File", "&Edit", "&Tools", "&Help"], top.Select(item => item.Text).ToArray());
            var file = top.Single(item => item.Text == "&File");
            Assert.Contains(items, item => item.Parent == file.Name && item.Text == "&New");
            Assert.Contains(items, item => item.Parent == file.Name && item.Text == "Print Pre&view");
            var tools = top.Single(item => item.Text == "&Tools");
            // Visual Studio writes plain "&Customize"/"&Options" here, with no "..." ellipsis.
            Assert.Contains(items, item => item.Parent == tools.Name && item.Text == "&Customize");

            // ShortcutKeys are written through SetItemProperty after the Set() that adds the
            // items, as one more statement inside the same undo step, not as part of the item
            // record Set() itself writes.
            Assert.Contains(
                "Me.NewToolStripMenuItem.ShortcutKeys = CType((System.Windows.Forms.Keys.Control Or System.Windows.Forms.Keys.N), System.Windows.Forms.Keys)",
                document.Text);
            Assert.Contains(
                "Me.SaveToolStripMenuItem.ShortcutKeys = CType((System.Windows.Forms.Keys.Control Or System.Windows.Forms.Keys.S), System.Windows.Forms.Keys)",
                document.Text);

            // A fresh WinFormsMenuDocument over the resulting text round-trips: the ShortcutKeys
            // statements SetItemProperty adds do not confuse the reader that built "items" above.
            var reread = new WinFormsMenuDocument(document.Text).Read("MenuStrip1");
            Assert.Equal(items.Count, reread.Count);

            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == before && view.IsEnabled);
        });
    }

    [AvaloniaFact]
    public async Task StatusStripOffersNoInsertStandardItemsButKeepsDock()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            surface.Select(FindControl<Forms.StatusStrip>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var flyout = OpenFlyout(tag, window);
            Assert.Equal("StatusStrip Tasks", Find<TextBlock>(flyout, "WinFormsSmartTagHeader").Text);
            Assert.Null(FindOrNull<Button>(flyout, "WinFormsSmartTagAction_InsertStandardItems"));
            Assert.NotNull(FindOrNull<ComboBox>(flyout, "WinFormsSmartTagCombo_Dock"));
        });
    }

    [AvaloniaFact]
    public async Task InsertStandardItemsIsDisabledOnceTheStripAlreadyHasItems()
    {
        await RunAsync(async (view, surface, window, document) =>
        {
            surface.Select(FindControl<Forms.ToolStrip>(surface));
            var tag = await WaitForSmartTagAsync(view, window);
            var flyout = OpenFlyout(tag, window);
            Click(Find<Button>(flyout, "WinFormsSmartTagAction_InsertStandardItems"));
            await WaitUntilAsync(() => ReadToolStrip(document, "ToolStrip1").Count == 10 && view.IsEnabled);

            // Reselecting rebuilds the preview, so the strip the smart tag now sees is a fresh
            // Forms.ToolStrip instance that already carries the items the edit above just wrote.
            surface.Select(FindControl<Forms.ToolStrip>(surface));
            var reopened = await WaitForSmartTagAsync(view, window);
            var reopenedFlyout = OpenFlyout(reopened, window);
            var insertButton = Find<Button>(reopenedFlyout, "WinFormsSmartTagAction_InsertStandardItems");
            Assert.False(insertButton.IsEnabled,
                "\"Insert Standard Items\" must be greyed out once the strip already has items.");
        });
    }

    private static async Task RunAsync(Func<WinFormsDesignerView, WinFormsDesignSurface, Window, EditorDocumentViewModel, Task> body)
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            await body(view, surface, window, document);
        }
        finally
        {
            window.Close();
        }
    }

    private static T FindControl<T>(WinFormsDesignSurface surface) where T : Forms.Control =>
        Assert.Single(surface.RootForm!.Controls.OfType<T>(), control => control.GetType() == typeof(T));

    private static async Task<WinFormsSmartTag> WaitForSmartTagAsync(WinFormsDesignerView view, Window window)
    {
        await WaitUntilAsync(() => view.GetVisualDescendants().OfType<WinFormsSmartTag>().Any());
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(view.GetVisualDescendants().OfType<WinFormsSmartTag>());
    }

    private static Border OpenFlyout(WinFormsSmartTag tag, Window window)
    {
        Click(Find<Button>(tag, "WinFormsSmartTagGlyphButton"));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var flyout = Find<Border>(tag, "WinFormsSmartTagFlyout");
        Assert.True(flyout.IsVisible, "Clicking the glyph must open the \"Tasks\" flyout.");
        return flyout;
    }

    private static IReadOnlyList<WinFormsToolStripItem> ReadToolStrip(EditorDocumentViewModel document, string strip) =>
        new WinFormsToolStripDocument(document.Text).Read(strip);

    private static IReadOnlyList<WinFormsMenuItem> ReadMenu(EditorDocumentViewModel document, string strip) =>
        new WinFormsMenuDocument(document.Text).Read(strip);

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static T? FindOrNull<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static void ClickUndo(WinFormsDesignerView view)
    {
        var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
            Equals(button.Content, Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.MenuEditUndo)));
        Click(undo);
    }

    private static (byte Red, byte Green, byte Blue) ReadPixel(Window window, Point point)
    {
        const int width = 1100;
        const int height = 700;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height),
                pin.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            pin.Free();
        }

        var offset = ((int)point.Y * width + (int)point.X) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // 20s: shares WinFormsToolStripInlineTests' rationale for the same deadline (every
        // AvaloniaFact test shares one dispatcher thread under a full-suite run, so a slow
        // neighbour test can starve this one's RunEditAsync of CPU for several seconds).
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The smart tag task did not complete.");
    }
}
