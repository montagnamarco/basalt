namespace Basalt.Workspace;

/// <summary>
/// The fixed content Visual Studio's "Insert Standard Items" smart-tag task writes for a
/// MenuStrip, ToolStrip or StatusStrip. Built as flat item lists ready for
/// <see cref="WinFormsMenuDocument.Set"/> and <see cref="WinFormsToolStripDocument.Set"/>, so the
/// WinForms designer's smart tag (<c>WinFormsSmartTag</c> in Basalt.Shell) can append them to
/// whatever items a strip already has without knowing the underlying VB syntax, the same way the
/// "Type Here" inline editors already append one item at a time.
/// </summary>
public static class WinFormsStandardItems
{
    private sealed record MenuChild(string? FieldPrefix, string? Text);

    private static readonly (string FieldPrefix, string Text, MenuChild[] Children)[] MenuTemplate =
    [
        ("File", "&File", new[]
        {
            new MenuChild("New", "&New"), new MenuChild("Open", "&Open"), new MenuChild(null, null),
            new MenuChild("Save", "&Save"), new MenuChild("SaveAs", "Save &As"), new MenuChild(null, null),
            new MenuChild("Print", "&Print"), new MenuChild("PrintPreview", "Print Pre&view"), new MenuChild(null, null),
            new MenuChild("Exit", "E&xit"),
        }),
        ("Edit", "&Edit", new[]
        {
            new MenuChild("Undo", "&Undo"), new MenuChild("Redo", "&Redo"), new MenuChild(null, null),
            new MenuChild("Cut", "Cu&t"), new MenuChild("Copy", "&Copy"), new MenuChild("Paste", "&Paste"),
            new MenuChild(null, null), new MenuChild("SelectAll", "Select &All"),
        }),
        ("Tools", "&Tools", new[]
        {
            new MenuChild("Customize", "&Customize"), new MenuChild("Options", "&Options"),
        }),
        ("Help", "&Help", new[]
        {
            new MenuChild("Contents", "&Contents"), new MenuChild("Index", "&Index"), new MenuChild("Search", "&Search"),
            new MenuChild(null, null), new MenuChild("About", "&About..."),
        }),
    ];

    private static readonly (string FieldPrefix, string Text)?[] ToolStripTemplate =
    [
        ("New", "&New"), ("Open", "&Open"), ("Save", "&Save"), ("Print", "&Print"), null,
        ("Cut", "Cu&t"), ("Copy", "&Copy"), ("Paste", "&Paste"), null, ("Help", "&Help"),
    ];

    // Visual Studio sets ShortcutKeys on this fixed subset of the MenuTemplate's conventional
    // text, the same way it would for any ToolStripMenuItem field named this way; looked up by
    // Text (fixed and unique within one insert) rather than by field name, since a naming
    // collision can rename the field Unique() below actually returns.
    private static readonly Dictionary<string, string> MenuShortcutKeys = new(StringComparer.Ordinal)
    {
        ["&New"] = "N", ["&Open"] = "O", ["&Save"] = "S", ["&Print"] = "P",
        ["&Undo"] = "Z", ["&Redo"] = "Y", ["Cu&t"] = "X", ["&Copy"] = "C", ["&Paste"] = "V",
    };

    /// <summary>The MenuStrip "&amp;File"/"&amp;Edit"/"&amp;Tools"/"&amp;Help" tree Visual Studio
    /// writes, with every field name made unique against <paramref name="declaredNames"/> (which
    /// already includes whatever the strip itself owns, so a second insert never collides with
    /// the first).</summary>
    public static IReadOnlyList<WinFormsMenuItem> BuildMenuItems(string stripName, IEnumerable<string> declaredNames)
    {
        var declared = new HashSet<string>(declaredNames, StringComparer.OrdinalIgnoreCase);
        var items = new List<WinFormsMenuItem>();
        foreach (var (prefix, text, children) in MenuTemplate)
        {
            var topName = Unique(declared, prefix + "ToolStripMenuItem");
            items.Add(new WinFormsMenuItem(topName, stripName, text));
            foreach (var child in children)
            {
                // Separators share one generic name, so Visual Studio numbers every one of
                // them from 1 (ToolStripSeparator1, ToolStripSeparator2, …); the conventional
                // names above (NewToolStripMenuItem, …) are already unique on their own and
                // only gain a suffix if a name they would otherwise collide with is declared.
                items.Add(child.FieldPrefix is null
                    ? new WinFormsMenuItem(UniqueNumbered(declared, "ToolStripSeparator"), topName, "", IsSeparator: true)
                    : new WinFormsMenuItem(Unique(declared, child.FieldPrefix + "ToolStripMenuItem"), topName, child.Text!));
            }
        }
        return items;
    }

    /// <summary>The ShortcutKeys Visual Studio sets on the subset of the items <paramref
    /// name="added"/> just built (from <see cref="BuildMenuItems"/>) that have one, ready for
    /// <see cref="WinFormsMenuDocument.SetItemProperty"/>.</summary>
    public static IReadOnlyList<(string ItemName, string Expression)> MenuItemShortcutKeys(
        IReadOnlyList<WinFormsMenuItem> added) =>
        added.Where(item => MenuShortcutKeys.ContainsKey(item.Text))
            .Select(item => (item.Name,
                $"CType((System.Windows.Forms.Keys.Control Or System.Windows.Forms.Keys.{MenuShortcutKeys[item.Text]}), System.Windows.Forms.Keys)"))
            .ToList();

    /// <summary>The flat ToolStrip "New, Open, Save, Print, Cut, Copy, Paste, Help" set Visual
    /// Studio writes, named the same way as <see cref="BuildMenuItems"/>. A StatusStrip has no
    /// "Insert Standard Items" task of its own in Visual Studio, so it never reaches here.</summary>
    public static IReadOnlyList<WinFormsToolStripItem> BuildToolStripItems(IEnumerable<string> declaredNames)
    {
        var declared = new HashSet<string>(declaredNames, StringComparer.OrdinalIgnoreCase);
        var items = new List<WinFormsToolStripItem>();
        foreach (var entry in ToolStripTemplate)
        {
            items.Add(entry is { } button
                ? new WinFormsToolStripItem(Unique(declared, button.FieldPrefix + "ToolStripButton"), "ToolStripButton", button.Text)
                : new WinFormsToolStripItem(Unique(declared, "ToolStripSeparator"), "ToolStripSeparator", ""));
        }
        return items;
    }

    private static string Unique(HashSet<string> declared, string prefix)
    {
        if (declared.Add(prefix)) return prefix;
        var suffix = 2;
        while (!declared.Add(prefix + suffix)) suffix++;
        return prefix + suffix;
    }

    private static string UniqueNumbered(HashSet<string> declared, string prefix)
    {
        var suffix = 1;
        while (!declared.Add(prefix + suffix)) suffix++;
        return prefix + suffix;
    }
}
