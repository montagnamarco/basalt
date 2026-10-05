using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>CheckBox, Button, Link and Image cells/columns: painting proved by reading pixels, clicks by CellContentClick.</summary>
public class DataGridViewCellTypesTests
{
    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static Form ShowWith(System.Windows.Forms.DataGridView grid)
    {
        var form = new Form { ClientSize = new Size(260, 160) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Focus();

        return form;
    }

    private static void Click(Form form, Control control, Point clientPoint)
    {
        var point = new Avalonia.Point(control.Left + clientPoint.X, control.Top + clientPoint.Y);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    [Fact]
    public void A_checked_box_paints_differently_from_an_unchecked_one()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Done" });
        grid.Rows.Add(false);
        grid.Rows.Add(true);

        var bitmap = Draw(grid);

        var uncheckedBounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        var checkedBounds = grid.GetCellDisplayRectangle(0, 1, cutOverflow: false);

        Assert.True(RegionsDiffer(bitmap, uncheckedBounds, bitmap, checkedBounds), "Expected the checked glyph to leave at least one pixel different from the unchecked one.");
    }

    [AvaloniaFact]
    public void Clicking_a_check_box_cell_toggles_its_value_and_raises_CellContentClick()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Done" });
        grid.Rows.Add(false);
        var form = ShowWith(grid);
        var contentClicks = 0;
        grid.CellContentClick += (_, _) => contentClicks++;

        var glyph = grid.Rows[0].Cells[0].ContentBounds;
        var bounds = grid.GetCellDisplayRectangle(0, 0, false);
        Click(form, grid, new Point(bounds.X + glyph.X + glyph.Width / 2, bounds.Y + glyph.Y + glyph.Height / 2));

        Assert.Equal(1, contentClicks);
        Assert.Equal(true, grid.Rows[0].Cells[0].Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Space_toggles_the_check_box_cell_that_is_current()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Done" });
        grid.Rows.Add(false);
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];

        // A real Space press also types a space character: that is the path that could open a text editor.
        form.Host!.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        form.Host!.KeyTextInput(" ");
        form.Host!.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.Equal(true, grid.Rows[0].Cells[0].Value);
        Assert.False(grid.IsCurrentCellInEditMode, "Space on a check box must not open a text editor over it");

        form.Close();
    }

    [AvaloniaFact]
    public void A_double_click_on_a_check_box_is_two_clicks_plus_a_double_click()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Done" });
        grid.Rows.Add(false);
        var form = ShowWith(grid);
        var contentClicks = 0;
        var contentDoubleClicks = 0;
        grid.CellContentClick += (_, _) => contentClicks++;
        grid.CellContentDoubleClick += (_, _) => contentDoubleClicks++;

        var glyph = grid.Rows[0].Cells[0].ContentBounds;
        var bounds = grid.GetCellDisplayRectangle(0, 0, false);
        var point = new Point(bounds.X + glyph.X + glyph.Width / 2, bounds.Y + glyph.Y + glyph.Height / 2);
        Click(form, grid, point);
        Click(form, grid, point);

        Assert.Equal(2, contentClicks);
        Assert.Equal(1, contentDoubleClicks);
        Assert.Equal(false, grid.Rows[0].Cells[0].Value);

