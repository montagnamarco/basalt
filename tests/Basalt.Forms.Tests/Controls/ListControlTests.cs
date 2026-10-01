using System.Collections;
using System.Globalization;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>
/// ListControl: DataSource/DisplayMember/ValueMember binding, SelectedValue,
/// GetItemText formatting and the two-way sync with the CurrencyManager that
/// ListBox and ComboBox will both build on.
/// </summary>
public class ListControlTests
{
    private sealed record Person(string Name, int Id, DateTime Joined);

    /// <summary>A minimal concrete ListControl: owns a plain int selection, and records SetItemsCore/RefreshItem calls.</summary>
    private sealed class TestListControl : ListControl
    {
        private int _selectedIndex = -1;

        public List<IList> SetItemsCoreCalls { get; } = new();

        public List<int> RefreshItemCalls { get; } = new();

        public new CurrencyManager? DataManager => base.DataManager;

        public override int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value == _selectedIndex) return;

                _selectedIndex = value;
                OnSelectedIndexChanged(EventArgs.Empty);
            }
        }

        protected override void RefreshItem(int index) => RefreshItemCalls.Add(index);

        protected override void SetItemsCore(IList items) => SetItemsCoreCalls.Add(items);
    }

    private static TestListControl NewBoundControl(IList dataSource, out Control container)
    {
        container = new Control { BindingContext = new BindingContext() };
        var control = new TestListControl { Parent = container };

        control.DataSource = dataSource;

        return control;
    }

    [Fact]
    public void Setting_DataSource_connects_the_CurrencyManager_and_populates_the_items()
    {
        var people = new List<Person> { new("Ada", 1, new DateTime(2024, 1, 2)), new("Grace", 2, new DateTime(2024, 3, 4)) };
        var control = NewBoundControl(people, out _);
        control.DisplayMember = "Name";
        control.ValueMember = "Id";

        Assert.NotNull(control.DataManager);
        Assert.Single(control.SetItemsCoreCalls);
        Assert.Same(people, control.SetItemsCoreCalls[0]);
        Assert.Equal(0, control.SelectedIndex);
        Assert.Equal("Ada", control.GetItemText(people[0]));
        Assert.Equal(1, control.SelectedValue);
    }

    [Fact]
    public void A_DataSource_that_is_neither_IList_nor_IListSource_throws()
    {
        var control = new TestListControl();

        Assert.Throws<ArgumentException>(() => control.DataSource = "a plain string is not a list");
    }

    [Fact]
    public void An_unknown_ValueMember_throws_once_a_DataSource_is_connected()
    {
        var people = new List<Person> { new("Ada", 1, new DateTime(2024, 1, 2)) };
        var control = NewBoundControl(people, out _);

        Assert.Throws<ArgumentException>(() => control.ValueMember = "NoSuchProperty");
    }

    [Fact]
    public void Setting_SelectedValue_looks_up_the_matching_item_and_moves_the_selection()
    {
        var people = new List<Person> { new("Ada", 1, new DateTime(2024, 1, 2)), new("Grace", 2, new DateTime(2024, 3, 4)) };
        var control = NewBoundControl(people, out _);
        control.DisplayMember = "Name";
        control.ValueMember = "Id";
        var changes = new List<object?>();
        control.SelectedValueChanged += (_, _) => changes.Add(control.SelectedValue);

        control.SelectedValue = 2;

        Assert.Equal(1, control.SelectedIndex);
        Assert.Equal(2, control.SelectedValue);
        Assert.Equal([2], changes);
    }

    [Fact]
    public void Changing_DataSource_resets_DisplayMember_and_ValueMember()
    {
        var firstSource = new List<Person> { new("Ada", 1, new DateTime(2024, 1, 2)) };
        var control = NewBoundControl(firstSource, out _);
        control.DisplayMember = "Name";
        control.ValueMember = "Id";
        var displayMemberChanges = 0;
        var valueMemberChanges = 0;
        control.DisplayMemberChanged += (_, _) => displayMemberChanges++;
        control.ValueMemberChanged += (_, _) => valueMemberChanges++;

        var secondSource = new List<Person> { new("Grace", 2, new DateTime(2024, 3, 4)) };
        control.DataSource = secondSource;

        Assert.Equal(string.Empty, control.DisplayMember);
        Assert.Equal(string.Empty, control.ValueMember);
        Assert.Equal(1, displayMemberChanges);
        Assert.Equal(1, valueMemberChanges);
    }

    [Fact]
    public void GetItemText_without_FormattingEnabled_reads_the_DisplayMember_field()
    {
        var person = new Person("Ada", 1, new DateTime(2024, 1, 2));
        var control = new TestListControl { DisplayMember = "Name" };

        Assert.Equal("Ada", control.GetItemText(person));
        Assert.Equal(string.Empty, control.GetItemText(null));
    }

    [Fact]
    public void GetItemText_with_FormattingEnabled_applies_FormatString_through_FormatInfo()
    {
        var person = new Person("Ada", 1, new DateTime(2024, 1, 2));
        var control = new TestListControl
        {
            DisplayMember = "Joined",
            FormattingEnabled = true,
            FormatString = "yyyy-MM-dd",
            FormatInfo = CultureInfo.InvariantCulture,
        };

        Assert.Equal("2024-01-02", control.GetItemText(person));
    }

    [Fact]
    public void GetItemText_lets_the_Format_event_replace_the_displayed_text()
    {
        var person = new Person("Ada", 1, new DateTime(2024, 1, 2));
        var control = new TestListControl { DisplayMember = "Name", FormattingEnabled = true };
        control.Format += (_, e) => e.Value = $"[{e.ListItem}]";

        var text = control.GetItemText(person);

        Assert.Equal($"[{person}]", text);
    }

    [Fact]
    public void Moving_the_CurrencyManagers_Position_moves_SelectedIndex()
    {
        var people = new List<Person> { new("Ada", 1, new DateTime(2024, 1, 2)), new("Grace", 2, new DateTime(2024, 3, 4)) };
        var control = NewBoundControl(people, out var container);

        var manager = (CurrencyManager)container.BindingContext![people];
        manager.Position = 1;

        Assert.Equal(1, control.SelectedIndex);
    }

    [Fact]
    public void Setting_SelectedIndex_moves_the_CurrencyManagers_Position_back()
    {
        var people = new List<Person> { new("Ada", 1, new DateTime(2024, 1, 2)), new("Grace", 2, new DateTime(2024, 3, 4)) };
        var control = NewBoundControl(people, out var container);

        control.SelectedIndex = 1;

        var manager = (CurrencyManager)container.BindingContext![people];
        Assert.Equal(1, manager.Position);
    }

    private sealed record Customer(string Name, List<Order> Orders);

    private sealed record Order(string Code, int Id);

    [Fact]
    public void A_disposed_list_control_stops_following_its_data_manager()
    {
        var people = new List<Person> { new("Ada", 1, default), new("Grace", 2, default) };
        var control = NewBoundControl(people, out var container);
        var manager = container.BindingContext![people];

        control.Dispose();
        manager.Position = 1;

        Assert.Equal(0, control.SelectedIndex);
    }

    [Fact]
    public void A_value_not_in_the_list_selects_nothing()
    {
        var people = new List<Person> { new("Ada", 1, default), new("Grace", 2, default) };
        var control = NewBoundControl(people, out _);
        control.ValueMember = "Id";

        control.SelectedValue = 99;

        Assert.Equal(-1, control.SelectedIndex);
    }

    [Fact]
    public void A_display_member_with_a_navigation_path_reads_the_field_of_the_child_list()
    {
        var customers = new List<Customer> { new("Acme", [new Order("A-1", 1), new Order("A-2", 2)]) };
        var control = NewBoundControl(customers, out _);

        control.DisplayMember = "Orders.Code";

        var orders = control.DataManager!.List;
        Assert.Equal("A-2", control.GetItemText(orders[1]));
    }

    [Fact]
    public void A_refused_value_member_leaves_the_connection_as_it_was()
    {
        var people = new List<Person> { new("Ada", 1, default) };
        var control = NewBoundControl(people, out _);
        var manager = control.DataManager;

        Assert.Throws<ArgumentException>(() => control.ValueMember = "Missing");

        Assert.Same(manager, control.DataManager);
    }
}
