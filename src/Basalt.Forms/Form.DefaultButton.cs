namespace System.Windows.Forms;

public partial class Form
{
    private IButtonControl? _defaultButton;

    protected override void UpdateDefaultButton()
    {
        var active = ActiveControl;
        while (active is ContainerControl container && container.ActiveControl is { } child)
            active = child;
        var next = active as IButtonControl ?? AcceptButton;
        if (ReferenceEquals(next, _defaultButton)) return;
        _defaultButton?.NotifyDefault(false);
        _defaultButton = next;
        _defaultButton?.NotifyDefault(true);
    }
}
