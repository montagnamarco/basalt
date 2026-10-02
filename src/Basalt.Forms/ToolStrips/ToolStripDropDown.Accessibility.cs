namespace System.Windows.Forms;

/// <summary>
/// <see cref="ToolStripDropDown.CreateAccessibilityInstance"/>: a drop-down
/// is a menu popup, not a toolbar, and (unlike a plain <see cref="ToolStrip"/>)
/// its accessible Name can be set directly, since there is rarely a label
/// control next to a floating popup for <see cref="Control.ControlAccessibleObject"/>'s
/// usual fallback to find.
/// </summary>
public partial class ToolStripDropDown
{
    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripDropDownAccessibleObject(this);

    public class ToolStripDropDownAccessibleObject : ToolStrip.ToolStripAccessibleObject
    {
        private string? _name;

        public ToolStripDropDownAccessibleObject(ToolStripDropDown owner)
            : base(owner)
        {
        }

        public override string? Name
        {
            get => _name ?? base.Name;
            set => _name = value;
        }

        public override AccessibleRole Role => AccessibleRole.MenuPopup;
    }
}
