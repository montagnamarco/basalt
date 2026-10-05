using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    internal WinFormsControlClipboard CopyControls(IEnumerable<Forms.Control> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        return Document.CopyControls(controls.Select(NameOf));
    }

    internal async Task<IReadOnlyList<string>> PasteControlsAsync(
        WinFormsControlClipboard clipboard, Forms.Control parent)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(parent);
        var parentName = NameOf(parent);
        IReadOnlyList<string> added = [];
        await ApplyAsync(document =>
        {
            var result = document.PasteControls(clipboard, parentName);
            added = result.TopLevelNames;
            return result.Source;
        });
        return added;
    }
}
