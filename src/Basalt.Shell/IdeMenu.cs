using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;

using Basalt.Shell.Controls;

namespace Basalt.Shell;

/// <summary>An entry in the IDE menu, independent of how it will be displayed.</summary>
public sealed record MenuEntry(
    string Header,
    ICommand? Command = null,
    Action? Action = null,
    KeyGesture? Gesture = null,
    IReadOnlyList<MenuEntry>? Children = null)
{
    public static MenuEntry Separator { get; } = new("-");

    public bool IsSeparator => Header == "-";

    /// <summary>
    /// The icon shown beside the item, where one helps.
    ///
    /// Shown on both menus. It used to be managed-only, on the belief that
    /// the macOS menu bar ignores icons — it does not: NativeMenuItem has an
    /// Icon of its own, it simply wants a bitmap rather than a control.
    /// </summary>
    public IconKind Icon { get; init; } = IconKind.None;

    /// <summary>
    /// Whether the entry can be chosen.
    ///
    /// A greyed entry says both that something exists and why it is not
    /// available; hiding it leaves the reader wondering where it went.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// The longer text shown on hovering, where the label alone is ambiguous.
    ///
    /// Two solutions can have the same file name and differ only by folder.
    /// </summary>
    public string? ToolTip { get; init; }
}

/// <summary>
/// Describes the menu once and exposes it in the form suited to the
/// platform: on macOS the system menu bar at the top of the screen, elsewhere a
/// menu inside the window.
/// </summary>
public static class IdeMenu
{
    /// <summary>On macOS the menu belongs to the application, not to the window.</summary>
    public static bool UsesSystemMenuBar => OperatingSystem.IsMacOS();

    public static NativeMenu BuildNative(IReadOnlyList<MenuEntry> entries)
    {
        var menu = new NativeMenu();
        foreach (var item in entries.Select(ToNativeItem)) menu.Add(item);
        return menu;
    }

    public static IReadOnlyList<MenuItem> BuildManaged(IReadOnlyList<MenuEntry> entries) =>
        entries.Select(ToManagedItem).ToList();

    private static NativeMenuItemBase ToNativeItem(MenuEntry entry)
    {
        if (entry.IsSeparator) return new NativeMenuItemSeparator();

        var item = new NativeMenuItem(entry.Header)
        {
            Gesture = entry.Gesture,
            IsEnabled = entry.IsEnabled,
            ToolTip = entry.ToolTip
        };

        // The system menu bar wants pixels: the same paths the managed menu
        // strokes, rendered once.
        if (entry.Icon != IconKind.None)
            item.Icon = IconView.Rasterize(entry.Icon);

        if (entry.Children is { Count: > 0 })
        {
            item.Menu = BuildNative(entry.Children);
            return item;
        }

        if (entry.Command is not null) item.Command = entry.Command;
        else if (entry.Action is not null) item.Click += (_, _) => entry.Action();

        return item;
    }

    private static MenuItem ToManagedItem(MenuEntry entry)
    {
        if (entry.IsSeparator) return new MenuItem { Header = "-" };

        var item = new MenuItem
        {
            Header = entry.Header,
            InputGesture = entry.Gesture,
            IsEnabled = entry.IsEnabled
        };

        if (entry.ToolTip is { Length: > 0 } tip) ToolTip.SetTip(item, tip);

        if (entry.Icon != IconKind.None)
            item.Icon = new IconView { Kind = entry.Icon, IconSize = 14 };

        if (entry.Children is { Count: > 0 })
        {
            item.ItemsSource = BuildManaged(entry.Children);
            return item;
        }

        if (entry.Command is not null) item.Command = entry.Command;
        else if (entry.Action is not null) item.Click += (_, _) => entry.Action();

        return item;
    }
}
