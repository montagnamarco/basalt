using System.ComponentModel;
using System.Data;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>
/// Review findings that need a DataTable, a BindingSource or an edited cell to prove: a DataTable
/// row deleted, a BindingSource's Filter resetting the rows, AutoGenerateColumns=false keeping the
/// user's own columns, a column added after binding being filled in, and clearing a cell bound to
/// a Nullable&lt;T&gt; property (or a nullable DataTable column) writing null/DBNull rather than
/// raising DataError. Disposal, ItemMoved/PropertyDescriptorChanged and the DataSource type check
/// live in DataGridViewBindingTests.Review.cs.
/// </summary>
public partial class DataGridViewBindingTests
{
    private sealed class Vehicle
    {
        public int? Mileage { get; set; }
    }

    private static DataTable NamedRows(params string[] names)
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        foreach (var name in names) table.Rows.Add(name);

        return table;
    }

    [Fact]
    public void Deleting_a_DataTable_row_removes_the_matching_grid_row()
    {
        var grid = new System.Windows.Forms.DataGridView { BindingContext = new BindingContext() };
        var table = NamedRows("Ann", "Bob");
        grid.DataSource = table;

        table.Rows[0].Delete();

        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Equal("Bob", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void Changing_a_BindingSource_s_Filter_resets_the_grid_s_rows()
    {
        var grid = new System.Windows.Forms.DataGridView { BindingContext = new BindingContext() };
        var table = NamedRows("Ann", "Bob");
        var source = new BindingSource { DataSource = table };
        grid.DataSource = source;

        source.Filter = "Name = 'Bob'";

        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Equal("Bob", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void AutoGenerateColumns_false_keeps_the_user_s_own_column_bound_by_DataPropertyName()
    {
        var grid = new System.Windows.Forms.DataGridView { BindingContext = new BindingContext(), AutoGenerateColumns = false };
        var userColumn = new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name" };
        grid.Columns.Add(userColumn);

        grid.DataSource = NamedRows("Ann");

        Assert.Single(grid.Columns.Cast<DataGridViewColumn>());
        Assert.Same(userColumn, grid.Columns[0]);
        Assert.Equal("Ann", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void A_column_added_after_the_grid_is_already_bound_is_filled_in_from_the_bound_items()
    {
        var (grid, _) = BoundGrid();

        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name again" });

        Assert.Equal("Ann", grid.Rows[0].Cells[3].Value);
        Assert.Equal("Bob", grid.Rows[1].Cells[3].Value);
    }

    [AvaloniaFact]
    public void Clearing_a_cell_bound_to_a_nullable_property_writes_null_without_raising_DataError()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);
        var vehicles = new BindingList<Vehicle> { new() { Mileage = 100 } };
        grid.DataSource = vehicles;
        grid.CurrentCell = grid.Rows[0].Cells[0];
        var dataErrorRaised = false;
        grid.DataError += (_, _) => dataErrorRaised = true;

        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = string.Empty;
        var committed = grid.EndEdit();

        Assert.True(committed);
        Assert.False(dataErrorRaised);
        Assert.Null(vehicles[0].Mileage);
        Assert.Null(grid.Rows[0].Cells[0].Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Clearing_a_cell_bound_to_a_nullable_DataTable_column_writes_DBNull_without_raising_DataError()
    {
        var grid = new System.Windows.Forms.DataGridView();
        var form = ShowWith(grid);
        var table = new DataTable();
        table.Columns.Add("Age", typeof(int));
        table.Columns["Age"]!.AllowDBNull = true;
        table.Rows.Add(30);
        grid.DataSource = table;
        grid.CurrentCell = grid.Rows[0].Cells[0];
        var dataErrorRaised = false;
        grid.DataError += (_, _) => dataErrorRaised = true;

        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = string.Empty;
        var committed = grid.EndEdit();

        Assert.True(committed);
        Assert.False(dataErrorRaised);
        Assert.Equal(DBNull.Value, table.Rows[0]["Age"]);
        Assert.Null(grid.Rows[0].Cells[0].Value);

        form.Close();
    }
}
