using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task SetTabIndicesBatchAsync(IReadOnlyList<WinFormsTabIndexEdit> edits)
    {
        var changes = new List<PropertyChange>();
        foreach (var edit in edits)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(edit.After);
            changes.Add(ValueChange(edit.Control, "TabIndex", edit.After));
        }
        return ApplyPropertyChangesAsync(changes);
    }
}
