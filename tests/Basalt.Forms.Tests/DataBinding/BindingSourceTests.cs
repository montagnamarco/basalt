using System.ComponentModel;
using System.Data;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataBinding;

/// <summary>
/// BindingSource's own navigation (Position/Current/Count, the Move* methods), its
/// relayed events, its own CurrencyManager (so a bound control follows its Position),
/// mutation (Add/Insert/Remove/AddNew), and Filter/Sort delegated to an underlying
/// DataView.
/// </summary>
public class BindingSourceTests
{
    private sealed class Person
    {
        public string? Name { get; set; }

        public int Age { get; set; }
    }

    private static DataTable NewPeopleTable()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Age", typeof(int));
        table.Rows.Add("Ada", 36);
        table.Rows.Add("Grace", 85);
        table.Rows.Add("Katherine", 101);

        return table;
    }

    [Fact]
    public void A_plain_list_is_navigated_by_Position_and_Current()
    {
        var people = new List<Person> { new() { Name = "Ada" }, new() { Name = "Grace" } };
        var source = new BindingSource { DataSource = people };

        Assert.Equal(2, source.Count);
        Assert.Equal(0, source.Position);
        Assert.Same(people[0], source.Current);

        source.Position = 1;

        Assert.Same(people[1], source.Current);
    }

    [Fact]
    public void MoveNext_and_MovePrevious_step_one_row_at_a_time()
    {
        var people = new List<Person> { new(), new(), new() };
        var source = new BindingSource { DataSource = people };

        source.MoveLast();
        Assert.Equal(2, source.Position);

        source.MovePrevious();
        Assert.Equal(1, source.Position);

        source.MoveNext();
        Assert.Equal(2, source.Position);

        source.MoveFirst();
        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void PositionChanged_and_CurrentChanged_fire_when_Position_moves()
    {
        var people = new List<Person> { new(), new() };
        var source = new BindingSource { DataSource = people };

        var positionChanged = 0;
        var currentChanged = 0;
        source.PositionChanged += (_, _) => positionChanged++;
        source.CurrentChanged += (_, _) => currentChanged++;

        source.Position = 1;

        Assert.Equal(1, positionChanged);
        Assert.Equal(1, currentChanged);
    }

    [Fact]
    public void A_control_bound_through_the_BindingSource_follows_its_Position()
    {
        var people = new List<Person> { new() { Name = "Ada" }, new() { Name = "Grace" } };
        var source = new BindingSource { DataSource = people };
        var textBox = new Control { BindingContext = new BindingContext() };

        textBox.DataBindings.Add("Text", source, "Name");

        Assert.Equal("Ada", textBox.Text);

        source.Position = 1;

        Assert.Equal("Grace", textBox.Text);
    }

    [Fact]
    public void Two_controls_bound_through_the_same_BindingSource_share_its_Position()
    {
        var people = new List<Person> { new() { Name = "Ada" }, new() { Name = "Grace" } };
        var source = new BindingSource { DataSource = people };
        var first = new Control { BindingContext = new BindingContext() };
        var second = new Control { BindingContext = new BindingContext() };

        first.DataBindings.Add("Text", source, "Name");
        second.DataBindings.Add("Text", source, "Name");

        source.MoveNext();

        Assert.Equal("Grace", first.Text);
        Assert.Equal("Grace", second.Text);
    }

    [Fact]
    public void Adding_to_a_plain_list_raises_ListChanged_and_keeps_Count_current()
    {
        var people = new List<Person> { new() { Name = "Ada" } };
        var source = new BindingSource { DataSource = people };

        ListChangedEventArgs? seen = null;
        source.ListChanged += (_, e) => seen = e;

        source.Add(new Person { Name = "Grace" });

        Assert.Equal(2, source.Count);
        Assert.Equal(ListChangedType.ItemAdded, seen!.ListChangedType);
        Assert.Equal(1, seen.NewIndex);
    }

    [Fact]
    public void RemoveAt_removes_from_the_underlying_list_and_raises_ListChanged()
    {
        var people = new List<Person> { new() { Name = "Ada" }, new() { Name = "Grace" } };
        var source = new BindingSource { DataSource = people };

        var raised = false;
        source.ListChanged += (_, e) =>
        {
            if (e.ListChangedType == ListChangedType.ItemDeleted) raised = true;
        };

        source.RemoveAt(0);

        Assert.Single(people);
        Assert.Equal("Grace", people[0].Name);
        Assert.True(raised);
    }

    [Fact]
    public void RemoveCurrent_removes_the_row_at_Position()
    {
        var people = new List<Person> { new() { Name = "Ada" }, new() { Name = "Grace" } };
        var source = new BindingSource { DataSource = people };

        source.Position = 1;
        source.RemoveCurrent();

        Assert.Single(source);
        Assert.Equal("Ada", people[0].Name);
    }

    [Fact]
    public void AddNew_creates_an_item_of_the_lists_element_type_and_makes_it_current()
    {
        var people = new List<Person> { new() { Name = "Ada" } };
        var source = new BindingSource { DataSource = people };

        var created = source.AddNew();

        Assert.IsType<Person>(created);
        Assert.Equal(2, source.Count);
        Assert.Equal(1, source.Position);
        Assert.Same(created, source.Current);
    }

    [Fact]
    public void AddingNew_lets_a_handler_supply_the_new_item()
    {
        var people = new List<Person>();
        var source = new BindingSource { DataSource = people };
        var suppliedPerson = new Person { Name = "Hedy" };

        source.AddingNew += (_, e) => e.NewObject = suppliedPerson;

        var created = source.AddNew();

        Assert.Same(suppliedPerson, created);
        Assert.Same(suppliedPerson, people[0]);
    }

    [Fact]
    public void AllowNew_is_false_once_turned_off_even_though_the_list_would_allow_it()
    {
        var people = new List<Person> { new() };
        var source = new BindingSource { DataSource = people, AllowNew = false };

        Assert.Throws<InvalidOperationException>(() => source.AddNew());
    }

    [Fact]
    public void A_data_table_binds_through_its_default_view_and_navigates_rows()
    {
        var table = NewPeopleTable();
        var source = new BindingSource { DataSource = table };

        Assert.Equal(3, source.Count);
        Assert.Equal("Ada", ((DataRowView)source.Current!)["Name"]);

        source.MoveNext();

        Assert.Equal("Grace", ((DataRowView)source.Current!)["Name"]);
    }

    [Fact]
    public void Filter_on_a_DataView_hides_rows_that_do_not_match()
    {
        var table = NewPeopleTable();
        var source = new BindingSource { DataSource = table };

        source.Filter = "Age > 50";

        Assert.Equal(2, source.Count);
        Assert.True(source.SupportsFiltering);
    }

    [Fact]
    public void RemoveFilter_restores_every_row()
    {
        var table = NewPeopleTable();
        var source = new BindingSource { DataSource = table, Filter = "Age > 50" };

        source.RemoveFilter();

        Assert.Equal(3, source.Count);
        Assert.Null(source.Filter);
    }

    [Fact]
    public void Sort_on_a_DataView_orders_rows_by_the_named_column()
    {
        var table = NewPeopleTable();
        var source = new BindingSource { DataSource = table };

        source.Sort = "Age DESC";

        Assert.Equal("Katherine", ((DataRowView)source[0]!)["Name"]);
        Assert.Equal("Ada", ((DataRowView)source[2]!)["Name"]);
        Assert.True(source.SupportsSorting);
    }

    [Fact]
    public void Filter_throws_when_the_underlying_list_does_not_support_it()
    {
        var people = new List<Person> { new() };
        var source = new BindingSource { DataSource = people };

        Assert.Throws<NotSupportedException>(() => source.Filter = "Age > 50");
    }

    [Fact]
    public void SupportsFiltering_is_false_for_a_plain_list()
    {
        var people = new List<Person> { new() };
        var source = new BindingSource { DataSource = people };

        Assert.False(source.SupportsFiltering);
        Assert.False(source.SupportsSorting);
    }

    [Fact]
    public void A_chained_BindingSource_follows_its_parents_current_row()
    {
        var customers = new List<Customer>
        {
            new() { Name = "Contoso", Orders = [new Order { Total = 10 }, new Order { Total = 20 }] },
            new() { Name = "Fabrikam", Orders = [new Order { Total = 99 }] },
        };

        var customerSource = new BindingSource { DataSource = customers };
        var orderSource = new BindingSource { DataSource = customerSource, DataMember = "Orders" };

        Assert.Equal(2, orderSource.Count);

        customerSource.MoveNext();

        Assert.Single(orderSource);
        Assert.Equal(99, ((Order)orderSource.Current!).Total);
    }

    private sealed class Customer
    {
        public string? Name { get; set; }

        public List<Order> Orders { get; set; } = [];
    }

    private sealed class Order
    {
        public int Total { get; set; }
    }

    [Fact]
    public void SuspendBinding_stops_pushing_to_bound_controls_until_resumed()
    {
        var people = new List<Person> { new() { Name = "Ada" }, new() { Name = "Grace" } };
        var source = new BindingSource { DataSource = people };
        var textBox = new Control { BindingContext = new BindingContext() };
        textBox.DataBindings.Add("Text", source, "Name");

        source.SuspendBinding();
        source.Position = 1;

        Assert.Equal("Ada", textBox.Text);

        source.ResumeBinding();

        Assert.Equal("Grace", textBox.Text);
    }

    [Fact]
    public void ResetCurrentItem_pushes_the_rows_values_again_after_an_external_edit()
    {
        var people = new List<Person> { new() { Name = "Ada" } };
        var source = new BindingSource { DataSource = people };
        var textBox = new Control { BindingContext = new BindingContext() };
        textBox.DataBindings.Add("Text", source, "Name");

        people[0].Name = "Changed outside the binding";
        source.ResetCurrentItem();

        Assert.Equal("Changed outside the binding", textBox.Text);
    }

    [Fact]
    public void BeginInit_defers_resolving_the_list_until_EndInit()
    {
        var people = new List<Person> { new() { Name = "Ada" } };
        var otherPeople = new List<Person> { new() { Name = "Grace" }, new() { Name = "Katherine" } };
        var source = new BindingSource { DataSource = people };

        ((ISupportInitialize)source).BeginInit();
        source.DataSource = otherPeople;

        // Still the old list: ResetList has not run yet.
        Assert.Single(source);

        ((ISupportInitialize)source).EndInit();

        Assert.Equal(2, source.Count);
    }

    [Fact]
    public void A_bound_control_follows_a_new_DataSource_given_to_the_same_BindingSource()
    {
        var source = new BindingSource { DataSource = new List<Person> { new() { Name = "Ada" } } };
        var textBox = new Control { BindingContext = new BindingContext() };
        textBox.DataBindings.Add("Text", source, "Name");
        var resets = 0;
        source.ListChanged += (_, e) => { if (e.ListChangedType == ListChangedType.Reset) resets++; };

        source.DataSource = new List<Person> { new() { Name = "Grace" }, new() { Name = "Katherine" } };

        Assert.Equal("Grace", textBox.Text);
        Assert.Equal(1, resets);

        source.Position = 1;
        Assert.Equal("Katherine", textBox.Text);
    }

    private sealed class NeedsAName
    {
        public NeedsAName(string name) => Name = name;

        public string Name { get; }
    }

    [Fact]
    public void AllowNew_is_false_for_an_item_type_without_a_default_constructor_unless_AddingNew_supplies_one()
    {
        var source = new BindingSource { DataSource = new List<NeedsAName> { new("Ada") } };

        Assert.False(source.AllowNew);

        source.AddingNew += (_, e) => e.NewObject = new NeedsAName("New");
        Assert.True(source.AllowNew);
    }

    [Fact]
    public void Cancelling_a_new_row_on_a_DataView_leaves_the_table_as_it_was()
    {
        var table = NewPeopleTable();
        var source = new BindingSource { DataSource = table };

        source.AddNew();
        Assert.Equal(4, source.Count);

        ((ICancelAddNew)source).CancelNew(source.Count - 1);

        Assert.Equal(3, source.Count);
        Assert.Equal(3, table.Rows.Count);
    }

    [Fact]
    public void A_DataSource_set_after_Dispose_still_reports_its_changes()
    {
        var source = new BindingSource { DataSource = new List<Person> { new() { Name = "Ada" } } };
        source.Dispose();
        var resets = 0;
        source.ListChanged += (_, e) => { if (e.ListChangedType == ListChangedType.Reset) resets++; };

        source.DataSource = new List<Person> { new() { Name = "Grace" } };

        Assert.Equal(1, resets);
        Assert.Equal(1, source.Count);
    }
}
