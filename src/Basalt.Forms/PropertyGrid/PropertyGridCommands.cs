using System.ComponentModel.Design;

namespace System.Windows.Forms.PropertyGridInternal;

/// <summary>
/// The command IDs PropertyGrid's context menu would register with a VS-style
/// shell command bar (Hide/Reset/Description/Commands); this family's
/// PropertyGrid draws its own fixed context menu instead of going through
/// IMenuCommandService, so these exist for shape compatibility only.
/// </summary>
public class PropertyGridCommands
{
    protected static readonly Guid wfcMenuGroup = new("a72a5c7c-6d19-4ab2-9ae6-0f3a8b7e7c23");

    protected static readonly Guid wfcMenuCommand = new("a72a5c7c-6d19-4ab2-9ae6-0f3a8b7e7c24");

    public static readonly CommandID Commands = new(wfcMenuGroup, 0x2001);

    public static readonly CommandID Description = new(wfcMenuGroup, 0x2002);

    public static readonly CommandID Hide = new(wfcMenuGroup, 0x2003);

    public static readonly CommandID Reset = new(wfcMenuGroup, 0x2004);

    public PropertyGridCommands()
    {
    }
}
