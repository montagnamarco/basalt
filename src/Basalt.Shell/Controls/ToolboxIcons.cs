using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>
/// A small picture of each control the toolbox offers.
///
/// Drawn as paths in the same 16 by 16 box the rest of the interface uses, so
/// one scale factor governs them all and they take the theme's foreground
/// colour without a light and a dark copy of every file.
///
/// Each shape is the control seen from a distance: a button is a rounded
/// rectangle with a line of text in it, a check box has its tick, a tree has
/// branches. Thirty entries reading as thirty names in a column is a list you
/// have to spell your way down; the same list with a picture beside each name
/// is one you find the button in without reading anything.
/// </summary>
public static class ToolboxIcons
{
    /// <summary>
    /// The outline for a control, or null where there is nothing to draw.
    ///
    /// Keyed on the element name rather than the display name: the names in
    /// the toolbox are translated, and an icon that disappears when the
    /// interface is switched to another language is worse than no icon.
    /// </summary>
    public static Geometry? For(string elementName) =>
        PathDataFor(elementName) is { } data ? Geometry.Parse(data) : null;

    /// <summary>
    /// The outline as path data, before it becomes geometry.
    ///
    /// Parsing a path needs a render backend started, which a plain unit test
    /// has no reason to do; this answers whether a control has a shape at all
    /// without one.
    /// </summary>
    public static string? PathDataFor(string elementName) => elementName switch
    {
        // Layout: containers are drawn as the divisions they make. A grid is
        // its lines, a stack is its rows, a dock is a frame with a filled
        // edge — which is the one thing that distinguishes it from a border.
        "Grid" => "M1,2 H15 V14 H1 Z M1,8 H15 M6.5,2 V14 M11,2 V14",
        "StackPanel" => "M1,2 H15 V14 H1 Z M1,6 H15 M1,10 H15",
        "DockPanel" => "M1,2 H15 V14 H1 Z M1,2 H5 V14 M5,5 H15",
        "WrapPanel" => "M1,3 H6 V7 H1 Z M8,3 H13 V7 H8 Z M1,9 H6 V13 H1 Z",
        "Canvas" => "M1,2 H15 V14 H1 Z M4,5 H8 V9 H4 Z M10,8 H13.5 V12 H10 Z",
        "Border" => "M2,3 H14 V13 H2 Z",
        "ScrollViewer" => "M1,2 H12 V14 H1 Z M14,3 V13 M14,3 L12.5,4.5 M14,3 L15.5,4.5 "
                        + "M14,13 L12.5,11.5 M14,13 L15.5,11.5",
        "Separator" => "M1,8 H15",
        "Expander" => "M1,2 H15 V14 H1 Z M1,6 H15 M3.5,3 L4.75,4.5 L6,3",

        // Common controls. The button carries a line of text: an empty
        // rounded rectangle is a border, and the two are next to each other
        // in the list.
        "Button" => "M1.5,4.5 H14.5 A1.5,1.5 0 0 1 14.5,11.5 H1.5 "
                  + "A1.5,1.5 0 0 1 1.5,4.5 Z M5,8 H11",
        "TextBlock" => "M2,4 H14 M4.5,4 V12.5 M2,12.5 H7",
        "TextBox" => "M1,4 H15 V12 H1 Z M4,6.5 V9.5",
        "CheckBox" => "M1,3 H9 V11 H1 Z M2.8,7 L4.4,8.8 L7.4,5.2 M11,7 H15",
        "RadioButton" => "M1,7 A4,4 0 1 0 9,7 A4,4 0 1 0 1,7 Z "
                       + "M3.6,7 A1.4,1.4 0 1 0 6.4,7 A1.4,1.4 0 1 0 3.6,7 Z M11,7 H15",
        "ComboBox" => "M1,4 H15 V12 H1 Z M11.5,4 V12 M12.3,7.3 L13.25,8.6 L14.2,7.3",
        "ToggleSwitch" => "M4.5,4 H11.5 A3.5,3.5 0 0 1 11.5,11 H4.5 "
                        + "A3.5,3.5 0 0 1 4.5,4 Z "
                        + "M9.4,7.5 A2.1,2.1 0 1 0 13.6,7.5 A2.1,2.1 0 1 0 9.4,7.5 Z",
        "Slider" => "M1,8 H15 M6.5,4.5 H9.5 V11.5 H6.5 Z",
        "ProgressBar" => "M1,5.5 H15 V10.5 H1 Z M1,5.5 H8 V10.5 H1 Z",
        "Image" => "M1,3 H15 V13 H1 Z M1,11 L5.5,6.5 L9,10 L11.5,8 L15,11 "
                 + "M10.3,5.5 A1.2,1.2 0 1 0 12.7,5.5 A1.2,1.2 0 1 0 10.3,5.5 Z",
        "Calendar" => "M1,3 H15 V14 H1 Z M1,6.5 H15 M4.5,1.5 V4.5 M11.5,1.5 V4.5 "
                    + "M4.5,9 H6 M7.5,9 H9 M10.5,9 H12 M4.5,11.5 H6 M7.5,11.5 H9",
        "DatePicker" => "M1,4 H15 V12 H1 Z M11,4 V12 M2.5,6.5 H5.5 M2.5,9 H8",

        // Lists. What separates them is what they hold, so each shows its own
        // arrangement of rows: plain, with a selection, indented, in columns.
        "ListBox" => "M1,2 H15 V14 H1 Z M3,5 H13 M3,8 H13 M3,11 H13",
        "ListView" => "M1,2 H15 V14 H1 Z M1,7 H15 V10 H1 Z M3,4.5 H13 M3,12.5 H13",
        "TreeView" => "M2,3 H6 M4,3 V13 M4,7 H8 M4,11 H8 M9,5 H14 M9,9 H14 M9,13 H14",
        "DataGrid" => "M1,2 H15 V14 H1 Z M1,5.5 H15 M1,9.75 H15 M6.5,2 V14 M11,2 V14",
        "TabControl" => "M1,4.5 H6 V2 H11 V4.5 H15 V14 H1 Z",

        // Menus.
        "Menu" => "M1,2 H15 V6 H1 Z M3,4 H5 M7,4 H9 M11,4 H13",
        "MenuItem" => "M1,3 H15 V13 H1 Z M3,8 H10 M12,6.8 L13.2,8 L12,9.2",

        _ => null
    };
}

