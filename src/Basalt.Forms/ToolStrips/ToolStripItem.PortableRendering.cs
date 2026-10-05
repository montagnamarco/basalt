namespace System.Windows.Forms;

public abstract partial class ToolStripItem
{
    internal bool HasExplicitPortableBackColor => !_backColor.IsEmpty;
    internal bool HasExplicitPortableForeColor => !_foreColor.IsEmpty;
}
