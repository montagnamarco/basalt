using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Core;

/// <summary>Painting, proved by drawing controls into bitmaps and reading their pixels.</summary>
public class PaintTests
{
    private const int WS_EX_CLIENTEDGE = 0x00000200;

    private sealed class Sunken : Control
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_CLIENTEDGE;

                return parameters;
            }
        }
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void A_control_fills_its_background_and_children_draw_over_it_in_z_order()
    {
        var parent = new Control { Size = new Size(40, 30), BackColor = Color.Red };
        var back = new Control { Bounds = new Rectangle(5, 5, 20, 20), BackColor = Color.Blue, Parent = parent };
        var front = new Control { Bounds = new Rectangle(15, 5, 20, 20), BackColor = Color.Lime };
        parent.Controls.Add(front);
        front.BringToFront();

        using var bitmap = Draw(parent);

        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(20, 10).ToArgb());
        Assert.Same(back, parent.Controls[1]);
    }

    [Fact]
    public void Children_are_clipped_to_their_parents_client_area()
    {
        var parent = new Control { Size = new Size(40, 30), BackColor = Color.Red };
        var panel = new Control { Bounds = new Rectangle(0, 0, 20, 30), BackColor = Color.White, Parent = parent };
        _ = new Control { Bounds = new Rectangle(10, 0, 30, 30), BackColor = Color.Blue, Parent = panel };

        using var bitmap = Draw(parent);

        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(15, 10).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(25, 10).ToArgb());
    }

    [Fact]
    public void A_paint_handler_draws_in_client_coordinates_inside_the_frame()
    {
        var control = new Sunken { Size = new Size(30, 30), BackColor = Color.White };
        control.Paint += (_, e) => e.Graphics.FillRectangle(Brushes.Black, 0, 0, 1, 1);

        using var bitmap = Draw(control);

        // The two-pixel sunken edge, then the client area's first pixel.
        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(SystemColors.ControlDarkDark.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
        Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(29, 29).ToArgb());
    }

    [Fact]
    public void Only_the_invalid_part_is_painted_again_and_the_rest_is_kept()
    {
        var control = new Control { Size = new Size(40, 20), BackColor = Color.White };
        var color = Color.Red;
        var clips = new List<Rectangle>();
        control.Paint += (_, e) =>
        {
            clips.Add(e.ClipRectangle);
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, control.ClientRectangle);
        };

        using (Draw(control))
        {
        }

        color = Color.Blue;
        control.Invalidate(new Rectangle(20, 0, 20, 20));
        using var bitmap = Draw(control);

        Assert.Equal([new Rectangle(0, 0, 40, 20), new Rectangle(20, 0, 20, 20)], clips);
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), bitmap.GetPixel(30, 5).ToArgb());
    }

    [Fact]
    public void A_transparent_child_shows_its_parents_paint()
    {
        var parent = new Control { Size = new Size(40, 20), BackColor = Color.White };
        parent.Paint += (_, e) => e.Graphics.FillRectangle(Brushes.Green, 0, 0, 40, 10);
        var child = new TransparentControl { Bounds = new Rectangle(10, 5, 20, 10), Parent = parent };

        using var bitmap = Draw(parent);

        Assert.Equal(Color.Green.ToArgb(), bitmap.GetPixel(15, 7).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(15, 12).ToArgb());
        Assert.NotNull(child.Parent);
    }

    private sealed class TransparentControl : Control
    {
        public TransparentControl()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
    }

    [Fact]
    public void Hidden_children_are_not_drawn()
    {
        var parent = new Control { Size = new Size(20, 20), BackColor = Color.White };
        _ = new Control { Bounds = new Rectangle(0, 0, 20, 20), BackColor = Color.Black, Visible = false, Parent = parent };

        using var bitmap = Draw(parent);

        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
    }
}
