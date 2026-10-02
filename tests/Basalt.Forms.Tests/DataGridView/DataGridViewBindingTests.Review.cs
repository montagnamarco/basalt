using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>
/// Review findings on top of the first binding round: disposal unwires the CurrencyManager,
/// ItemMoved/PropertyDescriptorChanged are handled (not silently ignored), and DataSource rejects
/// a value that is neither an IList nor an IListSource. DataTable/BindingSource/AutoGenerateColumns
/// coverage and the Nullable-clears-to-null behaviour live in DataGridViewBindingTests.ReviewEdits.cs.
/// </summary>
public partial class DataGridViewBindingTests
{
    /// <summary>
    /// A BindingList&lt;Person&gt; that can raise ListChangedType.ItemMoved and
    /// PropertyDescriptorChanged directly: BindingList&lt;T&gt;'s own public members never raise
    /// either, so a test double is the only way to drive the grid's handling of them.
    /// </summary>
    private sealed class RaisableList : BindingList<Person>
    {
        public void RaiseItemMoved(int oldIndex, int newIndex)
        {
            var item = Items[oldIndex];
            Items.RemoveAt(oldIndex);
            Items.Insert(newIndex, item);
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemMoved, newIndex, oldIndex));
        }

        public void RaisePropertyDescriptorChanged() => OnListChanged(new ListChangedEventArgs(ListChangedType.PropertyDescriptorChanged, -1));
    }

    [Fact]
    public void Disposing_a_bound_grid_unwires_it_from_the_CurrencyManager()
    {
        var (grid, people) = BoundGrid();
        var manager = (CurrencyManager)grid.BindingContext![people];
        var listChangedField = typeof(CurrencyManager).GetField("ListChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var subscribersBefore = ((Delegate?)listChangedField.GetValue(manager))?.GetInvocationList().Length ?? 0;

        grid.Dispose();

        var subscribersAfter = ((Delegate?)listChangedField.GetValue(manager))?.GetInvocationList().Length ?? 0;

        Assert.True(subscribersBefore > 0);
        Assert.Equal(subscribersBefore - 1, subscribersAfter);

        // A list change that would otherwise reach the disposed grid must not throw.
        people.Add(new Person { Name = "Zoe" });
    }

    [Fact]
    public void ItemMoved_moves_the_same_row_and_keeps_CurrentCell_on_the_same_item()
    {
        var grid = new System.Windows.Forms.DataGridView { BindingContext = new BindingContext() };
        var people = new RaisableList();
        people.Add(new Person { Name = "Ann" });
        people.Add(new Person { Name = "Bob" });
        people.Add(new Person { Name = "Cid" });
        grid.DataSource = people;
        var row = grid.Rows[0];
        grid.CurrentCell = row.Cells[0];

        people.RaiseItemMoved(0, 2);

        Assert.Same(row, grid.Rows[2]);
        Assert.Equal("Ann", grid.Rows[2].Cells[0].Value);
        Assert.Same(row.Cells[0], grid.CurrentCell);
    }

    [Fact]
    public void PropertyDescriptorChanged_rebuilds_the_generated_columns_and_raises_DataBindingComplete()
    {
        var grid = new System.Windows.Forms.DataGridView { BindingContext = new BindingContext() };
        var people = new RaisableList();
        people.Add(new Person { Name = "Ann" });
        grid.DataSource = people;
        var columnBefore = grid.Columns[0];
        DataGridViewBindingCompleteEventArgs? seen = null;
        grid.DataBindingComplete += (_, e) => seen = e;

        people.RaisePropertyDescriptorChanged();

        Assert.NotSame(columnBefore, grid.Columns[0]);
        Assert.Equal("Ann", grid.Rows[0].Cells[0].Value);
        Assert.Equal(ListChangedType.PropertyDescriptorChanged, seen?.ListChangedType);
    }

    [Fact]
    public void Setting_DataSource_to_a_value_that_is_not_a_list_or_an_IListSource_throws()
    {
        var grid = new System.Windows.Forms.DataGridView();

        Assert.Throws<ArgumentException>(() => grid.DataSource = new object());
    }
}
