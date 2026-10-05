using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// StatusStrip's sizing grip, a Spring label taking up leftover width, and
/// ToolStripStatusLabel's border sides and style.
/// </summary>
public class StatusStripTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void A_status_strip_docks_to_the_bottom_and_has_a_sizing_grip_by_default()
    {
        var strip = new StatusStrip { Font = Font };

        Assert.Equal(DockStyle.Bottom, strip.Dock);
        Assert.True(strip.SizingGrip);
    }

    [Fact]
    public void Stretch_uses_the_same_value_through_StatusStrip_and_ToolStrip()
    {
        var strip = new StatusStrip();
        ToolStrip baseStrip = strip;

        Assert.True(strip.Stretch);
        Assert.True(baseStrip.Stretch);

        strip.Stretch = false;
        Assert.False(baseStrip.Stretch);

        baseStrip.Stretch = true;
        Assert.True(strip.Stretch);
    }

    [Fact]
    public void The_sizing_grip_paints_in_the_trailing_bottom_corner_and_can_be_turned_off()
    {
        var strip = new StatusStrip { Font = Font, AutoSize = false, Size = new Size(200, 22), BackColor = Color.White };

        using var withGrip = Draw(strip);
        var gripArea = strip.SizeGripBounds;

        // The closest diagonal dot pair to the corner: a dark pixel immediately above-left of a light one.
        var gripPixel = withGrip.GetPixel(gripArea.Right - 5, gripArea.Bottom - 5);
        Assert.NotEqual(Color.White.ToArgb(), gripPixel.ToArgb());

        strip.SizingGrip = false;
        using var withoutGrip = Draw(strip);
        Assert.Equal(Color.White.ToArgb(), withoutGrip.GetPixel(gripArea.Right - 5, gripArea.Bottom - 5).ToArgb());
    }

    [Fact]
    public void A_default_item_is_a_ToolStripStatusLabel()
    {
        var strip = new StatusStrip { Font = Font };

        var item = strip.Items.Add("Ready");

        Assert.IsType<ToolStripStatusLabel>(item);
    }

    [Fact]
    public void A_spring_label_grows_to_use_the_width_its_siblings_leave()
    {
        var strip = new StatusStrip { Font = Font, AutoSize = false, Size = new Size(300, 22), SizingGrip = false };
        var left = new ToolStripStatusLabel("Left") { AutoSize = false, Size = new Size(50, 20) };
        var spring = new ToolStripStatusLabel("Middle") { AutoSize = false, Size = new Size(50, 20), Spring = true };
        var right = new ToolStripStatusLabel("Right") { AutoSize = false, Size = new Size(50, 20) };
        strip.Items.AddRange([left, spring, right]);

        strip.PerformLayout();

        // The spring label absorbs the width its siblings do not use, and the following
        // sibling (Right) is pushed along by exactly the width the spring label gained.
        Assert.True(spring.Bounds.Width > 50);
        Assert.Equal(spring.Bounds.Right, right.Bounds.Left);
    }

    [Fact]
    public void Spring_uses_the_space_up_to_the_grip_padding_once()
    {
        var strip = new StatusStrip { Font = Font, AutoSize = false, Size = new Size(300, 22) };
        var spring = new ToolStripStatusLabel("Ready")
        {
            AutoSize = false, Size = new Size(50, 20), Margin = Padding.Empty, Spring = true,
        };
        strip.Items.Add(spring);

        strip.PerformLayout();

        Assert.Equal(strip.DisplayRectangle.Right, spring.Bounds.Right);
        Assert.Equal(strip.SizeGripBounds.Left, spring.Bounds.Right);
    }

    [Fact]
    public void BorderSides_paints_a_line_only_on_the_requested_edges()
    {
        var strip = new StatusStrip { Font = Font, AutoSize = false, Size = new Size(200, 22), BackColor = Color.White };
        var label = new ToolStripStatusLabel("Item") { AutoSize = false, Size = new Size(100, 20), BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Sunken };
        strip.Items.Add(label);
        strip.PerformLayout();

        using var bitmap = Draw(strip);

        // The left edge has the border line; the right edge (not requested) stays the plain background.
        Assert.NotEqual(Color.White.ToArgb(), bitmap.GetPixel(label.Bounds.Left, label.Bounds.Top + 10).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(label.Bounds.Right - 1, label.Bounds.Top + 10).ToArgb());
    }
}
