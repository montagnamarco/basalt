using Basalt.Designer;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    internal WinFormsSourceValueState SourceValueState(Forms.Control control, string property)
    {
        var key = (NameOf(control), property);
        if (ResourceKeyFor(key.Item1, property) is not null && _resourceProperties.Contains(key))
            return _unresolvedProperties.Contains(key)
                ? WinFormsSourceValueState.ResourceBackedUnresolved : WinFormsSourceValueState.ResourceBacked;
        if (!Model(control).Properties.ContainsKey(property)) return WinFormsSourceValueState.Absent;
        return _unresolvedProperties.Contains(key)
            ? WinFormsSourceValueState.Unresolved : WinFormsSourceValueState.Evaluated;
    }
}
