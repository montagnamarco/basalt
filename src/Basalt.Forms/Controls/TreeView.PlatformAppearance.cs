using System.Drawing;

namespace System.Windows.Forms;

public partial class TreeView
{
    private Color SelectionBackColor => PortableVisualStylesEnabled
        ? CollectionVisualAppearance.SelectionField(this, Focused)
        : Focused ? SystemColors.Highlight : SystemColors.Control;

    private Color SelectionForeColor => PortableVisualStylesEnabled
        ? CollectionVisualAppearance.SelectionText(this, Focused)
        : Focused ? SystemColors.HighlightText : SystemColors.WindowText;
}
