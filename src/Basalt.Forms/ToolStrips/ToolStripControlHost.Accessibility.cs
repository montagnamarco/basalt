namespace System.Windows.Forms;

public partial class ToolStripControlHost
{
    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripHostedControlAccessibleObject(Control, this);

    /// <summary>
    /// What a control host answers for: the hosted Control's own
    /// <see cref="Control.ControlAccessibleObject"/>, wrapped only so the type
    /// identifies which item showed it (an item is not itself a Control, so it
    /// cannot just reuse <see cref="Control.AccessibilityObject"/> directly).
    /// </summary>
    public class ToolStripHostedControlAccessibleObject : Control.ControlAccessibleObject
    {
        private readonly ToolStripControlHost _item;

        public ToolStripHostedControlAccessibleObject(Control toolStripHostedControl, ToolStripControlHost item)
            : base(toolStripHostedControl)
        {
            ArgumentNullException.ThrowIfNull(item);

            _item = item;
        }

        /// <summary>Not part of Microsoft's public contract; kept for whatever later hooks the host item into this object.</summary>
        internal ToolStripControlHost Item => _item;
    }
}
