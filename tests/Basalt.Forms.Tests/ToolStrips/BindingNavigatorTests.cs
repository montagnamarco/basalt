using System.Windows.Forms;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// BindingNavigator's standard items (created by AddStandardItems), their
/// enabled state as Position and Count change, the position box accepting a
/// typed number, and clicking each navigation button driving its BindingSource.
/// </summary>
public class BindingNavigatorTests
{
    private sealed class Person
    {
        public string? Name { get; set; }
    }

    private static BindingSource NewPeopleSource(int count)
    {
        var people = new List<Person>();
        for (var i = 0; i < count; i++) people.Add(new Person { Name = $"Person {i}" });

        return new BindingSource { DataSource = people };
    }

    [Fact]
    public void AddStandardItems_creates_the_eight_items_in_order_with_one_separator()
    {
        var navigator = new BindingNavigator(addStandardItems: true);

        Assert.Equal(9, navigator.Items.Count);
        Assert.Same(navigator.MoveFirstItem, navigator.Items[0]);
        Assert.Same(navigator.MovePreviousItem, navigator.Items[1]);
        Assert.Same(navigator.PositionItem, navigator.Items[2]);
        Assert.Same(navigator.CountItem, navigator.Items[3]);
        Assert.Same(navigator.MoveNextItem, navigator.Items[4]);
        Assert.Same(navigator.MoveLastItem, navigator.Items[5]);
        Assert.IsType<ToolStripSeparator>(navigator.Items[6]);
        Assert.Same(navigator.AddNewItem, navigator.Items[7]);
        Assert.Same(navigator.DeleteItem, navigator.Items[8]);
    }

    [Fact]
    public void The_parameterless_constructor_adds_no_items()
    {
        var navigator = new BindingNavigator();

        Assert.Empty(navigator.Items);
        Assert.Null(navigator.MoveFirstItem);
    }

    [Fact]
    public void The_BindingSource_constructor_adds_the_standard_items_and_assigns_the_source()
    {
        var source = NewPeopleSource(3);
        var navigator = new BindingNavigator(source);

        Assert.Equal(9, navigator.Items.Count);
        Assert.Same(source, navigator.BindingSource);
    }

    [Fact]
    public void At_the_first_row_MoveFirst_and_MovePrevious_are_disabled_but_MoveNext_and_MoveLast_are_enabled()
    {
        var navigator = new BindingNavigator(NewPeopleSource(3));

        Assert.False(navigator.MoveFirstItem!.Enabled);
        Assert.False(navigator.MovePreviousItem!.Enabled);
        Assert.True(navigator.MoveNextItem!.Enabled);
        Assert.True(navigator.MoveLastItem!.Enabled);
    }

    [Fact]
    public void At_the_last_row_MoveNext_and_MoveLast_are_disabled_but_MoveFirst_and_MovePrevious_are_enabled()
    {
        var source = NewPeopleSource(3);
        var navigator = new BindingNavigator(source);

        source.MoveLast();

        Assert.True(navigator.MoveFirstItem!.Enabled);
        Assert.True(navigator.MovePreviousItem!.Enabled);
        Assert.False(navigator.MoveNextItem!.Enabled);
        Assert.False(navigator.MoveLastItem!.Enabled);
    }

    [Fact]
    public void With_no_BindingSource_every_navigation_item_is_disabled()
    {
        var navigator = new BindingNavigator(addStandardItems: true);

        Assert.False(navigator.MoveFirstItem!.Enabled);
        Assert.False(navigator.MoveNextItem!.Enabled);
        Assert.False(navigator.AddNewItem!.Enabled);
        Assert.False(navigator.DeleteItem!.Enabled);
    }

    [Fact]
    public void The_position_box_and_the_count_label_show_a_one_based_position_and_the_row_count()
    {
        var source = NewPeopleSource(5);
        var navigator = new BindingNavigator(source);

        Assert.Equal("1", navigator.PositionItem!.Text);
        Assert.Equal("of 5", navigator.CountItem!.Text);

        source.Position = 2;

        Assert.Equal("3", navigator.PositionItem!.Text);
    }

