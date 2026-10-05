using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public sealed class PropertyGridAndRichTextVisualStyleTests
{
    [AvaloniaFact]
    public void RichTextBoxKeepsInheritedBackgroundInEveryPortableProfile()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateForm(platform);
            using var editor = new RichTextBox { Size = new Size(160, 64), Text = "Word" };
            form.Controls.Add(editor);

            form.BackColor = Color.Magenta;
            using (var inherited = Draw(editor))
            {
                AssertPixel(inherited, 120, 40, Color.Magenta);
            }

            editor.BackColor = Color.Cyan;
            using (var own = Draw(editor))
            {
                AssertPixel(own, 120, 40, Color.Cyan);
            }

            editor.ResetBackColor();
            editor.ReadOnly = true;
            using var readOnly = Draw(editor);
            AssertPixel(readOnly, 120, 40, Color.Magenta);
        }
    }

    [AvaloniaFact]
    public void PropertyGridMarginAndToolbarGlyphFollowProfileAndExplicitAppearance()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var form = CreateForm(platform);
            using var grid = CreateGrid();
            form.Controls.Add(grid);

            var palette = PortableVisualStyles.Palette(platform);
            using (var themed = Draw(grid))
            {
                AssertPixel(themed, MarginCenter(grid).X, MarginCenter(grid).Y, palette.Surface);
                AssertPixel(themed, GlyphPoint(grid).X, GlyphPoint(grid).Y, palette.Text);
            }

            grid.BackColor = Color.Magenta;
            grid.ViewForeColor = Color.Green;
            using (var custom = Draw(grid))
            {
                AssertPixel(custom, MarginCenter(grid).X, MarginCenter(grid).Y, Color.Magenta);
                AssertPixel(custom, GlyphPoint(grid).X, GlyphPoint(grid).Y, Color.Green);
            }

            grid.Enabled = false;
            using var disabled = Draw(grid);
            AssertPixel(disabled, GlyphPoint(grid).X, GlyphPoint(grid).Y, grid.DisabledItemForeColor);
        }
    }

    [AvaloniaFact]
    public void PropertyGridClassicOptOutKeepsClassicMarginAndGlyph()
    {
        using var form = new Form { VisualStylesOverride = false };
        using var grid = CreateGrid();
        form.Controls.Add(grid);

        using var bitmap = Draw(grid);
        AssertPixel(bitmap, MarginCenter(grid).X, MarginCenter(grid).Y, SystemColors.Control);
        AssertPixel(bitmap, GlyphPoint(grid).X, GlyphPoint(grid).Y, SystemColors.ControlText);
    }

    private static System.Windows.Forms.PropertyGrid CreateGrid() => new()
    {
        Size = new Size(220, 150),
        PropertySort = PropertySort.Alphabetical,
        SelectedObject = new GridSubject(),
    };

    private static Point MarginCenter(System.Windows.Forms.PropertyGrid grid)
    {
        Assert.NotEmpty(grid.VisibleRowsWithDepth());
        var margin = grid.MarginBounds(0);
        return new Point(margin.Left + margin.Width - 3, margin.Top + margin.Height / 2);
    }

    private static Point GlyphPoint(System.Windows.Forms.PropertyGrid grid)
    {
        var button = grid.AlphabeticalButtonBounds;
        return new Point(button.Left + 5, button.Top + 4);
    }

    private static Form CreateForm(PortableVisualStylePlatform platform) => new()
    {
        VisualStylesOverride = true,
        VisualStylePlatformOverride = platform,
        FormBorderStyle = FormBorderStyle.None,
    };

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }

    private static void AssertPixel(Bitmap bitmap, int x, int y, Color expected) =>
        Assert.Equal(expected.ToArgb(), bitmap.GetPixel(x, y).ToArgb());

    private sealed class GridSubject
    {
        public string Caption { get; set; } = "Value";
    }
}
