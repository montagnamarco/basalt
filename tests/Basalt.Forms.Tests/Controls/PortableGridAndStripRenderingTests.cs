using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public sealed class PortableGridAndStripRenderingTests
{
    [AvaloniaFact]
    public void PlatformGridHeadersSelectionAndLinesUseActualProfilePixels()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var grid = CreateGrid(platform);
            grid.Rows[0].Cells[0].Selected = true;
            var palette = PortableVisualStyles.Palette(platform);
            using var bitmap = Draw(grid);
            var cell = grid.GetCellDisplayRectangle(0, 0, false);
            Assert.Equal(palette.Selection.ToArgb(), bitmap.GetPixel(cell.Left + 30, cell.Top + 8).ToArgb());
            Assert.Equal(palette.Border.ToArgb(), bitmap.GetPixel(cell.Right - 1, cell.Top + 8).ToArgb());
            if (platform == PortableVisualStylePlatform.Windows)
            {
                Assert.True(bitmap.GetPixel(60, 2).R > bitmap.GetPixel(60, grid.ColumnHeadersHeight - 3).R);
            }
            else
            {
                Assert.Equal(palette.Face.ToArgb(), bitmap.GetPixel(60, 10).ToArgb());
            }
            Assert.Equal(palette.Border.ToArgb(), bitmap.GetPixel(60, 0).ToArgb());
        }
    }

    [AvaloniaFact]
    public void ExplicitGridColorsAndPartialStyleAssignmentsSurviveTheProfile()
    {
        using var grid = CreateGrid(PortableVisualStylePlatform.MacOS);
        grid.DefaultCellStyle.ForeColor = Color.Red;
        grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.Magenta;
        grid.BackgroundColor = Color.Yellow;
        grid.GridColor = Color.Lime;
        grid.Rows[0].Cells[0].Selected = true;
        using var bitmap = Draw(grid);
        var cell = grid.GetCellDisplayRectangle(0, 0, false);
        Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(cell.Left + 30, cell.Top + 8).ToArgb());
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(60, 10).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(cell.Right - 1, cell.Top + 8).ToArgb());
        Assert.Equal(Color.Yellow.ToArgb(), bitmap.GetPixel(180, 100).ToArgb());
        Assert.Equal(Color.Red, grid.Rows[0].Cells[0].InheritedStyle.ForeColor);
        Assert.Equal(SystemColors.Window, grid.DefaultCellStyle.BackColor);
        Assert.Equal(SystemColors.Highlight, grid.DefaultCellStyle.SelectionBackColor);
    }

    [AvaloniaFact]
    public void ClassicGridAndDisabledHeaderVisualStylesKeepTheRaisedBevel()
    {
        using var grid = CreateGrid(PortableVisualStylePlatform.MacOS);
        grid.VisualStylesOverride = false;
        using var classic = Draw(grid);
        Assert.NotEqual(classic.GetPixel(60, 0), classic.GetPixel(60, grid.ColumnHeadersHeight - 1));
        grid.VisualStylesOverride = true;
        grid.EnableHeadersVisualStyles = false;
        using var disabled = Draw(grid);
        Assert.Equal(classic.GetPixel(60, 0), disabled.GetPixel(60, 0));
        Assert.Equal(classic.GetPixel(60, grid.ColumnHeadersHeight - 1), disabled.GetPixel(60, grid.ColumnHeadersHeight - 1));
    }

    [AvaloniaFact]
    public void BothDefaultStripRenderersUseProfilesAndCheckedButtonFaces()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            foreach (var renderer in new ToolStripRenderer[] { new ToolStripSystemRenderer(), new ToolStripProfessionalRenderer() })
            {
                using var container = new Panel { Size = new Size(200, 60) };
                using var strip = new ToolStrip
                {
                    Size = new Size(200, 30),
                    VisualStylesOverride = true,
                    VisualStylePlatformOverride = platform
                };
                var button = new ToolStripButton { AutoSize = false, Size = new Size(70, 24), Checked = true };
                strip.Items.Add(button);
                container.Controls.Add(strip);
                strip.PerformLayout();
                using var bitmap = new Bitmap(200, 30, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                renderer.DrawToolStripBackground(new ToolStripRenderEventArgs(graphics, strip));
                var palette = PortableVisualStyles.Palette(platform);
                Assert.Equal(palette.Face.ToArgb(), bitmap.GetPixel(180, 15).ToArgb());
                renderer.DrawButtonBackground(new ToolStripItemRenderEventArgs(graphics, button));
                var bounds = button.Bounds;
                if (platform != PortableVisualStylePlatform.Windows)
                {
                    Assert.Equal(palette.PressedFace.ToArgb(), bitmap.GetPixel(bounds.Left + 20, bounds.Top + 12).ToArgb());
                }
                else
                {
                    Assert.True(bitmap.GetPixel(bounds.Left + 20, bounds.Top + 3).R
                        > bitmap.GetPixel(bounds.Left + 20, bounds.Bottom - 4).R);
                }
            }
        }
    }

    [AvaloniaFact]
    public void ExplicitStripAppearanceCustomColorTablesAndRenderHandlersTakePrecedence()
    {
        using var container = new Panel { Size = new Size(200, 60) };
        using var strip = new ToolStrip
        {
            Size = new Size(200, 30),
            VisualStylesOverride = true,
            VisualStylePlatformOverride = PortableVisualStylePlatform.Linux,
            BackColor = Color.Yellow
        };
        var button = new ToolStripButton { AutoSize = false, Size = new Size(70, 24), BackColor = Color.Red, Checked = true };
        strip.Items.Add(button);
        container.Controls.Add(strip);
        strip.PerformLayout();
        using var bitmap = new Bitmap(200, 30, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var renderer = new ToolStripProfessionalRenderer();
        renderer.DrawToolStripBackground(new ToolStripRenderEventArgs(graphics, strip));
        Assert.Equal(Color.Yellow.ToArgb(), bitmap.GetPixel(180, 15).ToArgb());
        renderer.DrawButtonBackground(new ToolStripItemRenderEventArgs(graphics, button));
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(button.Bounds.Left + 20, button.Bounds.Top + 12).ToArgb());
        Assert.Equal(PortableVisualStyles.Palette(PortableVisualStylePlatform.Linux).Accent.ToArgb(),
            bitmap.GetPixel(button.Bounds.Left, button.Bounds.Top + 12).ToArgb());
        renderer.RenderButtonBackground += (_, args) => args.Graphics.FillRectangle(Brushes.Lime, args.Item.Bounds);
        renderer.DrawButtonBackground(new ToolStripItemRenderEventArgs(graphics, button));
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(button.Bounds.Left + 20, button.Bounds.Top + 12).ToArgb());

        var custom = new ToolStripProfessionalRenderer(new SolidColorTable());
        custom.DrawToolStripBackground(new ToolStripRenderEventArgs(graphics, strip));
        Assert.Equal(Color.Magenta.ToArgb(), bitmap.GetPixel(180, 15).ToArgb());
    }

    [AvaloniaFact]
    public void DisabledTextUsesProfilePixelsWhileExplicitFontColorAndTextHandlersSurvive()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var strip = new ToolStrip
            {
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform
            };
            using var font = new Font("Tahoma", 12);
            var button = new ToolStripButton { Enabled = false, Font = font };
            strip.Items.Add(button);
            using var bitmap = new Bitmap(120, 35, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            var renderer = new ToolStripSystemRenderer();
            var args = new ToolStripItemTextRenderEventArgs(graphics, button, "MMMM",
                new Rectangle(0, 0, 120, 35), button.ForeColor, button.Font, TextFormatFlags.Left);
            renderer.DrawItemText(args);
            Assert.Same(font, button.Font);
            Assert.Contains(Pixels(bitmap), color => color.ToArgb() == PortableVisualStyles.Palette(platform).DisabledText.ToArgb());

            button.ForeColor = Color.Magenta;
            args.TextColor = button.ForeColor;
            graphics.Clear(Color.White);
            renderer.DrawItemText(args);
            Assert.Contains(Pixels(bitmap), color => color.ToArgb() == Color.Magenta.ToArgb());
            renderer.RenderItemText += (_, value) => TextRenderer.DrawText(value.Graphics, value.Text,
                value.TextFont, value.TextRectangle, Color.Blue, value.TextFormat);
            graphics.Clear(Color.White);
            renderer.DrawItemText(args);
            Assert.Contains(Pixels(bitmap), color => color.ToArgb() == Color.Blue.ToArgb());
        }
    }

    [AvaloniaFact]
    public void DockedAutoSizeMenuAndStatusStripsKeepTheFormsAvailableWidth()
    {
        using var form = new Form { Size = new Size(480, 200), VisualStylesOverride = true };
        using var menu = new MenuStrip();
        using var status = new StatusStrip();
        menu.Items.Add(new ToolStripMenuItem("File"));
        status.Items.Add(new ToolStripStatusLabel("Ready"));
        form.Controls.Add(menu);
        form.Controls.Add(status);
        form.PerformLayout();
        menu.PerformLayout();
        status.PerformLayout();
        Assert.True(menu.AutoSize);
        Assert.True(status.AutoSize);
        Assert.Equal(form.DisplayRectangle.Width, menu.Width);
        Assert.Equal(form.DisplayRectangle.Width, status.Width);
        using var menuBitmap = Draw(menu);
        using var statusBitmap = Draw(status);
        var palette = PortableVisualStyles.Palette(menu.PortableVisualStylePlatform);
        Assert.Equal(palette.Face.ToArgb(), menuBitmap.GetPixel(menu.Width - 20, menu.Height / 2).ToArgb());
        Assert.Equal(palette.Face.ToArgb(), statusBitmap.GetPixel(status.Width - 30, status.Height / 2).ToArgb());
    }

    [AvaloniaFact]
    public void ExplicitAdvancedNoneStillSuppressesTheDefaultGridLines()
    {
        using var grid = CreateGrid(PortableVisualStylePlatform.MacOS);
        grid.Rows[0].Cells[0].Selected = true;
        grid.AdvancedCellBorderStyle.All = DataGridViewAdvancedCellBorderStyle.None;
        using var bitmap = Draw(grid);
        var cell = grid.GetCellDisplayRectangle(0, 0, false);
        Assert.Equal(PortableVisualStyles.Palette(PortableVisualStylePlatform.MacOS).Selection.ToArgb(),
            bitmap.GetPixel(cell.Right - 1, cell.Top + 8).ToArgb());
    }

    [AvaloniaFact]
    public void SelectedGridButtonKeepsAVisibleSelectionBandAroundItsFace()
    {
        using var grid = new System.Windows.Forms.DataGridView
        {
            Size = new Size(200, 100),
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            VisualStylesOverride = true,
            VisualStylePlatformOverride = PortableVisualStylePlatform.MacOS
        };
        grid.Columns.Add(new DataGridViewButtonColumn { Width = 150 });
        grid.Rows.Add();
        grid.Rows[0].Cells[0].Selected = true;
        using var bitmap = Draw(grid);
        var cell = grid.GetCellDisplayRectangle(0, 0, false);
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform.MacOS);
        Assert.Equal(palette.Selection.ToArgb(), bitmap.GetPixel(cell.Left + 1, cell.Top + 8).ToArgb());
        Assert.Equal(palette.Face.ToArgb(), bitmap.GetPixel(cell.Left + 30, cell.Top + 8).ToArgb());
    }

    [AvaloniaFact]
    public void CustomImageAndTextPaintingRemainOnTopOfDefaultPixels()
    {
        using var strip = new ToolStrip { VisualStylesOverride = true };
        var item = new ToolStripButton();
        strip.Items.Add(item);
        using var bitmap = new Bitmap(80, 30, PixelFormat.Format32bppArgb);
        using var image = new Bitmap(20, 20, PixelFormat.Format32bppArgb);
        using (var source = Graphics.FromImage(image))
        {
            source.Clear(Color.Red);
        }
        using var graphics = Graphics.FromImage(bitmap);
        var renderer = new ToolStripSystemRenderer();
        var imageCallbacks = 0;
        renderer.RenderItemImage += (_, args) =>
        {
            imageCallbacks++;
            args.Graphics.FillRectangle(Brushes.Lime, args.ImageRectangle);
        };
        renderer.DrawItemImage(new ToolStripItemImageRenderEventArgs(graphics, item, image, new Rectangle(0, 0, 20, 20)));
        Assert.Equal(1, imageCallbacks);
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        var textCallbacks = 0;
        renderer.RenderItemText += (_, args) =>
        {
            textCallbacks++;
            args.Graphics.FillRectangle(Brushes.Blue, args.TextRectangle);
        };
        renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(graphics, item, "MMMM",
            new Rectangle(0, 0, 80, 30), Color.Black, item.Font, TextFormatFlags.Left));
        Assert.Equal(1, textCallbacks);
        Assert.All(Pixels(bitmap), color => Assert.Equal(Color.Blue.ToArgb(), color.ToArgb()));
        strip.VisualStylesOverride = false;
        renderer.DrawItemImage(new ToolStripItemImageRenderEventArgs(graphics, item, image, new Rectangle(0, 0, 20, 20)));
        Assert.Equal(2, imageCallbacks);
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(graphics, item, "MMMM",
            new Rectangle(0, 0, 80, 30), Color.Black, item.Font, TextFormatFlags.Left));
        Assert.Equal(2, textCallbacks);
        Assert.All(Pixels(bitmap), color => Assert.Equal(Color.Blue.ToArgb(), color.ToArgb()));
    }

    [AvaloniaFact]
    public void CustomStripSurfaceRetainsVisibleSelectedPressedAndCheckedBorders()
    {
        using var strip = new ToolStrip
        {
            Size = new Size(150, 30),
            VisualStylesOverride = true,
            VisualStylePlatformOverride = PortableVisualStylePlatform.MacOS
        };
        var item = new ToolStripButton { AutoSize = false, Size = new Size(70, 24), BackColor = Color.Red };
        strip.Items.Add(item);
        strip.PerformLayout();
        using var bitmap = new Bitmap(150, 30, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var renderer = new ToolStripSystemRenderer();
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform.MacOS);
        foreach (var state in new[] { "Selected", "Pressed", "Checked" })
        {
            item.SetSelectedInternal(state == "Selected");
            item.SetPressedInternal(state == "Pressed");
            item.Checked = state == "Checked";
            renderer.DrawButtonBackground(new ToolStripItemRenderEventArgs(graphics, item));
            Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(item.Bounds.Left + 20, item.Bounds.Top + 12).ToArgb());
            Assert.Equal(palette.Accent.ToArgb(), bitmap.GetPixel(item.Bounds.Left, item.Bounds.Top + 12).ToArgb());
        }
        item.Enabled = false;
        renderer.DrawButtonBackground(new ToolStripItemRenderEventArgs(graphics, item));
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(item.Bounds.Left, item.Bounds.Top + 12).ToArgb());
    }

    private static IEnumerable<Color> Pixels(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                yield return bitmap.GetPixel(x, y);
            }
        }
    }

    private sealed class SolidColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Color.Magenta;
        public override Color ToolStripGradientEnd => Color.Magenta;
    }

    private static System.Windows.Forms.DataGridView CreateGrid(PortableVisualStylePlatform platform)
    {
        var grid = new System.Windows.Forms.DataGridView
        {
            Size = new Size(200, 140),
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            VisualStylesOverride = true,
            VisualStylePlatformOverride = platform
        };
        grid.Columns.Add("A", "");
        grid.Columns[0].Width = 150;
        grid.Rows.Add();
        return grid;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }
}
