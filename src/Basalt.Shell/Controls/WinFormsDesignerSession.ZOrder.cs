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
        foreach (var (key, children) in Document.ZOrder)
        {
            // A key names a panel as Owner.Panel: each panel orders its own children, as the designer's code does.
            var dot = key.IndexOf('.');
            var owner = dot < 0 ? key : key[..dot];
            if (!_controls.TryGetValue(owner, out var ownerControl))
            {
                continue;
            }
            var parent = HolderOf(ownerControl, dot < 0 ? null : key[(dot + 1)..]);
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
