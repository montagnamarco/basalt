using System.ComponentModel;
using System.Reflection;

namespace Basalt.Designer.Toolbox;

public sealed record WinFormsToolboxItem(Type ControlType)
{
    public string DisplayName => ControlType.Name;

    /// <summary>The group the control is listed under, as Visual Studio's toolbox names them.</summary>
    public string Category => WinFormsToolboxCatalog.CategoryOf(ControlType);

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

    /// <summary>Items grouped by category in toolbox order, then by name within each group.</summary>
    public static IReadOnlyList<WinFormsToolboxItem> Order(IEnumerable<WinFormsToolboxItem> items) =>
        items.OrderBy(item => CategoryOrder.ToList().IndexOf(item.Category))
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public const string Common = "Common Controls";
    public const string Containers = "Containers";
    public const string MenusAndToolbars = "Menus & Toolbars";
    public const string Data = "Data";
    public const string Components = "Components";
    public const string Printing = "Printing";
    public const string Dialogs = "Dialogs";

    /// <summary>Category titles in the order the toolbox shows them.</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } =
        [Common, Containers, MenusAndToolbars, Data, Components, Printing, Dialogs];

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
