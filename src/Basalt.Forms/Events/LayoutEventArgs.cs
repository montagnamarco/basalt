using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>Which component and which of its properties caused a layout.</summary>
public sealed class LayoutEventArgs : EventArgs
{
    public LayoutEventArgs(IComponent? affectedComponent, string? affectedProperty)
    {
        AffectedComponent = affectedComponent;
        AffectedProperty = affectedProperty;
    }

    public LayoutEventArgs(Control? affectedControl, string? affectedProperty)
        : this((IComponent?)affectedControl, affectedProperty)
    {
    }

    public IComponent? AffectedComponent { get; }

    /// <summary>The affected component when it is a control, otherwise null.</summary>
    public Control? AffectedControl => AffectedComponent as Control;

    public string? AffectedProperty { get; }
}
