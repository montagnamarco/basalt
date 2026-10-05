using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewCellInputCompatibilityTests
{
    [Fact]
    public void Button_space_key_draws_pressed_state_then_raises_content_click()
    {
        using var grid = new KeyboardGrid { Size = new Size(160, 90), AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewButtonColumn());
        grid.Rows.Add("go");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        var contentClicks = 0;
        grid.CellContentClick += (_, _) => contentClicks++;
        using var normal = Draw(grid);

        grid.SendKeyDown(Keys.Space);
        using var pressed = Draw(grid);
        grid.SendKeyUp(Keys.Space);

        var bounds = grid.GetCellDisplayRectangle(0, 0, false);
        Assert.True(CountChangedPixels(normal, pressed, bounds) > 0);
        Assert.Equal(1, contentClicks);
    }

    [Fact]
    public void Check_box_and_link_keyboard_actions_update_their_values()
    {
        using var grid = new KeyboardGrid { AllowUserToAddRows = false };
        grid.Columns.Add(new DataGridViewCheckBoxColumn());
        grid.Columns.Add(new DataGridViewLinkColumn());
        grid.Rows.Add(false, "open");
        var check = Assert.IsType<DataGridViewCheckBoxCell>(grid.Rows[0].Cells[0]);
        var link = Assert.IsType<DataGridViewLinkCell>(grid.Rows[0].Cells[1]);
        var linkClicks = 0;
        grid.CellContentClick += (_, e) => { if (e.ColumnIndex == 1) linkClicks++; };

        grid.CurrentCell = check;
        grid.SendKeyDown(Keys.Space);
        grid.SendKeyUp(Keys.Space);
        grid.CurrentCell = link;
        grid.SendKeyUp(Keys.Enter);

        Assert.Equal(true, check.Value);
        Assert.True(link.LinkVisited);
        Assert.Equal(1, linkClicks);
    }

    [Fact]
    public void Check_box_double_click_hook_toggles_when_dispatched_on_its_own()
    {
        var cell = new ExposedCheckBoxCell { Value = false };

        cell.DispatchContentDoubleClick();

        Assert.Equal(true, cell.Value);
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }

    private static int CountChangedPixels(Bitmap before, Bitmap after, Rectangle bounds)
    {
        var changed = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            if (before.GetPixel(x, y).ToArgb() != after.GetPixel(x, y).ToArgb()) changed++;
        }

        return changed;
    }

    private sealed class KeyboardGrid : System.Windows.Forms.DataGridView
    {
        public void SendKeyDown(Keys keys) => OnKeyDown(new KeyEventArgs(keys));

        public void SendKeyUp(Keys keys) => OnKeyUp(new KeyEventArgs(keys));
    }

    private sealed class ExposedCheckBoxCell : DataGridViewCheckBoxCell
    {
        public void DispatchContentDoubleClick() => OnContentDoubleClick(new DataGridViewCellEventArgs(0, 0));
    }
}
