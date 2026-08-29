namespace Basalt.Designer.Toolbox;

/// <summary>Toolbox entry that can be dragged onto the design surface.</summary>
public sealed record ToolboxItem(
    string DisplayName,
    string ElementName,
    string Category,
    string DefaultXaml)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Controls offered by the designer. The default XAML already includes the
/// properties that make the control visible as soon as it is inserted: a control
/// with no content or size would be invisible and would look like it was never
/// inserted.
/// </summary>
public static class ToolboxCatalog
{
    /// <summary>
    /// Controls a plugin has added.
    /// </summary>
    /// <remarks>
    /// Kept apart from the built-in list rather than mixed into it, so the
    /// two can be told apart: a plugin that is removed should take its
    /// controls with it, and a built-in one can never be shadowed by
    /// accident.
    /// </remarks>
    private static readonly List<ToolboxItem> Added = [];

    /// <summary>Adds a control, as a plugin does.</summary>
    public static void Add(ToolboxItem item)
    {
        Added.RemoveAll(existing =>
            string.Equals(existing.ElementName, item.ElementName, StringComparison.Ordinal));

        Added.Add(item);
    }

    /// <summary>Everything on offer, built in and contributed.</summary>
    public static IReadOnlyList<ToolboxItem> All => [.. Items, .. Added];

    public static IReadOnlyList<ToolboxItem> Items { get; } =
    [
        // Layout containers
        new("Grid", "Grid", "Layout", "<Grid />"),
        new("Stack Panel", "StackPanel", "Layout", "<StackPanel />"),
        new("Dock Panel", "DockPanel", "Layout", "<DockPanel />"),
        new("Wrap Panel", "WrapPanel", "Layout", "<WrapPanel />"),
        new("Canvas", "Canvas", "Layout", "<Canvas />"),
        new("Border", "Border", "Layout", """<Border BorderBrush="Gray" BorderThickness="1" Padding="8" />"""),
        new("Scroll Viewer", "ScrollViewer", "Layout", "<ScrollViewer />"),
        new("Separator", "Separator", "Layout", "<Separator />"),
        new("Expander", "Expander", "Layout", """<Expander Header="Expander" />"""),

        // Common controls
        new("Button", "Button", "Common", """<Button Content="Button" />"""),
        new("Text Block", "TextBlock", "Common", """<TextBlock Text="Text" />"""),
        new("Text Box", "TextBox", "Common", """<TextBox Width="150" />"""),
        new("Check Box", "CheckBox", "Common", """<CheckBox Content="Option" />"""),
        new("Radio Button", "RadioButton", "Common", """<RadioButton Content="Choice" />"""),
        new("Combo Box", "ComboBox", "Common", """<ComboBox Width="150" />"""),
        new("Toggle Switch", "ToggleSwitch", "Common", "<ToggleSwitch />"),
        new("Slider", "Slider", "Common", """<Slider Width="150" />"""),
        new("Progress Bar", "ProgressBar", "Common", """<ProgressBar Width="150" Value="50" />"""),
        new("Image", "Image", "Common", """<Image Width="100" Height="100" />"""),
        new("Calendar", "Calendar", "Common", "<Calendar />"),
        new("Date Picker", "DatePicker", "Common", "<DatePicker />"),

        // Lists
        new("List Box", "ListBox", "Lists", """<ListBox Width="150" Height="100" />"""),
        new("List View", "ListView", "Lists", """<ListView Width="150" Height="100" />"""),
        new("Tree View", "TreeView", "Lists", """<TreeView Width="150" Height="100" />"""),
        new("Data Grid", "DataGrid", "Lists", """<DataGrid Width="200" Height="120" />"""),
        new("Tab Control", "TabControl", "Lists", "<TabControl />"),

        // Menu
        new("Menu", "Menu", "Menu", "<Menu />"),
        new("Menu Item", "MenuItem", "Menu", """<MenuItem Header="Item" />"""),
        new("Status Bar", "StackPanel", "Menu", """<StackPanel Orientation="Horizontal" />""")
    ];

    public static IEnumerable<IGrouping<string, ToolboxItem>> ByCategory() =>
        All.GroupBy(i => i.Category);
}
