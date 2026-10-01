using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Core;

/// <summary>Bounds, the client area inside the frame, and the events a change raises.</summary>
public class BoundsTests
{
    private sealed class Framed(int style, int exStyle) : Control
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.Style |= style;
                parameters.ExStyle |= exStyle;

                return parameters;
            }
        }
    }

    private const int WS_BORDER = 0x00800000;
    private const int WS_EX_CLIENTEDGE = 0x00000200;
    private const int WS_VSCROLL = 0x00200000;

    [Fact]
    public void The_client_area_is_what_the_frame_of_the_styles_leaves()
    {
        var plain = new Control { Size = new Size(100, 50) };
        var bordered = new Framed(WS_BORDER, 0) { Size = new Size(100, 50) };
        var sunken = new Framed(0, WS_EX_CLIENTEDGE) { Size = new Size(100, 50) };
        var scrolled = new Framed(0, WS_EX_CLIENTEDGE) { Size = new Size(100, 50) };
        var withScrollBar = new Framed(WS_VSCROLL, WS_EX_CLIENTEDGE) { Size = new Size(100, 50) };

        Assert.Equal(new Size(100, 50), plain.ClientSize);
        Assert.Equal(new Size(98, 48), bordered.ClientSize);
        Assert.Equal(new Size(96, 46), sunken.ClientSize);
        Assert.Equal(new Size(80, 46), withScrollBar.ClientSize);
    }

    [Fact]
    public void Setting_the_client_size_sizes_the_frame_around_it()
    {
        var sunken = new Framed(0, WS_EX_CLIENTEDGE) { ClientSize = new Size(200, 100) };

        Assert.Equal(new Size(204, 104), sunken.Size);
        Assert.Equal(new Size(200, 100), sunken.ClientSize);
    }

    [Fact]
    public void Moving_and_resizing_raise_their_events_in_the_WinForms_order()
    {
        var control = new Control();
        var order = new List<string>();
        control.Move += (_, _) => order.Add("Move");
        control.LocationChanged += (_, _) => order.Add("LocationChanged");
        control.Resize += (_, _) => order.Add("Resize");
        control.SizeChanged += (_, _) => order.Add("SizeChanged");
        control.ClientSizeChanged += (_, _) => order.Add("ClientSizeChanged");
        control.Layout += (_, _) => order.Add("Layout");

        control.Bounds = new Rectangle(1, 2, 30, 40);

        Assert.Equal(["Move", "LocationChanged", "Layout", "Resize", "SizeChanged", "ClientSizeChanged"], order);
    }

    [Fact]
    public void Minimum_and_maximum_sizes_constrain_and_correct_each_other()
    {
        var control = new Control { MinimumSize = new Size(50, 20), MaximumSize = new Size(100, 0) };

        control.Size = new Size(10, 10);
        Assert.Equal(new Size(50, 20), control.Size);

        control.Size = new Size(500, 500);
        Assert.Equal(new Size(100, 500), control.Size);

        // A minimum above the maximum raises the maximum.
        control.MinimumSize = new Size(150, 20);
        Assert.Equal(new Size(150, 0), control.MaximumSize);
    }

    [Fact]
    public void Points_convert_through_every_parent_and_frame()
    {
        var parent = new Framed(WS_BORDER, 0) { Bounds = new Rectangle(100, 50, 300, 200) };
        var child = new Control { Bounds = new Rectangle(10, 20, 50, 50), Parent = parent };

        Assert.Equal(new Point(100 + 1 + 10 + 5, 50 + 1 + 20 + 5), child.PointToScreen(new Point(5, 5)));
        Assert.Equal(new Point(5, 5), child.PointToClient(new Point(116, 76)));
    }

    [Fact]
    public void Margin_and_padding_refuse_negative_sides()
    {
        var control = new Control { Padding = new Padding(-1, 2, 3, 4), Margin = new Padding(-5) };

        Assert.Equal(new Padding(0, 2, 3, 4), control.Padding);
        Assert.Equal(Padding.Empty, control.Margin);
    }
}
