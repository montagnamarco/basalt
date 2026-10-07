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
            // 12: the surface's gutter. 4, 23: the root form's default window frame
            // (sizing border, and border plus caption), drawn around the client area.
            var point = surface.TranslatePoint(
                new Point(12 + 4 + realPictureBox.Location.X + 8, 12 + 23 + realPictureBox.Location.Y + 8), window)!.Value;
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

    /// <summary>A form with a MenuStrip holding one ToolStripMenuItem (FileMenu), on top of
    /// everything <see cref="BuildFreshSource"/> already has.</summary>
    private static string BuildFreshSourceWithMenu()
    {
        var document = new WinFormsDesignerDocument(BuildFreshSource());
        document = new WinFormsDesignerDocument(document.AddControl("MenuStrip1", "Global.System.Windows.Forms.MenuStrip"));
        document = new WinFormsDesignerDocument(
            document.Menus.Set("MenuStrip1", [new WinFormsMenuItem("FileMenu", "MenuStrip1", "&File")]));
        return document.Source;
    }

    /// <summary>A plain ToolStrip (no MenuStrip) whose only item is a ToolStripDropDownButton
    /// owning one nested ToolStripMenuItem (OpenItem) in its own cascading DropDownItems tree,
    /// the shape <see cref="WinFormsDesignerView.ToolStripDropDownInline"/> edits and
    /// <see cref="WinFormsToolStripDropDownPreview"/> materializes live.</summary>
    private static string BuildFreshSourceWithDropDown()
    {
        var document = new WinFormsDesignerDocument(FreshSource);
        document = new WinFormsDesignerDocument(document.ToolStripItems.Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripDropDownButton1", "ToolStripDropDownButton", "Menu")]));
        document = new WinFormsDesignerDocument(
            document.Menus.Set("ToolStripDropDownButton1", [new WinFormsMenuItem("OpenItem", "ToolStripDropDownButton1", "Open")]));
        return document.Source;
    }

    /// <summary>
    /// <see cref="WinFormsDesignerView.MenuItemProperties"/>'s <c>OpenMenuItemPropertyEditor</c>
    /// had no Image/Icon branch at all before this fix: opening "…" on a MenuStrip item's Image
    /// fell through to the Color/Font checks and did nothing. This proves the same "Select
    /// Resource" round trip <see cref="ToolStripButtonImageRoundTripsThroughTheItemPropertiesPathAndPaintsRed"/>
    /// already proves for a flat ToolStrip item works for a MenuStrip item too, including the
    /// live preview (<see cref="WinFormsCollectionPreview"/>'s MenuStrip branch already resolved
    /// Local resources; this is the same statement shape, just reached from a new editor).
    /// </summary>
    [AvaloniaFact]
    public async Task MenuItemImageRoundTripsThroughTheItemPropertiesPathAndPaintsRed()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", BuildFreshSourceWithMenu(), true);
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
            surface.Select(surface.RootForm!.Controls.OfType<Forms.MenuStrip>().Single());

            var editor = Assert.Single(view.GetVisualDescendants().OfType<WinFormsMenuDesignEditor>());
            var itemButton = Assert.Single(editor.GetVisualDescendants().OfType<Button>(),
                b => b.Name == "MenuInlineItem_FileMenu");
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
                designer.Text.Contains("FileMenu.Image", StringComparison.Ordinal) && view.IsEnabled);

            Assert.Contains(
                "Me.FileMenu.Image = CType(resources.GetObject(\"FileMenu.Image\"), System.Drawing.Image)",
                designer.Text);
            Assert.True(WinFormsImageResources.TryDecode(
                new WinFormsResourceDocument(resourceBuffer!.Text).TryGet("FileMenu.Image")!, out var decoded));
            Assert.Equal(redPng, decoded);
            // The commit re-parses and re-instantiates the whole form, so the live control
            // behind "editor"/the menu from before the edit is stale; the current one is read again.
            var realMenu = surface.RootForm!.Controls.OfType<Forms.MenuStrip>().Single();
            var realItem = realMenu.Items.Cast<Forms.ToolStripItem>().Single();
            Assert.NotNull(realItem.Image);

            var afterImport = designer.Text;
            ClickUndo(view);
            await WaitUntilAsync(() =>
                !designer.Text.Contains("FileMenu.Image", StringComparison.Ordinal) && view.IsEnabled);
            Assert.DoesNotContain("FileMenu.Image", designer.Text);
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
    /// Proves the ToolStrip item path <see cref="WinFormsImageResourceDialog"/>'s "Project
    /// resource file" radio can now reach the same way the PictureBox test
    /// (<see cref="ChoosingAProjectResourceOnAPictureBoxWritesTheGlobalAssignment"/>) already
    /// proves for an ordinary control, and that the live strip actually paints the picture:
    /// before this fix, <see cref="WinFormsCollectionPreview"/>'s item loop only ever resolved
    /// a Local resource's "CType(resources.GetObject(...))" shape, never the plain
    /// "Global.RootNamespace.My.Resources.Resources.Name" a Project resource writes.
    /// </summary>
    /// <summary>
    /// A Resources.resx the preview cannot parse leaves the item without its picture,
    /// not the whole form without a preview: the preview is rebuilt after every edit, so
    /// a failure there would make every form in the project uneditable.
    /// </summary>
    [AvaloniaFact]
    public async Task AMalformedProjectResourceFileDoesNotBreakTheFormsPreview()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-bad-resx-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "App.vbproj"), VbprojWithRootNamespace("Contoso.App"));
            Directory.CreateDirectory(Path.Combine(root, "My Project"));
            File.WriteAllText(Path.Combine(root, "My Project", "Resources.resx"), "<root><data name=");

            var source = new WinFormsDesignerDocument(BuildFreshSource()).ToolStripItems
                .SetItemProperty("ToolStrip1", "ToolStripButton1", "Image", "Global.Contoso.App.My.Resources.Resources.Logo");
            var designer = new EditorDocumentViewModel(Path.Combine(root, "MainForm.Designer.vb"), source, true);

            using var view = new WinFormsDesignerView(designer);
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
                await WaitUntilAsync(() => surface.RootForm is not null);

                var item = surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single().Items.Cast<Forms.ToolStripItem>().Single();
                Assert.Equal("ToolStripButton1", item.Name);
                Assert.Null(item.Image);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ToolStripButtonImageSupportsProjectResourceFile()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-toolstrip-project-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "App.vbproj");
            File.WriteAllText(projectPath, VbprojWithRootNamespace("Contoso.App"));
            var imported = WinFormsProjectResources.Import(projectPath, WriteTempPng(SolidPng(Drawing.Color.Blue)), isIcon: false);

            var designerPath = Path.Combine(root, "MainForm.Designer.vb");
            var designer = new EditorDocumentViewModel(designerPath, BuildFreshSource(), true);

            using var view = new WinFormsDesignerView(designer);
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

                var projectRadio = Assert.Single(dialog.GetVisualDescendants().OfType<RadioButton>(),
                    r => Equals(r.Content, Localizer.Get(StringKeys.WinFormsImageDialogProjectResource)));
                Assert.True(projectRadio.IsEnabled);
                projectRadio.IsChecked = true;

                var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WinFormsImageDialogProjectList");
                var entry = list.ItemsSource!.Cast<WinFormsProjectResourceEntry>().Single(e => e.Name == imported.Name);
                list.SelectedItem = entry;

                ClickOk(dialog);
                await WaitUntilAsync(() =>
                    designer.Text.Contains($"Global.Contoso.App.My.Resources.Resources.{imported.Name}", StringComparison.Ordinal)
                    && view.IsEnabled);

                Assert.Contains($"Me.ToolStripButton1.Image = Global.Contoso.App.My.Resources.Resources.{imported.Name}", designer.Text);
                Assert.DoesNotContain("resources.GetObject", designer.Text, StringComparison.Ordinal);

                // The live strip must actually resolve the Project resource for its preview,
                // not just parse the statement: WinFormsCollectionPreview.EvaluateItemProperty
                // has to understand this expression shape too, not only a Local resource's.
                var realStrip = surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single();
                var realItem = realStrip.Items.Cast<Forms.ToolStripItem>().Single();
                Assert.NotNull(realItem.Image);

                var afterImport = designer.Text;
                ClickUndo(view);
                await WaitUntilAsync(() => designer.Text == BuildFreshSource() && view.IsEnabled);
                Assert.DoesNotContain("ToolStripButton1.Image", designer.Text);

                ClickRedo(view);
                await WaitUntilAsync(() => designer.Text == afterImport && view.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(root);
        }
    }

    /// <summary>The MenuStrip item equivalent of <see cref="ToolStripButtonImageSupportsProjectResourceFile"/>:
    /// <see cref="WinFormsDesignerView.MenuItemProperties"/> had no way to reach "Project resource
    /// file" at all (it had no Image branch whatsoever), and <see cref="WinFormsCollectionPreview"/>'s
    /// MenuStrip branch needs the same expression-shape fix the ToolStrip branch needed.</summary>
    [AvaloniaFact]
    public async Task MenuItemImageSupportsProjectResourceFile()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-menu-project-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "App.vbproj");
            File.WriteAllText(projectPath, VbprojWithRootNamespace("Contoso.App"));
            var imported = WinFormsProjectResources.Import(projectPath, WriteTempPng(SolidPng(Drawing.Color.Blue)), isIcon: false);

            var designerPath = Path.Combine(root, "MainForm.Designer.vb");
            var designer = new EditorDocumentViewModel(designerPath, BuildFreshSourceWithMenu(), true);

            using var view = new WinFormsDesignerView(designer);
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
                await WaitUntilAsync(() => surface.RootForm is not null);
                surface.Select(surface.RootForm!.Controls.OfType<Forms.MenuStrip>().Single());

                var editor = Assert.Single(view.GetVisualDescendants().OfType<WinFormsMenuDesignEditor>());
                var itemButton = Assert.Single(editor.GetVisualDescendants().OfType<Button>(),
                    b => b.Name == "MenuInlineItem_FileMenu");
                itemButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
                await WaitUntilAsync(() => grid.Properties.Any(p => p.Name == "Image"));
                var row = FindRow(grid, "Image");
                var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
                ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
                var dialog = window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single();

                var projectRadio = Assert.Single(dialog.GetVisualDescendants().OfType<RadioButton>(),
                    r => Equals(r.Content, Localizer.Get(StringKeys.WinFormsImageDialogProjectResource)));
                Assert.True(projectRadio.IsEnabled);
                projectRadio.IsChecked = true;

                var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WinFormsImageDialogProjectList");
                var entry = list.ItemsSource!.Cast<WinFormsProjectResourceEntry>().Single(e => e.Name == imported.Name);
                list.SelectedItem = entry;

                ClickOk(dialog);
                await WaitUntilAsync(() =>
                    designer.Text.Contains($"Global.Contoso.App.My.Resources.Resources.{imported.Name}", StringComparison.Ordinal)
                    && view.IsEnabled);

                Assert.Contains($"Me.FileMenu.Image = Global.Contoso.App.My.Resources.Resources.{imported.Name}", designer.Text);
                Assert.DoesNotContain("resources.GetObject", designer.Text, StringComparison.Ordinal);

                var realMenu = surface.RootForm!.Controls.OfType<Forms.MenuStrip>().Single();
                var realItem = realMenu.Items.Cast<Forms.ToolStripItem>().Single();
                Assert.NotNull(realItem.Image);

                var afterImport = designer.Text;
                ClickUndo(view);
                await WaitUntilAsync(() => designer.Text == BuildFreshSourceWithMenu() && view.IsEnabled);
                Assert.DoesNotContain("FileMenu.Image", designer.Text);

                ClickRedo(view);
                await WaitUntilAsync(() => designer.Text == afterImport && view.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(root);
        }
    }

    /// <summary>
    /// The nested drop-down item equivalent of <see cref="MenuItemImageRoundTripsThroughTheItemPropertiesPathAndPaintsRed"/>:
    /// <see cref="WinFormsDesignerView.ToolStripDropDownInline"/>'s <c>OpenToolStripDropDownItemPropertyEditor</c>
    /// had no Image/Icon branch at all before this fix, so "…" on a nested item's Image fell
    /// through to the Color/Font checks and did nothing. Drives the owner's own selection, then
    /// the nested panel's own selection, exactly the way
    /// <see cref="WinFormsToolStripDropDownInlineTests"/> already proves for adding/deleting a
    /// nested item.
    /// </summary>
    [AvaloniaFact]
    public async Task DropDownItemImageRoundTripsThroughTheItemPropertiesPathAndPaintsRed()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", BuildFreshSourceWithDropDown(), true);
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
            surface.Select(surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single());

            var editor = Assert.Single(view.GetVisualDescendants().OfType<WinFormsToolStripDesignEditor>());
            var ownerButton = Assert.Single(editor.GetVisualDescendants().OfType<Button>(),
                b => b.Name == "ToolStripInlineItem_ToolStripDropDownButton1");
            ownerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var panelHost = Assert.Single(editor.GetVisualDescendants().OfType<Border>(), b => b.Name == "ToolStripDropDownHost");
            var itemButton = Assert.Single(panelHost.GetVisualDescendants().OfType<Button>(),
                b => b.Name == "ToolStripDropDownItem_OpenItem");
            itemButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

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
                designer.Text.Contains("OpenItem.Image", StringComparison.Ordinal) && view.IsEnabled);

            Assert.Contains(
                "Me.OpenItem.Image = CType(resources.GetObject(\"OpenItem.Image\"), System.Drawing.Image)",
                designer.Text);
            Assert.True(WinFormsImageResources.TryDecode(
                new WinFormsResourceDocument(resourceBuffer!.Text).TryGet("OpenItem.Image")!, out var decoded));
            Assert.Equal(redPng, decoded);
            // The commit re-parses and re-instantiates the whole form, so the live owner/item from
            // before the edit is stale; the current ones are read again.
            var realStrip = surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single();
            var realOwner = Assert.IsAssignableFrom<Forms.ToolStripDropDownItem>(
                Assert.Single(realStrip.Items.Cast<Forms.ToolStripItem>()));
            var realItem = Assert.Single(realOwner.DropDownItems.Cast<Forms.ToolStripItem>());
            Assert.NotNull(realItem.Image);

            var afterImport = designer.Text;
            ClickUndo(view);
            await WaitUntilAsync(() =>
                !designer.Text.Contains("OpenItem.Image", StringComparison.Ordinal) && view.IsEnabled);
            Assert.DoesNotContain("OpenItem.Image", designer.Text);
            Assert.Empty(new WinFormsResourceDocument(resourceBuffer.Text).Entries);

            ClickRedo(view);
            await WaitUntilAsync(() => designer.Text == afterImport && view.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The nested drop-down item equivalent of <see cref="MenuItemImageSupportsProjectResourceFile"/>:
    /// before this fix, <see cref="WinFormsToolStripDropDownPreview"/>'s materializer only ever
    /// resolved a Local resource's "CType(resources.GetObject(...))" shape for a nested item,
    /// never a Project resource's plain "Global.RootNamespace.My.Resources.Resources.Name".</summary>
    [AvaloniaFact]
    public async Task DropDownItemImageSupportsProjectResourceFile()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-dropdown-project-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "App.vbproj");
            File.WriteAllText(projectPath, VbprojWithRootNamespace("Contoso.App"));
            var imported = WinFormsProjectResources.Import(projectPath, WriteTempPng(SolidPng(Drawing.Color.Blue)), isIcon: false);

            var designerPath = Path.Combine(root, "MainForm.Designer.vb");
            var designer = new EditorDocumentViewModel(designerPath, BuildFreshSourceWithDropDown(), true);

            using var view = new WinFormsDesignerView(designer);
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
                await WaitUntilAsync(() => surface.RootForm is not null);
                surface.Select(surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single());

                var editor = Assert.Single(view.GetVisualDescendants().OfType<WinFormsToolStripDesignEditor>());
                var ownerButton = Assert.Single(editor.GetVisualDescendants().OfType<Button>(),
                    b => b.Name == "ToolStripInlineItem_ToolStripDropDownButton1");
                ownerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var panelHost = Assert.Single(editor.GetVisualDescendants().OfType<Border>(), b => b.Name == "ToolStripDropDownHost");
                var itemButton = Assert.Single(panelHost.GetVisualDescendants().OfType<Button>(),
                    b => b.Name == "ToolStripDropDownItem_OpenItem");
                itemButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
                await WaitUntilAsync(() => grid.Properties.Any(p => p.Name == "Image"));
                var row = FindRow(grid, "Image");
                var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
                ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
                var dialog = window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Single();

                var projectRadio = Assert.Single(dialog.GetVisualDescendants().OfType<RadioButton>(),
                    r => Equals(r.Content, Localizer.Get(StringKeys.WinFormsImageDialogProjectResource)));
                Assert.True(projectRadio.IsEnabled);
                projectRadio.IsChecked = true;

                var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WinFormsImageDialogProjectList");
                var entry = list.ItemsSource!.Cast<WinFormsProjectResourceEntry>().Single(e => e.Name == imported.Name);
                list.SelectedItem = entry;

                ClickOk(dialog);
                await WaitUntilAsync(() =>
                    designer.Text.Contains($"Global.Contoso.App.My.Resources.Resources.{imported.Name}", StringComparison.Ordinal)
                    && view.IsEnabled);

                Assert.Contains($"Me.OpenItem.Image = Global.Contoso.App.My.Resources.Resources.{imported.Name}", designer.Text);
                Assert.DoesNotContain("resources.GetObject", designer.Text, StringComparison.Ordinal);

                // The live owner must actually resolve the Project resource for its preview, not
                // just parse the statement: WinFormsToolStripDropDownPreview has to understand
                // this expression shape too, not only a Local resource's.
                var realStrip = surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single();
                var realOwner = Assert.IsAssignableFrom<Forms.ToolStripDropDownItem>(
                    Assert.Single(realStrip.Items.Cast<Forms.ToolStripItem>()));
                var realItem = Assert.Single(realOwner.DropDownItems.Cast<Forms.ToolStripItem>());
                Assert.NotNull(realItem.Image);

                var afterImport = designer.Text;
                ClickUndo(view);
                await WaitUntilAsync(() => designer.Text == BuildFreshSourceWithDropDown() && view.IsEnabled);
                Assert.DoesNotContain("OpenItem.Image", designer.Text);

                ClickRedo(view);
                await WaitUntilAsync(() => designer.Text == afterImport && view.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(root);
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

    /// <summary>A vbproj with a &lt;RootNamespace&gt;, so a project resource's assignment names it explicitly.</summary>
    private static string VbprojWithRootNamespace(string rootNamespace) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace>{rootNamespace}</RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    /// <summary>
    /// Proves the designer can find the .vbproj above a form on disk, list an already-imported
    /// project resource, and write the plain "Global.RootNamespace.My.Resources.Resources.Name"
    /// assignment Visual Studio writes for it — the path
    /// <see cref="WinFormsImageResourceDialog"/>'s "Project resource file" radio could not reach
    /// before this fix, because it was always disabled.
    /// </summary>
    [AvaloniaFact]
    public async Task ChoosingAProjectResourceOnAPictureBoxWritesTheGlobalAssignment()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-project-test-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "App.vbproj");
            File.WriteAllText(projectPath, VbprojWithRootNamespace("Contoso.App"));
            var imported = WinFormsProjectResources.Import(projectPath, WriteTempPng(SolidPng(Drawing.Color.Blue)), isIcon: false);

            var designerPath = Path.Combine(root, "MainForm.Designer.vb");
            var designer = new EditorDocumentViewModel(designerPath, BuildFreshSource(), true);

            using var view = new WinFormsDesignerView(designer);
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

                // "Project resource file" is enabled: MainForm.Designer.vb sits inside App.vbproj.
                var projectRadio = Assert.Single(dialog.GetVisualDescendants().OfType<RadioButton>(),
                    r => Equals(r.Content, Localizer.Get(StringKeys.WinFormsImageDialogProjectResource)));
                Assert.True(projectRadio.IsEnabled);
                projectRadio.IsChecked = true;

                var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WinFormsImageDialogProjectList");
                var entry = list.ItemsSource!.Cast<WinFormsProjectResourceEntry>().Single(e => e.Name == imported.Name);
                list.SelectedItem = entry;

                ClickOk(dialog);
                await WaitUntilAsync(() =>
                    designer.Text.Contains($"Global.Contoso.App.My.Resources.Resources.{imported.Name}", StringComparison.Ordinal)
                    && view.IsEnabled);

                Assert.Contains($"Me.PictureBox1.Image = Global.Contoso.App.My.Resources.Resources.{imported.Name}", designer.Text);
                // Nothing of the resx-backed path leaked in: no ComponentResourceManager, no resx key.
                Assert.DoesNotContain("resources.GetObject", designer.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(root);
        }
    }

    /// <summary>
    /// Switching an already resource-backed PictureBox to a project resource must remove the
    /// local resx entry the property had (Visual Studio leaves no orphaned entry behind) and
    /// write the project assignment in the same step.
    /// </summary>
    [AvaloniaFact]
    public async Task ChoosingAProjectResourceRemovesTheLocalResxEntryItReplaces()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-project-switch-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "App.vbproj");
            File.WriteAllText(projectPath, VbprojWithRootNamespace("Contoso.App"));
            var imported = WinFormsProjectResources.Import(projectPath, WriteTempPng(SolidPng(Drawing.Color.Green)), isIcon: false);

            var (designerSource, resourcesSource) = BuildVisualStudioAuthoredSource();
            var designerPath = Path.Combine(root, "MainForm.Designer.vb");
            var designer = new EditorDocumentViewModel(designerPath, designerSource, true);
            var resourceBuffer = new EditorDocumentViewModel(Path.Combine(root, "MainForm.resx"), resourcesSource);

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

                // Starts showing the Local resource Visual Studio already assigned.
                Assert.NotNull(dialog.SelectedBytes);

                var projectRadio = Assert.Single(dialog.GetVisualDescendants().OfType<RadioButton>(),
                    r => Equals(r.Content, Localizer.Get(StringKeys.WinFormsImageDialogProjectResource)));
                Assert.True(projectRadio.IsEnabled);
                projectRadio.IsChecked = true;
                var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WinFormsImageDialogProjectList");
                var entry = list.ItemsSource!.Cast<WinFormsProjectResourceEntry>().Single(e => e.Name == imported.Name);
                list.SelectedItem = entry;

                ClickOk(dialog);
                await WaitUntilAsync(() =>
                    designer.Text.Contains($"Global.Contoso.App.My.Resources.Resources.{imported.Name}", StringComparison.Ordinal)
                    && view.IsEnabled);

                Assert.Contains($"Me.PictureBox1.Image = Global.Contoso.App.My.Resources.Resources.{imported.Name}", designer.Text);
                Assert.DoesNotContain("resources.GetObject", designer.Text, StringComparison.Ordinal);
                Assert.Null(new WinFormsResourceDocument(resourceBuffer.Text).TryGet("PictureBox1.Image"));

                // One undo step covers both the removal and the new assignment.
                ClickUndo(view);
                await WaitUntilAsync(() => designer.Text == designerSource && view.IsEnabled);
                Assert.Equal(resourcesSource, resourceBuffer.Text);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(root);
        }
    }

    /// <summary>
    /// A vbproj with no &lt;RootNamespace&gt; element at all defaults to the project's file
    /// name (the MSBuild default); an explicit but empty one is a deliberate "no root
    /// namespace" in Visual Basic instead, and must not leave a stray "Global..My.Resources"
    /// with a double dot in the statement the designer writes.
    /// </summary>
    private static string VbprojWithEmptyRootNamespace() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace></RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    [AvaloniaFact]
    public async Task ChoosingAProjectResourceWithAnExplicitlyEmptyRootNamespaceWritesTheBareGlobalAssignment()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-project-empty-namespace-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "App.vbproj");
            File.WriteAllText(projectPath, VbprojWithEmptyRootNamespace());
            var imported = WinFormsProjectResources.Import(projectPath, WriteTempPng(SolidPng(Drawing.Color.Blue)), isIcon: false);

            var designerPath = Path.Combine(root, "MainForm.Designer.vb");
            var designer = new EditorDocumentViewModel(designerPath, BuildFreshSource(), true);

            using var view = new WinFormsDesignerView(designer);
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

                var projectRadio = Assert.Single(dialog.GetVisualDescendants().OfType<RadioButton>(),
                    r => Equals(r.Content, Localizer.Get(StringKeys.WinFormsImageDialogProjectResource)));
                projectRadio.IsChecked = true;
                var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WinFormsImageDialogProjectList");
                var entry = list.ItemsSource!.Cast<WinFormsProjectResourceEntry>().Single(e => e.Name == imported.Name);
                list.SelectedItem = entry;

                ClickOk(dialog);
                await WaitUntilAsync(() =>
                    designer.Text.Contains($"Global.My.Resources.Resources.{imported.Name}", StringComparison.Ordinal)
                    && view.IsEnabled);

                Assert.Contains($"Me.PictureBox1.Image = Global.My.Resources.Resources.{imported.Name}", designer.Text);
                // No double dot where the (empty) root namespace would otherwise have gone.
                Assert.DoesNotContain("Global..My.Resources", designer.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(root);
        }
    }

    /// <summary>
    /// The crash this guards against: before this fix, <see cref="WinFormsImageResourceDialog"/>
    /// caught only IOException/UnauthorizedAccessException around
    /// <see cref="WinFormsProjectResources.Import"/>, so a Resources.Designer.vb this method
    /// cannot find its way around (hand-edited into something broken, not merely reformatted)
    /// threw a FormatException straight through the dialog's Click handler instead of being
    /// shown as an error. Nothing must be written when that happens, either.
    /// </summary>
    [AvaloniaFact]
    public async Task ImportingAProjectResourceWithAMalformedDesignerVbShowsAnErrorAndChangesNothing()
    {
        var root = Directory.CreateTempSubdirectory("basalt-image-project-malformed-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "App.vbproj");
            File.WriteAllText(projectPath, VbprojWithRootNamespace("Contoso.App"));
            var designerVbPath = WinFormsProjectResources.ResourcesDesignerVbPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(designerVbPath)!);
            const string malformedDesignerVb = """
                Namespace My.Resources
                    Friend Module Resources
                        ' Hand-edited: the closing "End Module" was deleted by mistake.
                End Namespace
                """;
            File.WriteAllText(designerVbPath, malformedDesignerVb);

            var owner = new Window();
            owner.Show();
            try
            {
                var pngPath = WriteTempPng(SolidPng(Drawing.Color.Red));
                var dialog = new WinFormsImageResourceDialog(
                    currentBytes: null, isIcon: false, importFile: () => Task.FromResult<string?>(pngPath), projectPath: projectPath);
                var resultTask = dialog.ShowDialog<bool>(owner);
                Dispatcher.UIThread.RunJobs();

                var projectRadio = Assert.Single(dialog.GetVisualDescendants().OfType<RadioButton>(),
                    r => Equals(r.Content, Localizer.Get(StringKeys.WinFormsImageDialogProjectResource)));
                projectRadio.IsChecked = true;

                Click(dialog, "WinFormsImageDialogProjectImport");
                await WaitUntilAsync(() => FindError(dialog).IsVisible);

                Assert.Contains("Resources.Designer.vb", FindError(dialog).Text);
                var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WinFormsImageDialogProjectList");
                Assert.Empty(list.ItemsSource!.Cast<WinFormsProjectResourceEntry>());

                ClickCancel(dialog);
                Assert.False(await resultTask);
            }
            finally
            {
                owner.Close();
            }

            // Nothing was written: the malformed Designer.vb survives untouched, and no
            // Resources.resx (or copied file) was created for the rejected import.
            Assert.Equal(malformedDesignerVb, File.ReadAllText(designerVbPath));
            Assert.False(File.Exists(WinFormsProjectResources.ResourcesResxPath(projectPath)));
            Assert.False(Directory.Exists(WinFormsProjectResources.ImportedFilesDirectory(projectPath)));
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(root);
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
