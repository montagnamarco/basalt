using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    // Visual Studio offers this combo in this fixed order on every ToolStrip/MenuStrip/StatusStrip.
    private static readonly string[] SmartTagDockOptions = ["Top", "Bottom", "Left", "Right", "Fill", "None"];

    private WinFormsSmartTag? _smartTag;

    private void InitializeSmartTag()
    {
        _surface.SelectionChanged += RefreshSmartTag;
    }

    private void RefreshSmartTag(Forms.Control? selected)
    {
        if (_session is not { } session || selected is null || ReferenceEquals(selected, session.Form) ||
            !ReferenceEquals(_surface.SelectedControl, selected))
        {
            _surface.ShowSmartTag(null, null);
            return;
        }

        var items = BuildSmartTagItems(session, selected);
        if (items.Count == 0)
        {
            _surface.ShowSmartTag(null, null);
            return;
        }

        _smartTag ??= new WinFormsSmartTag();
        _smartTag.Show(session.NameOf(selected), selected.GetType().Name, items);
        _surface.ShowSmartTag(_smartTag, selected);
    }

    private List<WinFormsSmartTagItem> BuildSmartTagItems(WinFormsDesignerSession session, Forms.Control selected)
    {
        var items = new List<WinFormsSmartTagItem>();

        // A ContextMenuStrip's drop-down is a ToolStrip subtype too, but it is never placed on
        // the design surface with bounds of its own, so it never reaches this method in practice;
        // excluded anyway so a future caller cannot show a glyph with nothing underneath it.
        if (selected is Forms.ToolStrip strip and not Forms.ToolStripDropDown)
        {
            var name = session.NameOf(strip);
            var isMenu = strip is Forms.MenuStrip;

            // Visual Studio offers no "Insert Standard Items" task on a StatusStrip at all (its
            // own standard content - one ToolStripStatusLabel - comes from the "Type Here"
            // editor, not this command); greyed out, not omitted, once the strip already has
            // items, the same way Visual Studio's own smart tag does.
            if (strip is not Forms.StatusStrip)
            {
                items.Add(new WinFormsSmartTagCommand(Localizer.Get(StringKeys.WinFormsSmartTagInsertStandardItems),
                    () => RunEditAsync(() => InsertStandardItemsAsync(session, name, isMenu)),
                    Enabled: strip.Items.Count == 0));
            }

            // The MenuStrip's full tree already has a modal editor the Properties grid's "Items"
            // ellipsis opens (EditMenuAsync); ToolStrip/StatusStrip items only have the inline
            // "Type Here" editor, not a collection editor, so they get no "Edit Items…" entry.
            if (isMenu)
            {
                items.Add(new WinFormsSmartTagCommand(Localizer.Get(StringKeys.WinFormsSmartTagEditItems),
                    () => OpenCollectionEditor(strip, "Items")));
            }

            items.Add(new WinFormsSmartTagChoice(Localizer.Get(StringKeys.WinFormsSmartTagDock), SmartTagDockOptions,
                strip.Dock.ToString(), value => RunEditAsync(() => session.SetPropertyAsync(strip, "Dock", value))));
            return items;
        }

        if (IsSmartTagDockable(selected))
        {
            var dockedFull = selected.Dock == Forms.DockStyle.Fill;
            items.Add(new WinFormsSmartTagCommand(
                Localizer.Get(dockedFull ? StringKeys.WinFormsSmartTagUndockInParent : StringKeys.WinFormsSmartTagDockInParent),
                () => RunEditAsync(() => session.SetPropertyAsync(selected, "Dock",
                    dockedFull ? nameof(Forms.DockStyle.None) : nameof(Forms.DockStyle.Fill)))));

            if (selected is Forms.PictureBox pictureBox)
            {
                items.Add(new WinFormsSmartTagChoice(Localizer.Get(StringKeys.WinFormsSmartTagSizeMode),
                    Enum.GetNames<Forms.PictureBoxSizeMode>(), pictureBox.SizeMode.ToString(),
                    value => RunEditAsync(() => session.SetPropertyAsync(pictureBox, "SizeMode", value))));
            }
        }

        return items;
    }

    private static bool IsSmartTagDockable(Forms.Control control) => control switch
    {
        Forms.Panel or Forms.TreeView or Forms.ListView or Forms.DataGridView or Forms.PictureBox or
            Forms.SplitContainer or Forms.TabControl => true,
        Forms.TextBox textBox => textBox.Multiline,
        _ => false,
    };

    private Task InsertStandardItemsAsync(WinFormsDesignerSession session, string name, bool isMenu)
    {
        var original = session.Document;
        return session.ApplyCollectionAsync(current =>
        {
            if (!ReferenceEquals(current, original))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }

            if (isMenu)
            {
                var menu = current.Menus;
                var items = menu.Read(name).ToList();
                var added = WinFormsStandardItems.BuildMenuItems(name, current.DeclaredNames);
                items.AddRange(added);
                var text = menu.Set(name, items);

                // Written as a second pass over the text Set() just produced, through
                // SetItemProperty, rather than by teaching WinFormsMenuItem about ShortcutKeys:
                // the whole method is still one ApplyCollectionAsync call, so it lands as the
                // single undo step Visual Studio's own "Insert Standard Items" is.
                foreach (var (itemName, expression) in WinFormsStandardItems.MenuItemShortcutKeys(added))
                {
                    text = new WinFormsMenuDocument(text).SetItemProperty(name, itemName, "ShortcutKeys", expression);
                }
                return text;
            }

            var toolStrip = current.ToolStripItems;
            var stripItems = toolStrip.Read(name).ToList();
            stripItems.AddRange(WinFormsStandardItems.BuildToolStripItems(current.DeclaredNames));
            return toolStrip.Set(name, stripItems);
        });
    }
}
