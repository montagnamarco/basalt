using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataBinding;

/// <summary>Bindings and managers across list changes, context changes, disposal and collection.</summary>
public class BindingLifetimeTests
{
    private sealed class Person(string name)
    {
        public string Name { get; set; } = name;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_a_row_before_the_current_one_keeps_the_same_row_current(bool bindingList)
    {
        IList<Person> people = bindingList
            ? new BindingList<Person> { new("A"), new("B"), new("C") }
            : new List<Person> { new("A"), new("B"), new("C") };
        var manager = (CurrencyManager)new BindingContext()[people];
        manager.Position = 1;
        var positionChanges = 0;
        manager.PositionChanged += (_, _) => positionChanges++;

        manager.RemoveAt(0);

        Assert.Equal("B", ((Person)manager.Current!).Name);
        Assert.Equal(0, manager.Position);
        Assert.Equal(1, positionChanges);
    }

    [Fact]
    public void Inserting_a_row_before_the_current_one_keeps_the_same_row_current()
    {
        var people = new BindingList<Person> { new("A"), new("B") };
        var manager = (CurrencyManager)new BindingContext()[people];
        manager.Position = 1;

        people.Insert(0, new Person("Z"));

        Assert.Equal("B", ((Person)manager.Current!).Name);
    }

    [Fact]
    public void Removing_the_last_rows_leaves_no_current_position()
    {
        var people = new BindingList<Person> { new("A") };
        var manager = (CurrencyManager)new BindingContext()[people];

        people.RemoveAt(0);

        Assert.Equal(-1, manager.Position);
    }

    [Fact]
    public void A_control_given_a_new_binding_context_rebinds_to_it()
    {
        var first = new BindingList<Person> { new("A"), new("B") };
        var control = new Control { BindingContext = new BindingContext() };
        control.DataBindings.Add("Text", first, "Name");

        var context = new BindingContext();
        context[first].Position = 1;
        control.BindingContext = context;

        Assert.Equal("B", control.Text);
    }

    [Fact]
    public void A_disposed_control_no_longer_listens_to_its_source()
    {
        var people = new BindingList<Person> { new("A"), new("B") };
        var context = new BindingContext();
        var control = new Control { BindingContext = context };
        control.DataBindings.Add("Text", people, "Name");

        control.Dispose();
        context[people].Position = 1;

        Assert.Empty(control.DataBindings.Cast<Binding>());
        Assert.Equal("A", control.Text);
    }

    [Fact]
    public void A_binding_context_does_not_keep_an_abandoned_data_source_alive()
    {
        var context = new BindingContext();
        var reference = BindAndForget(context);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(reference.TryGetTarget(out _));
        GC.KeepAlive(context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> BindAndForget(BindingContext context)
    {
        var source = new List<Person> { new("A") };
        _ = context[source];

        return new WeakReference<object>(source);
    }
}
