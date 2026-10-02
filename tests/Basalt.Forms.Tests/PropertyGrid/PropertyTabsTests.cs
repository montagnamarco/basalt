using System.ComponentModel;
using System.Windows.Forms.Design;
using System.Windows.Forms.PropertyGridInternal;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>PropertiesTab (now public, per Microsoft's own reference surface) and EventsTab: what GetProperties lists for a plain component, and what EventsTab's event-row descriptors remember.</summary>
public class PropertyTabsTests
{
    private sealed class Widget : Component
    {
        public string Name { get; set; } = "unnamed";

        public event EventHandler? Clicked;

        public void RaiseClicked() => Clicked?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void PropertiesTab_lists_the_components_browsable_properties()
    {
        var tab = new PropertiesTab();
        var widget = new Widget();

        var properties = tab.GetProperties(widget, null);

        Assert.NotNull(properties["Name"]);
        Assert.Equal("unnamed", properties["Name"]!.GetValue(widget));
    }

    [Fact]
    public void EventsTab_lists_one_row_per_event()
    {
        var tab = new EventsTab(sp: null);
        var widget = new Widget();

        var properties = tab.GetProperties(widget, null);

        var clicked = properties["Clicked"];
        Assert.NotNull(clicked);
        Assert.Equal(typeof(string), clicked!.PropertyType);
    }

    [Fact]
    public void EventsTab_remembers_the_handler_name_it_was_given()
    {
        var tab = new EventsTab(sp: null);
        var widget = new Widget();
        var clicked = tab.GetProperties(widget, null)["Clicked"]!;

        clicked.SetValue(widget, "Widget_Clicked");

        Assert.Equal("Widget_Clicked", clicked.GetValue(widget));
        Assert.True(clicked.CanResetValue(widget));

        clicked.ResetValue(widget);
        Assert.Null(clicked.GetValue(widget));
    }

    [Fact]
    public void EventsTab_CanExtend_is_false_for_an_object_with_no_events()
    {
        var tab = new EventsTab(sp: null);

        Assert.False(tab.CanExtend(42));
        Assert.True(tab.CanExtend(new Widget()));
    }
}
