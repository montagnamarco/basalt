using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// The IArrangedElement.Children a leaf item answers with: a ToolStripItem
/// never has children of its own, only the ArrangedElementCollection type
/// the interface asks for.
/// </summary>
internal sealed class EmptyArrangedElementCollection : ArrangedElementCollection
{
    public static readonly EmptyArrangedElementCollection Instance = new();
}
