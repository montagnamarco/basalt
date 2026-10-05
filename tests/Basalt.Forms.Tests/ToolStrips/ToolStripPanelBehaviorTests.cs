using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// The real behaviour added for ToolStripPanel's forwarding members:
/// disposing a panel disposes its rows, painting goes through the renderer
/// (Handled skips the default fill), AutoScroll is refused, and the
/// standalone ToolStripPanelRowCollection type.
/// </summary>
public class ToolStripPanelBehaviorTests
{
    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(Math.Max(control.Width, 1), Math.Max(control.Height, 1), PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void Disposing_the_panel_disposes_its_rows()
    {
        var panel = new ToolStripPanel();
        var strip = new ToolStrip { AutoSize = false, Size = new Size(50, 20) };
        panel.Join(strip);
        var row = panel.Rows[0];
        var rowDisposed = false;
        row.Disposed += (_, _) => rowDisposed = true;

        panel.Dispose();

        Assert.True(rowDisposed);
    }

    [Fact]
    public void AutoScroll_cannot_be_turned_on()
    {
        var panel = new ToolStripPanel();

        Assert.Throws<NotSupportedException>(() => panel.AutoScroll = true);
        Assert.False(panel.AutoScroll);
    }

    [Fact]
    public void OnPaintBackground_asks_the_renderer_and_Handled_skips_the_default_fill()
    {
        // ToolStripProfessionalRenderer fills the panel band with a real
        // gradient, so its pixels prove whether the default fill ran.
        var gradientBegin = new ToolStripProfessionalRenderer().ColorTable.ToolStripPanelGradientBegin;

        var painted = new ToolStripPanel { AutoSize = false, Size = new Size(100, 30), Renderer = new ToolStripProfessionalRenderer() };
        var askedWhenPainted = 0;
        painted.Renderer.RenderToolStripPanelBackground += (_, _) => askedWhenPainted++;
        using var paintedBitmap = Draw(painted);
        var paintedPixel = paintedBitmap.GetPixel(1, 1);

        var suppressed = new ToolStripPanel { AutoSize = false, Size = new Size(100, 30), Renderer = new ToolStripProfessionalRenderer() };
        var askedWhenSuppressed = 0;
        suppressed.Renderer.RenderToolStripPanelBackground += (_, e) =>
        {
            askedWhenSuppressed++;
            e.Handled = true;
        };
        var suppressedPixel = Draw(suppressed).GetPixel(1, 1);

        // The panel's BackColor (Control) equals the gradient's begin colour, so
        // the top row cannot tell the gradient from the base fill. The bottom
        // row is where the gradient reaches ControlLight, which the BackColor
        // fill (painted afterwards when Handled is not set) would erase.
        var bottomPixel = paintedBitmap.GetPixel(1, painted.Height - 1);
        var gradientEnd = new ToolStripProfessionalRenderer().ColorTable.ToolStripPanelGradientEnd;
        Assert.NotEqual(painted.BackColor.ToArgb(), gradientEnd.ToArgb());
        Assert.NotEqual(painted.BackColor.ToArgb(), bottomPixel.ToArgb());
        Assert.InRange(bottomPixel.R, gradientEnd.R - 4, gradientEnd.R + 4);
        Assert.InRange(bottomPixel.G, gradientEnd.G - 4, gradientEnd.G + 4);
        Assert.InRange(bottomPixel.B, gradientEnd.B - 4, gradientEnd.B + 4);

        Assert.Equal(1, askedWhenPainted);
        Assert.Equal(1, askedWhenSuppressed);
        // Without Handled, the renderer's gradient fill reaches the bitmap...
        Assert.Equal(gradientBegin.ToArgb(), paintedPixel.ToArgb());
        // ...but a subscriber setting Handled suppresses both the renderer's
        // own default fill and the panel's base BackColor fill, leaving the
        // pixel exactly as the untouched 32bpp bitmap started: transparent.
        Assert.NotEqual(gradientBegin.ToArgb(), suppressedPixel.ToArgb());
        Assert.Equal(0, suppressedPixel.A);
    }

    [Fact]
    public void Content_panel_asks_the_renderer_and_Handled_skips_the_base_fill()
    {
        // The professional renderer fills the content panel with
        // ControlLightLight while its BackColor is Control: the pixel tells
        // which of the two fills reached the bitmap.
        var rendererFill = new ToolStripProfessionalRenderer().ColorTable.ToolStripContentPanelGradientEnd;

        using var painted = new ToolStripContentPanel { Size = new Size(60, 30), Renderer = new ToolStripProfessionalRenderer() };
        var askedWhenPainted = 0;
        painted.Renderer.RenderToolStripContentPanelBackground += (_, _) => askedWhenPainted++;
        using var paintedBitmap = Draw(painted);

        using var suppressed = new ToolStripContentPanel { Size = new Size(60, 30), Renderer = new ToolStripProfessionalRenderer() };
        suppressed.Renderer.RenderToolStripContentPanelBackground += (_, e) => e.Handled = true;
        using var suppressedBitmap = Draw(suppressed);

        Assert.Equal(1, askedWhenPainted);
        Assert.NotEqual(painted.BackColor.ToArgb(), rendererFill.ToArgb());
        Assert.Equal(rendererFill.ToArgb(), paintedBitmap.GetPixel(5, 5).ToArgb());
        // Handled set before the renderer paints: neither fill runs.
        Assert.Equal(0, suppressedBitmap.GetPixel(5, 5).A);
    }

    [Fact]
    public void Row_collection_is_the_panels_arranged_row_storage()
    {
        using var panel = new ToolStripPanel { AutoSize = false, Size = new Size(150, 80) };
        var rows = new ToolStripPanel.ToolStripPanelRowCollection(panel);
        var first = new ToolStripPanelRow(panel);
        var second = new ToolStripPanelRow(panel);

        Assert.Equal(0, rows.Add(first));
        rows.Insert(0, second);
        Assert.Equal(new[] { second, first }, panel.Rows);
        Assert.Equal(2, ((System.Collections.IList)rows).Count);
        Assert.Same(second, ((System.Collections.IList)rows)[0]);

        rows.Remove(second);
        Assert.Equal(new[] { first }, panel.Rows);
        rows.Clear();
        Assert.Empty(panel.Rows);
    }

    [Fact]
    public void Removing_a_row_detaches_its_strips_from_the_panel()
    {
        using var panel = new ToolStripPanel { AutoSize = false, Size = new Size(150, 80) };
        var strip = new ToolStrip { AutoSize = false, Size = new Size(50, 20) };
        panel.Join(strip);
        var rows = new ToolStripPanel.ToolStripPanelRowCollection(panel);
        var row = panel.Rows[0];

        rows.Remove(row);

        Assert.Empty(panel.Rows);
        Assert.Null(strip.Parent);
        Assert.Empty(row.Controls);
    }

    [Fact]
    public void Inherited_IList_mutations_share_the_panel_rows_and_preserve_layout()
    {
        using var panel = new ToolStripPanel { AutoSize = false, Size = new Size(150, 80) };
        var first = new ToolStripPanelRow(panel);
        var second = new ToolStripPanelRow(panel);
        var rows = new ToolStripPanel.ToolStripPanelRowCollection(panel, [first]);
        var list = (System.Collections.IList)rows;

        Assert.Equal(1, list.Add(second));
        Assert.Equal(new[] { first, second }, panel.Rows);

        var copy = new ToolStripPanelRow[2];
        rows.CopyTo(copy, 0);
        Assert.Equal(panel.Rows, copy);

        list.RemoveAt(0);
        Assert.Equal(new[] { second }, panel.Rows);
        list.Clear();
        Assert.Empty(panel.Rows);
    }

    [Fact]
    public void Rejoining_a_strip_survives_row_mutation_in_a_removal_callback()
    {
        using var panel = new ToolStripPanel { AutoSize = false, Size = new Size(150, 80) };
        var rows = new ToolStripPanel.ToolStripPanelRowCollection(panel);
        var row = new MutatingRow(panel);
        rows.Add(row);
        var strip = new ToolStrip { AutoSize = false, Size = new Size(50, 20) };
        panel.Controls.Add(strip);
        row.RemovingControl = () => rows.Add(new ToolStripPanelRow(panel));

        panel.Join(strip, 0);

        Assert.Single(panel.Rows);
        Assert.Contains(strip, panel.Rows[0].Controls);
    }

    [Fact]
    public void Removing_empty_rows_survives_layout_callback_clearing_other_rows()
    {
        using var panel = new ToolStripPanel { AutoSize = false, Size = new Size(150, 80) };
        var rows = new ToolStripPanel.ToolStripPanelRowCollection(panel);
        rows.Add(new ToolStripPanelRow(panel));
        var last = new MutatingRow(panel);
        rows.Add(last);
        var strip = new ToolStrip { AutoSize = false, Size = new Size(50, 20) };
        panel.Controls.Add(strip);

        var armed = false;
        var cleared = false;
        last.RemovingControl = () => armed = true;
        panel.Layout += (_, _) =>
        {
            if (!armed || cleared) return;

            cleared = true;
            rows.Clear();
        };

        panel.Controls.Remove(strip);

        Assert.True(cleared);
        Assert.Empty(panel.Rows);
    }

    private sealed class MutatingRow(ToolStripPanel panel) : ToolStripPanelRow(panel)
    {
        public Action? RemovingControl { get; set; }

        protected override void OnControlRemoved(Control control, int index) => RemovingControl?.Invoke();
    }
}
