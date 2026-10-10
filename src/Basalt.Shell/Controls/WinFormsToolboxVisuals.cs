using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Basalt.Designer.Toolbox;

namespace Basalt.Shell.Controls;

/// <summary>Uses the IDE's vector toolbox artwork for corresponding Forms controls.</summary>
internal static class WinFormsToolboxVisuals
{
    internal static Control CreateRow(WinFormsToolboxItem item)
    {
        Control icon = ToolboxBitmap(item.ControlType) is { } bitmap
            ? new Image { Source = bitmap, Width = 16, Height = 16 }
            : new ToolboxIconView
            {
                ElementName = IconName(item.ControlType),
                IconSize = 16,
                Width = 16,
                Height = 16,
            };
        var name = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Small,
            Children = { icon, name },
            // Pointer has no real type for a user to care about; its own name
            // is the whole story, the same one the row already shows.
            [ToolTip.TipProperty] = WinFormsToolboxCatalog.IsPointer(item.ControlType)
                ? item.DisplayName
                : item.ControlType.FullName ?? item.DisplayName,
        };
    }

    /// <summary>
    /// The image a project's own control or component names with [ToolboxBitmap], at toolbox size, or
    /// null when it names none. The built-in Forms entries keep the IDE's vector artwork, so they never
    /// take their image from an attribute here.
    /// </summary>
    internal static Bitmap? ToolboxBitmap(Type type)
    {
        if (WinFormsToolboxCatalog.IsPointer(type) || WinFormsDesignTypes.IsFormsType(type)
            || type.GetCustomAttribute<System.Drawing.ToolboxBitmapAttribute>() is not { } attribute) return null;
        try
        {
            using var image = attribute.GetImage(type);
            using var png = new MemoryStream();
            image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            png.Position = 0;

            // Avalonia decodes the stream when the bitmap is created, so the stream is not kept.
            return new Bitmap(png);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or NotSupportedException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            // A picture that does not decode leaves the row with the default icon instead of no row.
            return null;
        }
    }

    internal static string IconName(Type type) => WinFormsToolboxCatalog.IsPointer(type) ? "Pointer" : type.Name switch
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
