using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

/// <summary>
/// Drives the real "Select Resource" editor end to end through the Properties grid's "…"
/// button: a form opens in the headless designer, a control (or ToolStrip item) is selected,
/// the ellipsis opens <see cref="WinFormsImageResourceDialog"/>, "Import…" is answered by an
/// injected picker that returns a real 16x16 red PNG, and OK commits it. This is the UI path
/// <see cref="WinFormsImageResourcesTests"/> cannot reach: that file proves the pure
/// Basalt.Workspace/Basalt.Designer layer, this file proves the dialog, the grid and the
/// rendered pixels actually agree with it.
/// </summary>
public sealed class WinFormsImageEditorUiTests
{
    private const string FreshSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ToolStrip1 = New Global.System.Windows.Forms.ToolStrip()
                Me.ToolStrip1.Name = "ToolStrip1"
                Me.Controls.Add(Me.ToolStrip1)
                Me.ClientSize = New System.Drawing.Size(500, 400)
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents ToolStrip1 As Global.System.Windows.Forms.ToolStrip
        End Class
        """;

    /// <summary>A form with a Button, a PictureBox and a ToolStrip holding one ToolStripButton, none with an image yet.</summary>
    private static string BuildFreshSource()
    {
        var document = new WinFormsDesignerDocument(FreshSource);
        document = new WinFormsDesignerDocument(document.AddControl("Button1", "Global.System.Windows.Forms.Button"));
        document = new WinFormsDesignerDocument(document.SetProperty("Button1", "Location", "New System.Drawing.Point(20, 60)"));
        document = new WinFormsDesignerDocument(document.SetProperty("Button1", "Size", "New System.Drawing.Size(80, 40)"));
        document = new WinFormsDesignerDocument(document.AddControl("PictureBox1", "Global.System.Windows.Forms.PictureBox"));
        document = new WinFormsDesignerDocument(document.SetProperty("PictureBox1", "Location", "New System.Drawing.Point(20, 120)"));
        document = new WinFormsDesignerDocument(document.SetProperty("PictureBox1", "Size", "New System.Drawing.Size(40, 40)"));
        document = new WinFormsDesignerDocument(
            document.ToolStripItems.Set("ToolStrip1", [new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "")]));
        return document.Source;
    }

    /// <summary>The same shape, but Visual Studio already wrote a resx entry and GetObject assignment for PictureBox1.Image.</summary>
    private static (string Designer, string Resources) BuildVisualStudioAuthoredSource()
    {
        var fresh = BuildFreshSource();
        var bluePng = SolidPng(Drawing.Color.Blue);
        var (value, typeName, mimeType) = WinFormsImageResources.Encode(bluePng, isIcon: false);
        var resources = new WinFormsResourceDocument(
            new WinFormsResourceDocument(WinFormsResourceDocumentTemplate.Empty).SetValue("PictureBox1.Image", value, typeName, mimeType));
        var designer = new WinFormsDesignerDocument(fresh)
            .SetImageResource("PictureBox1", "Image", "PictureBox1.Image", "System.Drawing.Image");
        return (designer, resources.Source);
    }

    /// <summary>A real, decodable PNG (not just bytes that round-trip through base64): the
    /// surface must actually paint it, so <c>Image.FromStream</c> has to succeed on it.</summary>
    private static byte[] SolidPng(Drawing.Color color)
    {
        using var bitmap = new Drawing.Bitmap(16, 16);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    [AvaloniaFact]
    public async Task ImportingAnImageOnAPictureBoxWithoutAResxWritesBothFilesPaintsRedAndUndoesCleanly()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", BuildFreshSource(), true);
        EditorDocumentViewModel? resourceBuffer = null;
        var created = 0;
        var redPng = SolidPng(Drawing.Color.Red);

        using var view = new WinFormsDesignerView(designer,
            createCode: (path, content) =>
            {
                ++created;
                resourceBuffer = new EditorDocumentViewModel(path, content);
                return Task.FromResult(resourceBuffer);
            },
            importImageFile: () => Task.FromResult<string?>(WriteTempPng(redPng)));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var pictureBox = surface.RootForm!.Controls.OfType<Forms.PictureBox>().Single();
            surface.Select(pictureBox);

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => grid.Properties.Any(p => p.Name == "Image"));
            var row = FindRow(grid, "Image");
            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
            var dialog = window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single();

            Click(dialog, "WinFormsImageDialogImport");
            await WaitUntilAsync(() => dialog.SelectedBytes is not null);
            ClickOk(dialog);
            await WaitUntilAsync(() => !window.OwnedWindows.Any());
            await WaitUntilAsync(() => designer.Text.Contains("PictureBox1.Image", StringComparison.Ordinal) && view.IsEnabled);

            Assert.Equal(1, created);
            Assert.Contains(
                "Dim resources As System.ComponentModel.ComponentResourceManager = " +
                "New System.ComponentModel.ComponentResourceManager(GetType(MainForm))", designer.Text);
            Assert.Contains("Me.PictureBox1.Image = CType(resources.GetObject(\"PictureBox1.Image\"), System.Drawing.Image)", designer.Text);
            Assert.True(WinFormsImageResources.TryDecode(
                new WinFormsResourceDocument(resourceBuffer!.Text).TryGet("PictureBox1.Image")!, out var decoded));
            Assert.Equal(redPng, decoded);

            // The commit re-parses and re-instantiates the whole form; the live control
            // behind "pictureBox" from before the edit is stale, so it is read again.
            var realPictureBox = surface.RootForm!.Controls.OfType<Forms.PictureBox>().Single();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            // PictureBox1's default SizeMode (Normal) pins the 16x16 image at its top-left
            // corner rather than stretching or centering it, so the sampled point must land
            // inside that corner, not the middle of the (larger) control. The surface's own
            // 12px design gutter places the control within the surface, but the surface
            // itself sits to the right of the toolbox and below the toolbar inside this
            // view's full layout (unlike a bare surface filling the window), so the point has
            // to be translated through the surface's own position, the same way
            // WinFormsSmartTagTests locates glyphs painted inside this same view.
            var point = surface.TranslatePoint(
                new Point(12 + realPictureBox.Location.X + 8, 12 + realPictureBox.Location.Y + 8), window)!.Value;
            var pixel = ReadPixel(window, point);
            Assert.True(pixel.Red > 150 && pixel.Green < 80 && pixel.Blue < 80,
                $"The PictureBox should paint the imported red image at {point}; RGB={pixel.Red},{pixel.Green},{pixel.Blue}.");

            var afterImport = designer.Text;
            var afterImportResources = resourceBuffer.Text;
            ClickUndo(view);
            await WaitUntilAsync(() => !designer.Text.Contains("PictureBox1.Image", StringComparison.Ordinal) && view.IsEnabled);
            Assert.DoesNotContain("PictureBox1.Image", designer.Text);
            Assert.Empty(new WinFormsResourceDocument(resourceBuffer.Text).Entries);
            Assert.Equal(1, created);

            ClickRedo(view);
            await WaitUntilAsync(() => designer.Text == afterImport && view.IsEnabled);
            Assert.Equal(afterImportResources, resourceBuffer.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClearingAVisualStudioAuthoredImageRemovesStatementAndResxEntry()
    {
        var (designerSource, resourcesSource) = BuildVisualStudioAuthoredSource();
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", designerSource, true);
        var resourceBuffer = new EditorDocumentViewModel("MainForm.resx", resourcesSource);

        using var view = new WinFormsDesignerView(designer,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? resourceBuffer : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var pictureBox = surface.RootForm!.Controls.OfType<Forms.PictureBox>().Single();
            surface.Select(pictureBox);

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => grid.Properties.Any(p => p.Name == "Image"));
            var row = FindRow(grid, "Image");
            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
            var dialog = window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single();

            // The dialog opens already showing the image Visual Studio assigned.
            Assert.NotNull(dialog.SelectedBytes);

            Click(dialog, "WinFormsImageDialogClear");
            ClickOk(dialog);
            await WaitUntilAsync(() =>
                !designer.Text.Contains("PictureBox1.Image", StringComparison.Ordinal) && view.IsEnabled);

            Assert.DoesNotContain("PictureBox1.Image", designer.Text);
            Assert.Null(new WinFormsResourceDocument(resourceBuffer.Text).TryGet("PictureBox1.Image"));

            var afterClear = designer.Text;
            var afterClearResources = resourceBuffer.Text;
            ClickUndo(view);
            await WaitUntilAsync(() => designer.Text.Contains("PictureBox1.Image", StringComparison.Ordinal) && view.IsEnabled);
            Assert.Equal(designerSource, designer.Text);
            Assert.Equal(resourcesSource, resourceBuffer.Text);

            ClickRedo(view);
            await WaitUntilAsync(() => designer.Text == afterClear && view.IsEnabled);
            Assert.Equal(afterClearResources, resourceBuffer.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ToolStripButtonImageRoundTripsThroughTheItemPropertiesPathAndPaintsRed()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", BuildFreshSource(), true);
        EditorDocumentViewModel? resourceBuffer = null;
        var redPng = SolidPng(Drawing.Color.Red);

        using var view = new WinFormsDesignerView(designer,
            createCode: (path, content) =>
            {
                resourceBuffer = new EditorDocumentViewModel(path, content);
                return Task.FromResult(resourceBuffer);
            },
            importImageFile: () => Task.FromResult<string?>(WriteTempPng(redPng)));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var strip = surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single();
            surface.Select(strip);

            var editor = Assert.Single(view.GetVisualDescendants().OfType<WinFormsToolStripDesignEditor>());
            var itemButton = Assert.Single(editor.GetVisualDescendants().OfType<Button>(),
                b => b.Name == "ToolStripInlineItem_ToolStripButton1");
            itemButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => grid.Properties.Any(p => p.Name == "Image"));
            var row = FindRow(grid, "Image");
            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
            var dialog = window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single();

            Click(dialog, "WinFormsImageDialogImport");
            await WaitUntilAsync(() => dialog.SelectedBytes is not null);
            ClickOk(dialog);
            await WaitUntilAsync(() =>
                designer.Text.Contains("ToolStripButton1.Image", StringComparison.Ordinal) && view.IsEnabled);

            Assert.Contains(
                "Me.ToolStripButton1.Image = CType(resources.GetObject(\"ToolStripButton1.Image\"), System.Drawing.Image)",
                designer.Text);
            Assert.True(WinFormsImageResources.TryDecode(
                new WinFormsResourceDocument(resourceBuffer!.Text).TryGet("ToolStripButton1.Image")!, out var decoded));
            Assert.Equal(redPng, decoded);
            // The commit re-parses and re-instantiates the whole form, so the live control
            // behind "strip" from before the edit is stale; the current one is read again.
            var realStrip = surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single();
            var realItem = realStrip.Items.Cast<Forms.ToolStripItem>().Single();
            Assert.NotNull(realItem.Image);

            var afterImport = designer.Text;
            ClickUndo(view);
            await WaitUntilAsync(() =>
                !designer.Text.Contains("ToolStripButton1.Image", StringComparison.Ordinal) && view.IsEnabled);
            Assert.DoesNotContain("ToolStripButton1.Image", designer.Text);
            Assert.Empty(new WinFormsResourceDocument(resourceBuffer.Text).Entries);

            ClickRedo(view);
            await WaitUntilAsync(() => designer.Text == afterImport && view.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The root cause of the compiled app crashing at startup: a PNG on an Icon
    /// property wrote a resx <c>System.Drawing.Icon, System.Drawing</c> entry that
    /// <c>IconConverter</c> cannot decode from InitializeComponent. The dialog must
    /// catch this itself, by actually decoding the imported bytes as the target
    /// type, rather than trust the file the picker happened to return.
    /// </summary>
    [AvaloniaFact]
    public async Task IconDialogRejectsAPngImportWithAnErrorAndLeavesTheSelectionUntouched()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var pngPath = WriteTempPng(SolidPng(Drawing.Color.Red));
            var dialog = new WinFormsImageResourceDialog(
                currentBytes: null, isIcon: true, importFile: () => Task.FromResult<string?>(pngPath));
            var resultTask = dialog.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();

            Click(dialog, "WinFormsImageDialogImport");
            await WaitUntilAsync(() => FindError(dialog).IsVisible);
            Assert.Null(dialog.SelectedBytes);
            Assert.False(dialog.Changed);
            Assert.False(dialog.Cleared);

            ClickCancel(dialog);
            Assert.False(await resultTask);
        }
        finally
        {
            owner.Close();
        }
    }

    /// <summary>
    /// A file that is neither a valid icon nor a valid image (truncated, or simply
    /// not a picture at all) must be rejected the same way a wrong-type file is,
    /// instead of being written into the resx as unreadable bytes.
    /// </summary>
    [AvaloniaFact]
    public async Task ImportingACorruptFileOntoAPictureBoxImageShowsAnErrorAndWritesNothing()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", BuildFreshSource(), true);
        var created = 0;
        var corruptPath = WriteTempFile([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        using var view = new WinFormsDesignerView(designer,
            createCode: (path, content) =>
            {
                ++created;
                return Task.FromResult(new EditorDocumentViewModel(path, content));
            },
            importImageFile: () => Task.FromResult<string?>(corruptPath));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var pictureBox = surface.RootForm!.Controls.OfType<Forms.PictureBox>().Single();
            surface.Select(pictureBox);

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => grid.Properties.Any(p => p.Name == "Image"));
            var row = FindRow(grid, "Image");
            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
            var dialog = window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single();

            Click(dialog, "WinFormsImageDialogImport");
            await WaitUntilAsync(() => FindError(dialog).IsVisible);
            Assert.Null(dialog.SelectedBytes);
            Assert.False(dialog.Changed);

            ClickCancel(dialog);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BuildFreshSource(), designer.Text);
            Assert.Equal(0, created);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Opening "…" and pressing OK without Import… or Clear must leave both files
    /// untouched: before this fix, OK always re-wrote the resx with a freshly
    /// re-encoded (and not byte-identical) copy of the image already there, and
    /// pushed a spurious step onto the undo stack.
    /// </summary>
    [AvaloniaFact]
    public async Task OpeningTheImageDialogAndPressingOkWithoutChangingAnythingIsANoOp()
    {
        var (designerSource, resourcesSource) = BuildVisualStudioAuthoredSource();
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", designerSource, true);
        var resourceBuffer = new EditorDocumentViewModel("MainForm.resx", resourcesSource);

        using var view = new WinFormsDesignerView(designer,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? resourceBuffer : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var pictureBox = surface.RootForm!.Controls.OfType<Forms.PictureBox>().Single();
            surface.Select(pictureBox);

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => grid.Properties.Any(p => p.Name == "Image"));
            var row = FindRow(grid, "Image");
            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
            var dialog = window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single();

            // The dialog opens already showing the image Visual Studio assigned,
            // but nothing was imported or cleared.
            Assert.NotNull(dialog.SelectedBytes);
            Assert.False(dialog.Changed);

            ClickOk(dialog);
            await WaitUntilAsync(() => !window.OwnedWindows.Any());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(designerSource, designer.Text);
            Assert.Equal(resourcesSource, resourceBuffer.Text);

            // Nothing was pushed onto the undo stack: Undo must be a no-op.
            ClickUndo(view);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(designerSource, designer.Text);
            Assert.Equal(resourcesSource, resourceBuffer.Text);
        }
        finally
        {
            window.Close();
        }
    }

    private static TextBlock FindError(Window dialog) =>
        dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "WinFormsImageDialogError");

    private static void ClickCancel(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, Localizer.Get(StringKeys.WinFormsEditorCancel)))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string WriteTempPng(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "basalt-image-test-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "basalt-image-test-" + Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Border FindRow(PropertyGrid grid, string name) => Assert.Single(grid.Rows.OfType<Border>(),
        row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static void Click(Window dialog, string name) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickOk(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, Localizer.Get(StringKeys.WinFormsEditorOk)))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickUndo(WinFormsDesignerView view) => ClickHistory(view, StringKeys.MenuEditUndo);
    private static void ClickRedo(WinFormsDesignerView view) => ClickHistory(view, StringKeys.MenuEditRedo);

    private static void ClickHistory(WinFormsDesignerView view, string key) =>
        Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, Localizer.Get(key)))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static (byte Red, byte Green, byte Blue) ReadPixel(Window window, Point point)
    {
        var width = (int)window.Width;
        var height = (int)window.Height;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4);
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
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The image editor round trip did not complete.");
    }
}