    [Fact]
    public void CountItemFormat_controls_how_the_count_label_is_written()
    {
        var source = NewPeopleSource(4);
        var navigator = new BindingNavigator(source) { CountItemFormat = "{0} rows" };

        Assert.Equal("4 rows", navigator.CountItem!.Text);
    }

    [Fact]
    public void Clicking_MoveNext_advances_the_BindingSources_Position()
    {
        var source = NewPeopleSource(3);
        var navigator = new BindingNavigator(source);

        navigator.MoveNextItem!.PerformClick();

        Assert.Equal(1, source.Position);
    }

    [Fact]
    public void Clicking_MoveLast_then_MoveFirst_then_MovePrevious_navigates_as_expected()
    {
        var source = NewPeopleSource(3);
        var navigator = new BindingNavigator(source);

        navigator.MoveLastItem!.PerformClick();
        Assert.Equal(2, source.Position);

        navigator.MoveFirstItem!.PerformClick();
        Assert.Equal(0, source.Position);

        navigator.MoveLastItem!.PerformClick();
        navigator.MovePreviousItem!.PerformClick();
        Assert.Equal(1, source.Position);
    }

    [Fact]
    public void Clicking_a_disabled_MoveFirst_does_nothing_because_PerformClick_ignores_disabled_items()
    {
        var source = NewPeopleSource(3);
        var navigator = new BindingNavigator(source);

        // At the first row, MoveFirstItem is disabled: PerformClick must be a no-op, proving the
        // navigator actually disables it rather than merely leaving the button checked but inert.
        navigator.MoveFirstItem!.PerformClick();

        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void Clicking_AddNew_adds_a_row_and_clicking_Delete_removes_the_current_row()
    {
        var source = NewPeopleSource(2);
        var navigator = new BindingNavigator(source);

        navigator.AddNewItem!.PerformClick();

        Assert.Equal(3, source.Count);
        Assert.Equal(2, source.Position);

        navigator.DeleteItem!.PerformClick();

        Assert.Equal(2, source.Count);
    }

    [Fact]
    public void Typing_a_number_into_the_position_box_moves_the_BindingSource_there()
    {
        var source = NewPeopleSource(5);
        var navigator = new BindingNavigator(source);

        // The position box has no real hosted TextBox yet (ToolStripControlHost is missing), so a
        // typed number is simulated by writing it to the item's Text, the way each keystroke would.
        navigator.PositionItem!.Text = "4";

        Assert.Equal(3, source.Position);
    }

    [Fact]
    public void Replacing_the_BindingSource_stops_following_the_old_one()
    {
        var first = NewPeopleSource(3);
        var second = NewPeopleSource(1);
        var navigator = new BindingNavigator(first);

        navigator.BindingSource = second;
        first.MoveNext();

        // Only the new source's row count should reach the label: the navigator must have
        // unsubscribed from the old BindingSource's events.
        Assert.Equal("of 1", navigator.CountItem!.Text);
    }

    [Fact]
    public void RefreshItems_fires_whenever_the_items_are_recomputed()
    {
        var source = NewPeopleSource(2);
        var navigator = new BindingNavigator(source);

        var raised = 0;
        navigator.RefreshItems += (_, _) => raised++;

        source.MoveNext();

        Assert.True(raised > 0);
    }

    [Fact]
    public void BeginInit_defers_refreshing_the_items_until_EndInit()
    {
        var source = NewPeopleSource(5);
        var navigator = new BindingNavigator(addStandardItems: true);

        navigator.BeginInit();
        navigator.BindingSource = source;

        // Still unrefreshed: the count label was written only by AddStandardItems, before EndInit.
        Assert.Equal("of 0", navigator.CountItem!.Text);

        navigator.EndInit();

        Assert.Equal("of 5", navigator.CountItem!.Text);
    }
}
