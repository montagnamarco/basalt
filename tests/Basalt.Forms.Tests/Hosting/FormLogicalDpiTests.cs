using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Hosting;

public sealed class FormLogicalDpiTests
{
    [Theory]
    [InlineData(1.5, 144)]
    [InlineData(2.0, 192)]
    public void HostScalingPreservesLogicalGeometryAndFontWhileReportingPhysicalDpi(double scaling, int dpi)
    {
        using var form = new Form
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            Bounds = new Rectangle(20, 30, 400, 250),
        };
        using var font = new Font("Tahoma", 12f);
        var panel = new Panel { Bounds = new Rectangle(10, 20, 200, 100), Font = font };
        var button = new Button { Bounds = new Rectangle(15, 25, 80, 30) };
        form.Controls.Add(panel);
        panel.Controls.Add(button);
        var formBounds = form.Bounds;
        var panelBounds = panel.Bounds;
        var buttonBounds = button.Bounds;
        var fontSize = button.Font.Size;
        var events = new List<DpiChangedEventArgs>();
        form.DpiChanged += (_, args) => events.Add(args);
        form.InitializeHostScaling(1.0);
        form.OnHostScalingChanged(scaling);
        var changed = Assert.Single(events);
        Assert.Equal(96, changed.DeviceDpiOld);
        Assert.Equal(dpi, changed.DeviceDpiNew);
        Assert.Equal(formBounds, changed.SuggestedRectangle);
        Assert.Equal(formBounds, form.Bounds);
        Assert.Equal(panelBounds, panel.Bounds);
        Assert.Equal(buttonBounds, button.Bounds);
        Assert.Equal(fontSize, button.Font.Size);
        form.OnHostScalingChanged(scaling);
        Assert.Single(events);
        form.OnHostScalingChanged(1.0);
        Assert.Equal(2, events.Count);
        Assert.Equal(dpi, events[1].DeviceDpiOld);
        Assert.Equal(96, events[1].DeviceDpiNew);
        Assert.Equal(formBounds, form.Bounds);
        Assert.Equal(buttonBounds, button.Bounds);
    }
}
