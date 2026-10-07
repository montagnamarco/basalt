using System.ComponentModel;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// Materializes the cascading DropDownItems tree a ToolStripDropDownButton or
/// ToolStripSplitButton owns into the live preview, the same shape
/// <see cref="WinFormsCollectionPreview"/> already builds for a MenuStrip's own
/// Items/DropDownItems. Kept in its own file so that method's single Apply() loop only grows by
/// the one call-out that reaches <see cref="Apply"/>, not by a second copy of its
/// dictionary-and-attach logic.
/// </summary>
internal static class WinFormsToolStripDropDownPreview
{
    public static void Apply(WinFormsMenuDocument menus, string ownerName, Forms.ToolStripDropDownItem owner,
        List<string> problems, WinFormsResourceDocument? resources,
        Lazy<IReadOnlyList<WinFormsProjectResourceEntry>?> projectResources)
    {
        var definitions = menus.Read(ownerName);
        var items = new Dictionary<string, Forms.ToolStripItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in definitions)
        {
            if (item.IsSeparator)
            {
                items[item.Name] = new Forms.ToolStripSeparator { Name = item.Name };
                continue;
            }

            var type = typeof(Forms.ToolStripItem).Assembly.GetType("System.Windows.Forms." + item.TypeName, false, true);
            if (type is null)
            {
                problems.Add(item.Name + ": " + item.TypeName);
                continue;
            }

            var created = (Forms.ToolStripItem)Activator.CreateInstance(type)!;
            created.Name = item.Name;
            if (created is Forms.ToolStripMenuItem menuItem) menuItem.Text = item.Text;
            items[item.Name] = created;
        }

        foreach (var item in definitions)
        {
            if (!items.TryGetValue(item.Name, out var created)) continue;
            foreach (var (property, expression) in menus.ReadItemProperties(ownerName, item.Name))
            {
                if (property.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                var descriptor = TypeDescriptor.GetProperties(created)[property];
                if (descriptor is null || descriptor.IsReadOnly) continue;
                try
                {
                    descriptor.SetValue(created,
                        WinFormsCollectionPreview.EvaluateItemProperty(expression, descriptor.PropertyType, resources, projectResources));
                }
                catch
                {
                    problems.Add(item.Name + "." + property + ": " + Localizer.Get(StringKeys.WinFormsCollectionUnsupported));
                }
            }

            if (item.Parent.Equals(ownerName, StringComparison.OrdinalIgnoreCase))
            {
                owner.DropDownItems.Add(created);
            }
            else if (items.TryGetValue(item.Parent, out var parent) && parent is Forms.ToolStripDropDownItem parentOwner)
            {
                parentOwner.DropDownItems.Add(created);
            }
        }
    }
}
