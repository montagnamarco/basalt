using System.ComponentModel;
using System.Data;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataBinding;

/// <summary>
/// The CurrencyManager a list binds through: one Position shared by every
/// bound control, one BindingContext entry shared by every control in the
/// same container, and the same navigation over a DataTable's DataView.
/// </summary>
public class CurrencyManagerTests
{
    private sealed class Person
    {
        public string? Name { get; set; }
    }

    private static Control NewContainerWithChild(out Control child)
    {
        var container = new Control { BindingContext = new BindingContext() };
        child = new Control { Parent = container };

        return container;
    }

    [Fact]
    public void Moving_the_position_pushes_the_new_row_into_every_bound_control()
    {
        var people = new BindingList<Person> { new() { Name = "Ada" }, new() { Name = "Grace" } };
        var container = NewContainerWithChild(out var first);
        var second = new Control { Parent = container };

        first.DataBindings.Add("Text", people, "Name");
        second.DataBindings.Add("Text", people, "Name");

        Assert.Equal("Ada", first.Text);
        Assert.Equal("Ada", second.Text);

        var manager = (CurrencyManager)container.BindingContext![people];
        manager.Position = 1;

        Assert.Equal("Grace", first.Text);
        Assert.Equal("Grace", second.Text);
    }

    [Fact]
    public void Two_controls_in_the_same_container_bound_to_the_same_source_share_one_manager()
    {
        var people = new BindingList<Person> { new() { Name = "Ada" } };
        var container = NewContainerWithChild(out var first);
        var second = new Control { Parent = container };

        first.DataBindings.Add("Text", people, "Name");
        second.DataBindings.Add("Text", people, "Name");

        Assert.Same(first.DataBindings["Text"]!.BindingManagerBase, second.DataBindings["Text"]!.BindingManagerBase);
    }

    [Fact]
    public void A_control_without_its_own_binding_context_inherits_its_parents()
    {
        var container = NewContainerWithChild(out var child);

        Assert.Same(container.BindingContext, child.BindingContext);
    }

    [Fact]
    public void A_data_table_binds_through_its_default_view_and_navigates_rows()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add("Ada");
        table.Rows.Add("Grace");

        var textBox = new Control { BindingContext = new BindingContext() };
        textBox.DataBindings.Add("Text", table, "Name");

        Assert.Equal("Ada", textBox.Text);

        var manager = (CurrencyManager)textBox.BindingContext![table];
        manager.Position = 1;

        Assert.Equal("Grace", textBox.Text);
    }

    [Fact]
    public void Binding_directly_to_a_DataView_navigates_the_same_way_as_through_its_table()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add("Ada");
        table.Rows.Add("Grace");
        var view = new DataView(table);

        var textBox = new Control { BindingContext = new BindingContext() };
        textBox.DataBindings.Add("Text", view, "Name");

        Assert.Equal("Ada", textBox.Text);

        var manager = (CurrencyManager)textBox.BindingContext![view];
        manager.Position = 1;

        Assert.Equal("Grace", textBox.Text);
    }
}
