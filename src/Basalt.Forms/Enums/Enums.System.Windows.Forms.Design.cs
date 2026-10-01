// The enumerations of this namespace, with the names and values of Microsoft's
// System.Windows.Forms reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Windows.Forms.Design;

[Flags]
public enum ToolStripItemDesignerAvailability
{
    None = 0,
    ToolStrip = 1,
    MenuStrip = 2,
    ContextMenuStrip = 4,
    StatusStrip = 8,
    All = 15,
}
