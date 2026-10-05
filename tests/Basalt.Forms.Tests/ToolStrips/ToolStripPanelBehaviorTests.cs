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

    // ToolStripPanelRowCollection is not implemented yet: see the task's report (ArrangedElementCollection's
    // inherited Count/GetEnumerator cannot be redeclared without failing Nothing_is_public_that_Microsoft_does_not_have,
    // and its Items storage is List<IArrangedElement>, which ToolStripPanelRow does not implement).
}
