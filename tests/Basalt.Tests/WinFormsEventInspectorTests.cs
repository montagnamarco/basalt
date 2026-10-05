using System.ComponentModel;
using Basalt.Designer;

namespace Basalt.Tests;

public class WinFormsEventInspectorTests
{
    [Fact]
    public void DescribesRealFormsEventsAndTheirDelegateTypes()
    {
        using var button = new System.Windows.Forms.Button();
        var events = WinFormsEventInspector.Describe(button, name => name == "Click" ? "Button1_Click" : null);
        var click = Assert.Single(events, item => item.Name == "Click");
        Assert.Equal(typeof(EventHandler), click.EventType);
        Assert.Equal("Button1_Click", click.HandlerName);
        Assert.True(click.IsDefault);
        Assert.Equal("Click", events[0].Name);
        Assert.Contains(events, item => item.Name == "MouseDown" && item.EventType == typeof(System.Windows.Forms.MouseEventHandler));
    }

    [Fact]
    public void HonorsBrowsableDefaultAndDescriptionMetadata()
    {
        var events = WinFormsEventInspector.Describe(typeof(TestComponent));
        var changed = Assert.Single(events);
        Assert.Equal("Changed", changed.Name);
        Assert.True(changed.IsDefault);
        Assert.Equal("Behavior", changed.Category);
        Assert.Equal("Raised after changing the value.", changed.Description);
    }

    [DefaultEvent("Changed")]
    public sealed class TestComponent
    {
        [Category("Behavior"), Description("Raised after changing the value.")]
        public event EventHandler Changed { add { } remove { } }
        [Browsable(false)]
        public event EventHandler Hidden { add { } remove { } }
    }
}