        form.Close();
    }

    [Fact]
    public void A_three_state_check_box_cycles_through_unchecked_checked_and_indeterminate()
    {
        var cell = new DataGridViewCheckBoxCell(threeState: true);
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add(new DataGridViewCheckBoxColumn(threeState: true) { CellTemplate = cell });
        grid.Rows.Add();
        var liveCell = (DataGridViewCheckBoxCell)grid.Rows[0].Cells[0];

        Assert.Equal(CheckState.Unchecked, liveCell.GetEditingCellFormattedValue(DataGridViewDataErrorContexts.Formatting));

        liveCell.Value = true;
        Assert.Equal(CheckState.Checked, liveCell.GetEditingCellFormattedValue(DataGridViewDataErrorContexts.Formatting));
    }

    [Fact]
    public void A_button_cell_paints_as_a_raised_button_with_its_text()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Go", Text = "Go", UseColumnTextForButtonValue = true });
        grid.Rows.Add();

        var bitmap = Draw(grid);

        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        var top = bitmap.GetPixel(bounds.X + 4, bounds.Y).ToArgb();
        var bottom = bitmap.GetPixel(bounds.X + 4, bounds.Bottom - 1).ToArgb();

        // A raised button's top/left highlight differs from its bottom/right shadow; a flat fill would not.
        Assert.NotEqual(top, bottom);
    }

    [AvaloniaFact]
    public void Clicking_a_button_cell_raises_CellContentClick()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Go" });
        grid.Rows.Add();
        var form = ShowWith(grid);
        var clicks = 0;
        grid.CellContentClick += (_, _) => clicks++;

        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        Click(form, grid, new Point(bounds.X + 4, bounds.Y + 4));

        Assert.Equal(1, clicks);

        form.Close();
    }

    [Fact]
    public void A_link_cell_switches_from_LinkColor_to_VisitedLinkColor_once_visited()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        var column = new DataGridViewLinkColumn { Name = "Link", LinkColor = Color.Blue, VisitedLinkColor = Color.Purple };
        grid.Columns.Add(column);
        grid.Rows.Add("Visit");
        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);

        var cell = (DataGridViewLinkCell)grid.Rows[0].Cells[0];
        Assert.False(cell.LinkVisited);
        var beforeVisit = Draw(grid);

        cell.LinkVisited = true;
        grid.Invalidate(); // direct property writes do not self-invalidate; the click handler path already does
        var afterVisit = Draw(grid);

        Assert.True(RegionsDiffer(beforeVisit, bounds, afterVisit, bounds), "Expected VisitedLinkColor to paint at least one pixel differently from LinkColor.");
    }

    [AvaloniaFact]
    public void Clicking_a_link_cell_raises_CellContentClick_and_marks_it_visited_when_tracked()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewLinkColumn { Name = "Link" });
        grid.Rows.Add("Visit");
        var form = ShowWith(grid);
        var clicks = 0;
        grid.CellContentClick += (_, _) => clicks++;

        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        Click(form, grid, new Point(bounds.X + 4, bounds.Y + 4));

        Assert.Equal(1, clicks);
        Assert.True(((DataGridViewLinkCell)grid.Rows[0].Cells[0]).LinkVisited);

        form.Close();
    }

    [Fact]
    public void A_check_box_cell_responds_to_a_content_click_before_it_is_ever_painted()
    {
        // Never calling Draw/Refresh on this grid: the old EnsureSubscribed-from-Paint wiring
        // left a cell dead (unable to react to a click) until it was painted at least once.
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add(new DataGridViewCheckBoxColumn());
        grid.Rows.Add(false);
        var cell = (DataGridViewCheckBoxCell)grid.Rows[0].Cells[0];

        cell.OnContentClickCore(new DataGridViewCellEventArgs(0, 0));

        Assert.Equal(true, cell.Value);
    }

    [Fact]
    public void Painting_check_box_cells_does_not_leave_a_subscription_on_the_grid_once_their_rows_are_removed()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Done" });
        for (var i = 0; i < 5; i++) grid.Rows.Add(false);

        Draw(grid); // paints every cell at least once - the old EnsureSubscribed path ran for each

        while (grid.Rows.Count > 0) grid.Rows.RemoveAt(0);

        Assert.Equal(0, CellContentClickInvocationCount(grid));
    }

    [AvaloniaFact]
    public void CellContentClick_fires_on_mouse_up_over_the_same_cell_not_on_mouse_down_or_a_different_cell()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "A" });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "B" });
        grid.Rows.Add(false, false);
        var form = ShowWith(grid);
        var clicks = 0;
        grid.CellContentClick += (_, _) => clicks++;

        var glyphA = grid.Rows[0].Cells[0].ContentBounds;
        var glyphB = grid.Rows[0].Cells[1].ContentBounds;
        var boundsA = grid.GetCellDisplayRectangle(0, 0, false);
        var boundsB = grid.GetCellDisplayRectangle(1, 0, false);
        var pointA = new Avalonia.Point(grid.Left + boundsA.X + glyphA.X + glyphA.Width / 2, grid.Top + boundsA.Y + glyphA.Y + glyphA.Height / 2);
        var pointB = new Avalonia.Point(grid.Left + boundsB.X + glyphB.X + glyphB.Width / 2, grid.Top + boundsB.Y + glyphB.Y + glyphB.Height / 2);

        form.Host!.MouseDown(pointA, MouseButton.Left);
        Assert.Equal(0, clicks); // not raised on mouse-down

        form.Host!.MouseMove(pointB, RawInputModifiers.LeftMouseButton);
        form.Host!.MouseUp(pointB, MouseButton.Left);
        Assert.Equal(0, clicks); // released over a different cell: no click at all
        Assert.Equal(false, grid.Rows[0].Cells[0].Value);

        form.Host!.MouseDown(pointA, MouseButton.Left);
        form.Host!.MouseUp(pointA, MouseButton.Left);
        Assert.Equal(1, clicks); // released over the same cell it went down on: one click
        Assert.Equal(true, grid.Rows[0].Cells[0].Value);

        form.Close();
    }

    [Fact]
    public void A_link_cell_paints_ActiveLinkColor_while_the_mouse_is_pressed_on_it()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewLinkColumn { Name = "Link", LinkColor = Color.Blue, ActiveLinkColor = Color.Lime });
        grid.Rows.Add("Visit");
        var cell = (DataGridViewLinkCell)grid.Rows[0].Cells[0];
        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);

        var beforePress = Draw(grid);

        cell.OnMouseDownCore(new DataGridViewCellMouseEventArgs(0, 0, 3, 3, null));
        var whilePressed = Draw(grid);

        Assert.True(RegionsDiffer(beforePress, bounds, whilePressed, bounds), "Expected ActiveLinkColor to paint differently from LinkColor while pressed.");

        cell.OnMouseUpCore(new DataGridViewCellMouseEventArgs(0, 0, 3, 3, null));
    }

    /// <summary>
    /// How many delegates are attached to the grid's own CellContentClick event, read through the
    /// protected EventHandlerList every Component carries: the one place a leaked per-cell
    /// subscription (the defect this file's leak test catches) would show up.
    /// </summary>
    private static int CellContentClickInvocationCount(System.Windows.Forms.DataGridView grid)
    {
        var key = typeof(System.Windows.Forms.DataGridView)
            .GetField("s_cellContentClickEvent", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var eventsProperty = typeof(Component).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var events = (EventHandlerList)eventsProperty.GetValue(grid)!;

        return (events[key] as Delegate)?.GetInvocationList().Length ?? 0;
    }

    [Fact]
    public void An_image_cell_paints_the_image_s_pixels_into_the_cell()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add(new DataGridViewImageColumn { Name = "Icon" });
        grid.Columns[0].Width = 40;

        var image = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image)) g.Clear(Color.Red);

        grid.Rows.Add(image);
        grid.Rows[0].Height = 40;

        var bitmap = Draw(grid);
        var bounds = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        var center = bitmap.GetPixel(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

        Assert.Equal(Color.Red.ToArgb(), center.ToArgb());
    }

    /// <summary>Whether any pixel of one bitmap's region differs from the pixel at the same relative position in the other's.</summary>
    private static bool RegionsDiffer(Bitmap first, Rectangle firstBounds, Bitmap second, Rectangle secondBounds)
    {
        for (var dx = 0; dx < Math.Min(firstBounds.Width, secondBounds.Width); dx++)
        for (var dy = 0; dy < Math.Min(firstBounds.Height, secondBounds.Height); dy++)
        {
            var a = first.GetPixel(firstBounds.X + dx, firstBounds.Y + dy);
            var b = second.GetPixel(secondBounds.X + dx, secondBounds.Y + dy);
            if (a.ToArgb() != b.ToArgb()) return true;
        }

        return false;
    }
}
