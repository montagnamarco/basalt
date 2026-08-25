using Avalonia.Controls;
using Avalonia.Input;

namespace Basalt.Shell.Controls;

/// <summary>One entry of a context menu.</summary>
public sealed record MenuAction(string Header, Action Invoke)
{
    /// <summary>Whether the entry can be used right now; null means always.</summary>
    public Func<bool>? IsAvailable { get; init; }

    /// <summary>The shortcut shown beside it, where there is one.</summary>
    public string? Gesture { get; init; }

    /// <summary>An icon shown beside it, where one helps.</summary>
    public IconKind Icon { get; init; } = IconKind.None;

    /// <summary>A separator, which has no action of its own.</summary>
    public static MenuAction Separator { get; } = new("-", () => { });

    public bool IsSeparator => Header == "-";
}

/// <summary>
/// Builds the context menus the panels share.
///
/// Written once because every panel wants the same behaviour: entries that
/// grey out rather than disappear, shortcuts shown beside them, and the
/// availability decided when the menu opens rather than when it was built.
/// </summary>
public static class ContextMenus
{
    /// <summary>Builds a menu from a list of entries.</summary>
    public static ContextMenu Build(IReadOnlyList<MenuAction> actions)
    {
        var items = new List<object>();
        var live = new List<(MenuAction Action, MenuItem Item)>();

        foreach (var action in actions)
        {
            if (action.IsSeparator)
            {
                items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = action.Header,
                InputGesture = action.Gesture is null ? null : KeyGesture.Parse(action.Gesture)
            };

            if (action.Icon != IconKind.None)
                item.Icon = new IconView { Kind = action.Icon, IconSize = 14 };

            item.Click += (_, _) => action.Invoke();

            items.Add(item);
            live.Add((action, item));
        }

        var menu = new ContextMenu { ItemsSource = items };

        void Refresh()
        {
            foreach (var (action, item) in live)
                item.IsEnabled = action.IsAvailable?.Invoke() ?? true;
        }

        // Availability is asked for when the menu opens: what a panel can do
        // depends on what is selected, which changes between openings.
        menu.Opening += (_, _) => Refresh();

        _refreshers.Add(menu, Refresh);

        return menu;
    }

    /// <summary>
    /// Which entries are set up to decide their own availability.
    ///
    /// Kept so a test can ask what the menu would show without opening it:
    /// ContextMenu.Open needs a control it is attached to and a window on
    /// screen, neither of which a test of the entries should require. Weak
    /// keys, so a menu that goes away is not held alive by this.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        ContextMenu, Action> _refreshers = [];

    /// <summary>Reports what the entries would look like if the menu opened now.</summary>
    internal static IReadOnlyList<(string Header, bool Enabled)> OpenForTests(ContextMenu menu)
    {
        if (_refreshers.TryGetValue(menu, out var refresh)) refresh();

        return [.. menu.ItemsSource!
            .OfType<MenuItem>()
            .Select(i => (i.Header?.ToString() ?? "", i.IsEnabled))];
    }

    /// <summary>
    /// The entries that have no icon, for tests.
    ///
    /// By name rather than by count, so a failure says which one is bare
    /// instead of only that one is.
    /// </summary>
    internal static IReadOnlyList<string> WithoutIconsForTests(ContextMenu menu) =>
    [
        // Either source: a menu built from a list uses ItemsSource, one built
        // item by item uses Items, and both turn up in this window.
        .. (menu.ItemsSource ?? menu.Items)
            .OfType<MenuItem>()
            // A separator is a MenuItem with no header, and has no icon by
            // definition; only named entries are expected to carry one.
            .Where(i => i.Icon is null && i.Header is not null)
            .Select(i => i.Header!.ToString() ?? "?")
    ];

    /// <summary>Invokes a named entry, for tests.</summary>
    internal static void ChooseForTests(ContextMenu menu, string header) =>
        menu.ItemsSource!
            .OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == header)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
}