/// <summary>
/// Draws the picture of one toolbox control.
///
/// A control rather than an image, for the same reason <see cref="IconView"/>
/// is one: the shape is stroked in the current foreground colour, so the same
/// icon reads on a light panel and a dark one without a second set of files.
/// </summary>
public sealed class ToolboxIconView : Avalonia.Controls.Control
{
    /// <summary>The box every path is drawn in, before scaling.</summary>
    private const double DesignSize = 16;

    public static readonly Avalonia.StyledProperty<string?> ElementNameProperty =
        Avalonia.AvaloniaProperty.Register<ToolboxIconView, string?>(nameof(ElementName));

    public static readonly Avalonia.StyledProperty<double> IconSizeProperty =
        Avalonia.AvaloniaProperty.Register<ToolboxIconView, double>(nameof(IconSize), 16);

    static ToolboxIconView()
    {
        AffectsRender<ToolboxIconView>(ElementNameProperty, IconSizeProperty);
        AffectsMeasure<ToolboxIconView>(IconSizeProperty);
    }

    /// <summary>Which control to depict, by its element name.</summary>
    public string? ElementName
    {
        get => GetValue(ElementNameProperty);
        set => SetValue(ElementNameProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize) =>
        new(IconSize, IconSize);

    public override void Render(DrawingContext context)
    {
        if (ElementName is not { } name) return;
        if (ToolboxIcons.For(name) is not { } geometry) return;

        // Slightly dimmed. The name is what is being read; the icon is what
        // makes the row findable, and at full strength thirty of them in a
        // column shout over the words they are meant to help with.
        var brush = new SolidColorBrush(
            (Foreground as ISolidColorBrush)?.Color ?? Colors.Gray, 0.85);

        var scale = IconSize / DesignSize;

        using var _ = context.PushTransform(
            Avalonia.Matrix.CreateScale(scale, scale));

        context.DrawGeometry(null, new Pen(brush, 1.1), geometry);
    }

    /// <summary>The colour to stroke in; inherited from the list.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly Avalonia.StyledProperty<IBrush?> ForegroundProperty =
        Avalonia.Controls.TextBlock.ForegroundProperty
            .AddOwner<ToolboxIconView>();
}
