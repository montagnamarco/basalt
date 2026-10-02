using System.Windows.Forms;

namespace Microsoft.VisualBasic.CompilerServices;

/// <summary>What a VB host (the IDE's designer, or a custom host) gives the runtime so My.Forms' error dialogs and InputBox can parent themselves and pick up the running application's title.</summary>
public interface IVbHost
{
    IWin32Window GetParentWindow();

    string GetWindowTitle();
}
