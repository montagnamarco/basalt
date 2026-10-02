using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>
/// DataSource/DataMember/AutoGenerateColumns against a BindingList&lt;T&gt;: column generation,
/// values shown, ItemAdded/ItemDeleted/Reset mirrored into Rows, Position/CurrentCell kept in
/// step both ways, the null-clears-everything case, and the Rows guard while bound. DataTable,
/// BindingSource and edit-commit/DataError live in DataGridViewBindingTests.DataSources.cs.
/// </summary>
public partial class DataGridViewBindingTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }

        public bool Active { get; set; }
    }

    private static (System.Windows.Forms.DataGridView Grid, BindingList<Person> People) BoundGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { BindingContext = new BindingContext() };
        var people = new BindingList<Person>
        {
            new() { Name = "Ann", Age = 30, Active = true },
            new() { Name = "Bob", Age = 40, Active = false },
        };

        grid.DataSource = people;

        return (grid, people);
    }

    [Fact]
    public void Binding_a_list_generates_one_column_per_bindable_property()
    {
        var (grid, _) = BoundGrid();

        Assert.Equal(3, grid.Columns.Count);
        Assert.Equal("Name", grid.Columns[0].DataPropertyName);
        Assert.Equal("Age", grid.Columns[1].DataPropertyName);
        Assert.Equal("Active", grid.Columns[2].DataPropertyName);
        Assert.IsType<DataGridViewCheckBoxColumn>(grid.Columns[2]);
        Assert.IsType<DataGridViewTextBoxColumn>(grid.Columns[0]);
    }

    [Fact]
    public void Without_a_feature_a_grid_bound_to_an_empty_list_has_no_rows_but_still_has_columns()
    {
        var grid = new System.Windows.Forms.DataGridView { BindingContext = new BindingContext() };

        grid.DataSource = new BindingList<Person>();

        Assert.Equal(3, grid.Columns.Count);
        Assert.Empty(grid.Rows);
    }

    [Fact]
    public void Binding_a_list_shows_each_item_s_property_values_in_its_row()
    {
        var (grid, _) = BoundGrid();

        Assert.Equal("Ann", grid.Rows[0].Cells[0].Value);
        Assert.Equal(30, grid.Rows[0].Cells[1].Value);
        Assert.Equal("Bob", grid.Rows[1].Cells[0].Value);
        Assert.Equal(40, grid.Rows[1].Cells[1].Value);
    }

    [Fact]
    public void Adding_to_the_bound_list_adds_a_row_without_rebuilding_columns()
    {
        var (grid, people) = BoundGrid();
        var columnsBefore = grid.Columns[0];

        people.Add(new Person { Name = "Cid", Age = 50 });

        Assert.Equal(3, grid.Rows.Count);
        Assert.Equal("Cid", grid.Rows[2].Cells[0].Value);
        Assert.Same(columnsBefore, grid.Columns[0]);
    }

    [Fact]
    public void Removing_from_the_bound_list_removes_the_matching_row()
    {
        var (grid, people) = BoundGrid();

        people.RemoveAt(0);

        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Equal("Bob", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void Resetting_the_bound_list_repopulates_every_row()
    {
        var (grid, people) = BoundGrid();

        people[0].Name = "Changed";
        people.ResetBindings();

        Assert.Equal("Changed", grid.Rows[0].Cells[0].Value);
        Assert.Equal(2, grid.Rows.Count);
    }

    [Fact]
    public void Setting_DataSource_to_null_clears_the_bound_rows_and_the_generated_columns()
    {
        var (grid, _) = BoundGrid();

        grid.DataSource = null;

        Assert.Empty(grid.Rows);
        Assert.Empty(grid.Columns);
    }

    [Fact]
    public void Rows_cannot_be_added_programmatically_while_the_grid_is_data_bound()
    {
        var (grid, _) = BoundGrid();

        Assert.Throws<InvalidOperationException>(() => grid.Rows.Add());
    }

    [Fact]
    public void Rows_cannot_be_removed_programmatically_while_the_grid_is_data_bound()
    {
        var (grid, _) = BoundGrid();

        Assert.Throws<InvalidOperationException>(() => grid.Rows.RemoveAt(0));
    }

    [Fact]
    public void Moving_CurrentCell_to_another_row_moves_the_CurrencyManager_s_Position()
    {
        var (grid, people) = BoundGrid();
        var manager = (CurrencyManager)grid.BindingContext![people];

        grid.CurrentCell = grid.Rows[1].Cells[0];

        Assert.Equal(1, manager.Position);
    }

    [Fact]
    public void Moving_the_CurrencyManager_s_Position_moves_CurrentCell_to_the_matching_row()
    {
        var (grid, people) = BoundGrid();
        var manager = (CurrencyManager)grid.BindingContext![people];
        grid.CurrentCell = grid.Rows[0].Cells[0];

        manager.Position = 1;

        Assert.Equal(1, grid.CurrentCellAddress.Y);
    }
}
