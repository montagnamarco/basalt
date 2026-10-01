namespace System.Windows.Forms;

/// <summary>A control that keeps track of which of its children is active.</summary>
public interface IContainerControl
{
    Control? ActiveControl { get; set; }

    bool ActivateControl(Control active);
}
