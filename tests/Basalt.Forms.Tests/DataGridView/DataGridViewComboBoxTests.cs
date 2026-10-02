using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>DataGridViewComboBoxCell/Column: the classic drop-down button painted in the cell, and picking an item through the pooled DataGridViewComboBoxEditingControl.</summary>
public class DataGridViewComboBoxTests
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

    private static System.Windows.Forms.DataGridView ComboGrid(DataGridViewComboBoxDisplayStyle displayStyle)
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 80) };
        var column = new DataGridViewComboBoxColumn { Name = "Color", DisplayStyle = displayStyle };
        column.Items.AddRange("Red", "Green", "Blue");
        grid.Columns.Add(column);
        grid.Rows.Add("Red");

        return grid;
    }

    [Fact]
    public void DropDownButton_style_paints_a_button_that_Nothing_style_leaves_out()
    {
        var gridWithButton = ComboGrid(DataGridViewComboBoxDisplayStyle.DropDownButton);
        var gridWithoutButton = ComboGrid(DataGridViewComboBoxDisplayStyle.Nothing);
        var withButton = Draw(gridWithButton);
        var withoutButton = Draw(gridWithoutButton);

        var cellBounds = gridWithButton.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        var buttonBounds = new Rectangle(cellBounds.Right - 16, cellBounds.Top, 16, cellBounds.Height);

        Assert.True(
            RegionsDiffer(withButton, buttonBounds, withoutButton, buttonBounds),
            "Expected the drop-down button glyph to leave at least one pixel different from a plain text cell.");
    }

    [AvaloniaFact]
    public void GetFormattedValue_shows_the_item_s_text_for_the_cell_s_current_Value()
    {
        var grid = ComboGrid(DataGridViewComboBoxDisplayStyle.DropDownButton);

        Assert.Equal("Red", grid.Rows[0].Cells[0].FormattedValue);
    }

    [AvaloniaFact]
    public void BeginEdit_on_a_combo_cell_shows_a_DataGridViewComboBoxEditingControl_listing_its_items()
    {
        var grid = ComboGrid(DataGridViewComboBoxDisplayStyle.DropDownButton);
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];

        grid.BeginEdit(true);

        var editingControl = Assert.IsType<DataGridViewComboBoxEditingControl>(grid.EditingControl);
        Assert.Equal(3, editingControl.Items.Count);
        Assert.Equal("Red", editingControl.SelectedItem);

        form.Close();
    }

    [AvaloniaFact]
    public void Choosing_a_different_item_and_committing_changes_the_cell_s_Value()
    {
        var grid = ComboGrid(DataGridViewComboBoxDisplayStyle.DropDownButton);
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        var editingControl = (ComboBox)grid.EditingControl!;

        editingControl.SelectedItem = "Green";
        grid.EndEdit();

        Assert.False(grid.IsCurrentCellInEditMode);
        Assert.Equal("Green", grid.Rows[0].Cells[0].Value);

        form.Close();
    }

    private sealed record Fruit(int Id, string Name);

    [Fact]
    public void DataSource_with_DisplayMember_and_ValueMember_maps_the_cell_s_Value_to_the_matching_row_s_name()
    {
        var cell = new DataGridViewComboBoxCell { DisplayMember = "Name", ValueMember = "Id" };
        cell.DataSource = new List<Fruit> { new(1, "Apple"), new(2, "Pear") };

        Assert.Equal(2, cell.Items.Count);
        Assert.Equal("Pear", cell.GetItemDisplayText(cell.Items[1]!));
        Assert.Equal(2, cell.GetItemValue(cell.Items[1]!));
    }

    [AvaloniaFact]
    public void The_same_combo_editing_control_instance_is_reused_for_the_next_combo_cell_edited()
    {
        var grid = ComboGrid(DataGridViewComboBoxDisplayStyle.DropDownButton);
        grid.Rows.Add("Blue");
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        var firstControl = grid.EditingControl;
        grid.EndEdit();

        grid.CurrentCell = grid.Rows[1].Cells[0];
        grid.BeginEdit(true);

        Assert.Same(firstControl, grid.EditingControl);

        form.Close();
    }
}
