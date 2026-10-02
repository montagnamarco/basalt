using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>
/// DataTable and BindingSource as DataSource, and the parts of binding that need a real editing
/// control: committing an edited cell back through its PropertyDescriptor, and DataError on a
/// conversion that fails. BindingList&lt;T&gt; and the plumbing around it live in DataGridViewBindingTests.cs.
/// </summary>
public partial class DataGridViewBindingTests
{
    private static Form ShowWith(System.Windows.Forms.DataGridView grid)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        grid.BindingContext = new BindingContext();
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Focus();

        return form;
    }

    private static DataTable PeopleTable()
    {
        var table = new DataTable("People");
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Age", typeof(int));
        table.Rows.Add("Ann", 30);
        table.Rows.Add("Bob", 40);

        return table;
    }

    [AvaloniaFact]
    public void A_DataTable_data_source_generates_one_column_per_data_column_and_shows_the_rows()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);

        grid.DataSource = PeopleTable();

        Assert.Equal(2, grid.Columns.Count);
        Assert.Equal("Name", grid.Columns[0].DataPropertyName);
        Assert.Equal("Age", grid.Columns[1].DataPropertyName);
        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal("Ann", grid.Rows[0].Cells[0].Value);
        Assert.Equal(30, grid.Rows[0].Cells[1].Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Adding_a_row_to_the_DataTable_adds_a_row_to_the_grid()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);
        var table = PeopleTable();
        grid.DataSource = table;

        table.Rows.Add("Cid", 50);

        Assert.Equal(3, grid.Rows.Count);
        Assert.Equal("Cid", grid.Rows[2].Cells[0].Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Committing_an_edited_cell_writes_the_converted_value_back_through_the_property_descriptor()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);
        var table = PeopleTable();
        grid.DataSource = table;
        grid.CurrentCell = grid.Rows[0].Cells[1];

        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "99";
        var committed = grid.EndEdit();

        Assert.True(committed);
        Assert.Equal(99, table.Rows[0]["Age"]);
        Assert.IsType<int>(grid.Rows[0].Cells[1].Value);
        Assert.Equal(99, grid.Rows[0].Cells[1].Value);

        form.Close();
    }

    [AvaloniaFact]
    public void A_conversion_that_fails_on_commit_raises_DataError_with_the_exception()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);
        var table = PeopleTable();
        grid.DataSource = table;
        grid.CurrentCell = grid.Rows[0].Cells[1];

        DataGridViewDataErrorEventArgs? seen = null;
        grid.DataError += (_, e) => seen = e;

        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "not a number";
        grid.EndEdit();

        Assert.NotNull(seen);
        Assert.NotNull(seen!.Exception);
        Assert.Equal(30, table.Rows[0]["Age"]);

        form.Close();
    }

    [AvaloniaFact]
    public void Cancelling_DataError_keeps_the_cell_in_edit_mode()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);
        var table = PeopleTable();
        grid.DataSource = table;
        grid.CurrentCell = grid.Rows[0].Cells[1];
        grid.DataError += (_, e) => e.Cancel = true;

        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "not a number";
        var committed = grid.EndEdit();

        Assert.False(committed);
        Assert.True(grid.IsCurrentCellInEditMode);

        form.Close();
    }

    [AvaloniaFact]
    public void A_BindingSource_data_source_mirrors_its_list_and_keeps_Position_and_CurrentCell_in_step()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);
        var people = new BindingList<Person> { new() { Name = "Ann", Age = 30 }, new() { Name = "Bob", Age = 40 } };
        var source = new BindingSource { DataSource = people };
        grid.DataSource = source;

        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal("Ann", grid.Rows[0].Cells[0].Value);

        source.Position = 1;
        Assert.Equal(1, grid.CurrentCellAddress.Y);

        grid.CurrentCell = grid.Rows[0].Cells[0];
        Assert.Equal(0, source.Position);

        form.Close();
    }
}
