using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task SetZOrderAsync(IReadOnlyList<Forms.Control> controls, bool bringToFront)
    {
        var names = controls.Select(NameOf).ToArray();
        return ApplyAsync(document => document.SetZOrder(names, bringToFront));
    }

    private void ApplyZOrderPreview()
    {
        foreach (var (parentName, children) in Document.ZOrder)
        {
            if (!_controls.TryGetValue(parentName, out var parent))
            {
                continue;
            }
            for (var index = 0; index < children.Count; index++)
            {
                if (_controls.TryGetValue(children[index], out var child) && ReferenceEquals(child.Parent, parent))
                {
                    parent.Controls.SetChildIndex(child, index);
                }
            }
        }
    }
}
