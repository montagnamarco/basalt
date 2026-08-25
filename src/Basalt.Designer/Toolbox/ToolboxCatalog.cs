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
    public static IReadOnlyList<ToolboxItem> Items { get; } =
    [
        // Layout containers
        new("Griglia", "Grid", "Layout", "<Grid />"),
        new("Pannello impilato", "StackPanel", "Layout", "<StackPanel />"),
        new("Pannello con bordi", "DockPanel", "Layout", "<DockPanel />"),
        new("Pannello a fluire", "WrapPanel", "Layout", "<WrapPanel />"),
        new("Pannello a coordinate", "Canvas", "Layout", "<Canvas />"),
        new("Bordo", "Border", "Layout", """<Border BorderBrush="Gray" BorderThickness="1" Padding="8" />"""),
        new("Area scorrevole", "ScrollViewer", "Layout", "<ScrollViewer />"),
        new("Separatore", "Separator", "Layout", "<Separator />"),
        new("Espansore", "Expander", "Layout", """<Expander Header="Espansore" />"""),

        // Common controls
        new("Pulsante", "Button", "Comuni", """<Button Content="Pulsante" />"""),
        new("Etichetta", "TextBlock", "Comuni", """<TextBlock Text="Etichetta" />"""),
        new("Casella di testo", "TextBox", "Comuni", """<TextBox Width="150" />"""),
        new("Casella di controllo", "CheckBox", "Comuni", """<CheckBox Content="Opzione" />"""),
        new("Pulsante di opzione", "RadioButton", "Comuni", """<RadioButton Content="Scelta" />"""),
        new("Casella combinata", "ComboBox", "Comuni", """<ComboBox Width="150" />"""),
        new("Interruttore", "ToggleSwitch", "Comuni", "<ToggleSwitch />"),
        new("Dispositivo di scorrimento", "Slider", "Comuni", """<Slider Width="150" />"""),
        new("Barra di avanzamento", "ProgressBar", "Comuni", """<ProgressBar Width="150" Value="50" />"""),
        new("Immagine", "Image", "Comuni", """<Image Width="100" Height="100" />"""),
        new("Calendario", "Calendar", "Comuni", "<Calendar />"),
        new("Selettore data", "DatePicker", "Comuni", "<DatePicker />"),

        // Lists
        new("Casella di riepilogo", "ListBox", "Elenchi", """<ListBox Width="150" Height="100" />"""),
        new("Vista list", "ListView", "Elenchi", """<ListView Width="150" Height="100" />"""),
        new("Vista albero", "TreeView", "Elenchi", """<TreeView Width="150" Height="100" />"""),
        new("Griglia dati", "DataGrid", "Elenchi", """<DataGrid Width="200" Height="120" />"""),
        new("Controllo a schede", "TabControl", "Elenchi", "<TabControl />"),

        // Menu
        new("Menu", "Menu", "Menu", "<Menu />"),
        new("Voce di menu", "MenuItem", "Menu", """<MenuItem Header="Voce" />"""),
        new("Barra di stato", "StackPanel", "Menu", """<StackPanel Orientation="Horizontal" />""")
    ];

    public static IEnumerable<IGrouping<string, ToolboxItem>> ByCategory() =>
        Items.GroupBy(i => i.Category);
}
