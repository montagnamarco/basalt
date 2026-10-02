namespace System.Windows.Forms;

public partial class ToolStripSplitButton
{
    /// <summary>
    /// What <see cref="CreateAccessibilityInstance"/> answers: the default
    /// action is the button half's own click, not opening the submenu (unlike
    /// a plain drop-down item, a split button's main part is the one most
    /// often invoked, the arrow a secondary way to reach the rest).
    /// </summary>
    public class ToolStripSplitButtonAccessibleObject : ToolStripItem.ToolStripItemAccessibleObject
    {
        public ToolStripSplitButtonAccessibleObject(ToolStripSplitButton ownerItem)
            : base(ownerItem)
        {
        }

        public override void DoDefaultAction() => ((ToolStripSplitButton)OwnerItem).PerformButtonClick();
    }
}
