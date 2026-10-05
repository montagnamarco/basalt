using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class ContainerCompatibilityTests
{
    private const BindingFlags DeclaredOnly = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void ToolStripContainer_declares_and_forwards_ambient_colour_and_change_event()
    {
        var container = new ToolStripContainer();
        Assert.NotNull(typeof(ToolStripContainer).GetProperty(nameof(ToolStripContainer.ForeColor), DeclaredOnly));
        Assert.NotNull(typeof(ToolStripContainer).GetEvent(nameof(ToolStripContainer.ForeColorChanged), DeclaredOnly));

        var changes = 0;
        container.ForeColorChanged += (_, _) => changes++;
        container.ForeColor = Color.Blue;

        Assert.Equal(Color.Blue, ((Control)container).ForeColor);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ContentPanel_declares_and_forwards_tab_index()
    {
        var panel = new ToolStripContentPanel();
        Assert.NotNull(typeof(ToolStripContentPanel).GetProperty(nameof(ToolStripContentPanel.TabIndex), DeclaredOnly));

        var changes = 0;
        panel.TabIndexChanged += (_, _) => changes++;
        panel.TabIndex = 9;

        Assert.Equal(9, ((Control)panel).TabIndex);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void UserControl_declares_validation_api_and_forwards_change_event()
    {
        var control = new UserControl();
        Assert.NotNull(typeof(UserControl).GetMethod(nameof(UserControl.ValidateChildren), DeclaredOnly, [typeof(ValidationConstraints)]));
        Assert.NotNull(typeof(UserControl).GetEvent(nameof(UserControl.AutoValidateChanged), DeclaredOnly));

        var changes = 0;
        control.AutoValidateChanged += (_, _) => changes++;
        control.AutoValidate = AutoValidate.Disable;

        Assert.Equal(AutoValidate.Disable, ((ContainerControl)control).AutoValidate);
        Assert.Equal(1, changes);
    }
}
