using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Basalt.Designer.Toolbox;

namespace Basalt.Shell.Controls;

/// <summary>Uses the IDE's vector toolbox artwork for corresponding Forms controls.</summary>
internal static class WinFormsToolboxVisuals
{
    internal static readonly FuncDataTemplate<WinFormsToolboxItem> Template =
        new((item, _) => CreateRow(item), supportsRecycling: false);

    private static Control CreateRow(WinFormsToolboxItem? item)
    {
        if (item is null) return new TextBlock();

        var icon = new ToolboxIconView
        {
            ElementName = IconName(item.ControlType),
            IconSize = 16,
            Width = 16,
            Height = 16,
        };
        var name = new TextBlock
        {
            Text = item.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Small,
            Children = { icon, name },
            [ToolTip.TipProperty] = item.ControlType.FullName ?? item.DisplayName,
        };
    }

    internal static string IconName(Type type) => type.Name switch
    {
        "Label" or "LinkLabel" => "TextBlock",
        "Panel" or "GroupBox" or "FlowLayoutPanel" or "TableLayoutPanel" => "Border",
        "PictureBox" => "Image",
        "DataGridView" => "DataGrid",
        "TrackBar" or "VScrollBar" or "HScrollBar" => "Slider",
        "ProgressBar" => "ProgressBar",
        "DateTimePicker" => "DatePicker",
        "MonthCalendar" => "Calendar",
        "MenuStrip" or "ContextMenuStrip" or "ToolStrip" or "StatusStrip" => "Menu",
        "TabPage" => "TabControl",
        "NumericUpDown" or "MaskedTextBox" or "RichTextBox" => "TextBox",
        "CheckedListBox" => "CheckBox",
        "Timer" => "ProgressBar",
        "ImageList" => "Image",
        "ToolTip" => "MenuItem",
        var name when ToolboxIcons.PathDataFor(name) is not null => name,
        _ => "Border",
    };
}
