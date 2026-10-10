using System.ComponentModel;
using System.Reflection;

namespace Basalt.Designer.Toolbox;

/// <param name="ControlType">
/// The Forms type the entry creates. The Pointer entry has no real control
/// behind it, so it carries a private marker type and overrides below rather
/// than being asked to resolve a category or a name from it.
/// </param>
/// <param name="DisplayNameOverride">Set only by <see cref="WinFormsToolboxCatalog.Pointer"/>.</param>
/// <param name="CategoryOverride">Set only by <see cref="WinFormsToolboxCatalog.Pointer"/>.</param>
public sealed record WinFormsToolboxItem(Type ControlType, string? DisplayNameOverride = null, string? CategoryOverride = null)
{
    public string DisplayName => DisplayNameOverride ?? ControlType.Name;

    /// <summary>The group the control is listed under, as Visual Studio's toolbox names them.</summary>
    public string Category => CategoryOverride ?? WinFormsToolboxCatalog.CategoryOf(ControlType);

    public override string ToString() => DisplayName;
}

/// <summary>Discovers usable controls from the supplied Forms or custom-control assembly.</summary>
public static class WinFormsToolboxCatalog
{
    public static IReadOnlyList<WinFormsToolboxItem> Describe(Assembly assembly, Type controlBaseType) =>
        SafeTypes(assembly)
            .Where(type => type.IsVisible && !type.IsAbstract && !type.ContainsGenericParameters &&
                type != controlBaseType && controlBaseType.IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .Where(type => TypeDescriptor.GetAttributes(type)[typeof(ToolboxItemAttribute)] is not ToolboxItemAttribute attribute ||
                !attribute.Equals(ToolboxItemAttribute.None))
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .Select(type => new WinFormsToolboxItem(type)).ToArray();

    /// <summary>
    /// The toolbox group of a Forms control or component.
    /// </summary>
    /// <remarks>
    /// Derived here, from the type's name, rather than in the view: the lists
    /// follow Visual Studio's Windows Forms toolbox so the controls are where a
    /// Forms developer looks for them, and a custom control from the user's own
    /// assembly, which no list names, falls under "Components" as it does in
    /// Visual Studio. Names rather than base types because the grouping is a
    /// convention and not a property the types express.
    /// </remarks>
    public static string CategoryOf(Type type)
    {
        var name = type.Name;
        var category = Categories.FirstOrDefault(entry => entry.Names.Contains(name));

        return category.Title ?? Components;
    }

    /// <summary>
    /// The same items under another group: a project's own controls are listed under
    /// "&lt;Project&gt; Components", a group no built-in category names.
    /// </summary>
    public static IReadOnlyList<WinFormsToolboxItem> UnderCategory(IEnumerable<WinFormsToolboxItem> items, string category) =>
        items.Select(item => item with { CategoryOverride = category }).ToArray();

    /// <summary>
    /// Items grouped by category in toolbox order, then by name within each group. A category that
    /// is not one of <see cref="CategoryOrder"/> (a project's own group) follows all of them, so the
    /// built-in groups keep their places whatever the project is called.
    /// </summary>
    public static IReadOnlyList<WinFormsToolboxItem> Order(IEnumerable<WinFormsToolboxItem> items) =>
        items.OrderBy(item => CategoryRank(item.Category))
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    private static int CategoryRank(string category)
    {
        var index = CategoryOrder.ToList().IndexOf(category);
        return index < 0 ? CategoryOrder.Count : index;
    }

    public const string Common = "Common Controls";
    public const string Containers = "Containers";
    public const string MenusAndToolbars = "Menus & Toolbars";
    public const string Data = "Data";
    public const string Components = "Components";
    public const string Printing = "Printing";
    public const string Dialogs = "Dialogs";

    /// <summary>
    /// Pointer's category: the empty string rather than a named group, so
    /// <see cref="ToolboxListing.Build"/> gives it no heading of its own and it
    /// simply leads the list, the way VB6 and Visual Studio lead their toolbox.
    /// </summary>
    public const string PointerCategory = "";

    /// <summary>Marker type for the Pointer entry; never instantiated, never a real Forms control.</summary>
    private sealed class PointerToolMarker;

    /// <summary>
    /// The toolbox's permanent first entry. Activating it places nothing: it
    /// cancels whatever control placement was pending and returns the toolbox
    /// to plain selection, exactly as Pointer does in VB6 and Visual Studio.
    /// </summary>
    /// <summary>Whether a toolbox entry is <see cref="Pointer"/> rather than a control.</summary>
    public static bool IsPointer(Type type) => type == typeof(PointerToolMarker);

    public static readonly WinFormsToolboxItem Pointer =
        new(typeof(PointerToolMarker), DisplayNameOverride: "Pointer", CategoryOverride: PointerCategory);

    /// <summary>Category titles in the order the toolbox shows them.</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } =
        [PointerCategory, Common, Containers, MenusAndToolbars, Data, Components, Printing, Dialogs];

    private static readonly (string Title, HashSet<string> Names)[] Categories =
    [
        (Common, ["Button", "CheckBox", "CheckedListBox", "ComboBox", "DateTimePicker", "Label", "LinkLabel", "ListBox",
            "ListView", "MaskedTextBox", "MonthCalendar", "NotifyIcon", "NumericUpDown", "PictureBox", "ProgressBar",
            "RadioButton", "RichTextBox", "TextBox", "ToolTip", "TreeView", "WebBrowser", "DomainUpDown", "HScrollBar",
            "VScrollBar", "TrackBar", "PropertyGrid", "HelpProvider", "ErrorProvider"]),
        (Containers, ["FlowLayoutPanel", "GroupBox", "Panel", "SplitContainer", "TabControl", "TabPage", "TableLayoutPanel",
            "SplitterPanel", "Splitter", "UserControl"]),
        (MenusAndToolbars, ["ContextMenuStrip", "MenuStrip", "StatusStrip", "ToolStrip", "ToolStripContainer",
            "ToolStripPanel", "ToolStripProgressBar", "ToolStripComboBox", "ToolStripTextBox"]),
        (Data, ["DataGridView", "BindingNavigator", "BindingSource", "DataGrid", "DataSet", "Chart"]),
        (Printing, ["PrintDocument", "PrintPreviewControl", "PrintPreviewDialog", "PageSetupDialog", "PrintDialog"]),
        (Dialogs, ["ColorDialog", "FolderBrowserDialog", "FontDialog", "OpenFileDialog", "SaveFileDialog"]),
    ];

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
