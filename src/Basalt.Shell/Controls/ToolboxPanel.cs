using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;

namespace Basalt.Shell.Controls;

/// <summary>List of controls that can be dropped into the designer.</summary>
/// <remarks>
/// Dragging is the gesture people arrive expecting, and it says where the
/// control lands. Double-clicking still works and is faster once you know it,
/// so the hint at the top says both — a toolbox that only answers to a
/// double-click looks like a toolbox that does nothing.
/// </remarks>
public sealed class ToolboxPanel : UserControl
{
    /// <summary>
    /// The format a dragged toolbox entry travels under.
    /// </summary>
    /// <remarks>
    /// In-process: the entry is a live object handed to the surface in the
    /// same application, not bytes for another program to read.
    /// </remarks>
    public static readonly DataFormat<ToolboxItem> DragFormat =
        DataFormat.CreateInProcessFormat<ToolboxItem>("basalt.toolbox-item");

    private readonly ListBox _list;

    public ToolboxPanel()
    {
        // The entries with their category headings mixed in, rather than a
        // flat run of thirty names: thirty in one column is a list you spell
        // your way down, and the same thirty under Layout, Common, Lists and
        // Menus is four short ones with a word saying which to look in.
        //
        // Headings are rows in the same list because Avalonia has no grouped
        // list — a second control per category would scroll independently,
        // and a toolbox that scrolls in five places is worse than one long
        // list.
        _list = new ListBox
        {
            ItemsSource = WithHeadings(),
            ItemTemplate = new FuncDataTemplate<object>(
                (item, _) => item is string ? Heading() : Row(),
                supportsRecycling: false),
        };

        // A heading is not something you can insert, so it does not take the
        // selection: arrowing down the list steps over them.
        _list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem row && row.DataContext is string)
            {
                row.IsHitTestVisible = false;
                row.Focusable = false;
            }
        };

        _list.DoubleTapped += (_, _) =>
        {
            if (_list.SelectedItem is ToolboxItem item) ControlChosen?.Invoke(this, item);
        };

        _list.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        _list.PointerMoved += OnMoved;
        _list.PointerReleased += (_, _) => _pending = null;
        _list.PointerCaptureLost += (_, _) => _pending = null;

        var hint = new TextBlock
        {
            Text = Localizer.Get(StringKeys.ToolboxHint),
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { hint, _list },
        };

        Grid.SetRow(_list, 1);

        Content = layout;
    }

    public event EventHandler<ToolboxItem>? ControlChosen;

    /// <summary>
    /// The press that may become a drag, until the pointer moves or lets go.
    /// </summary>
    /// <remarks>
    /// Held rather than acted on: starting the drag from the press itself
    /// began one on every single click, and on macOS a drag begun before the
    /// pointer has moved crashes the application in AppKit — "There are 0
    /// items on the pasteboard, but 1 drag images". A click has to stay a
    /// click.
    /// </remarks>
    private (PointerPressedEventArgs Event, ToolboxItem Item, Point At)? _pending;

    /// <summary>How far the pointer must travel before a click becomes a drag.</summary>
    private const double DragThreshold = 4;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        // The item under the pointer, not the selected one: dragging an entry
        // you have not clicked first is the ordinary way to use a toolbox.
        if ((e.Source as Control)?.DataContext is not ToolboxItem item)
        {
            _pending = null;
            return;
        }

        _pending = (e, item, e.GetPosition(this));
    }

    private async void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pending is not { } pending) return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pending = null;
            return;
        }

        var travelled = e.GetPosition(this) - pending.At;

        if (Math.Abs(travelled.X) < DragThreshold && Math.Abs(travelled.Y) < DragThreshold)
            return;

        // Cleared before the drag rather than after: the call does not return
        // until the drag ends, and a second move arriving meanwhile would
        // start another one on top of it.
        _pending = null;

        // One item carrying both formats, not two items.
        //
        // macOS makes a drag image per item but writes a pasteboard entry
        // only for items with a format it can serialise, and an in-process
        // format is not one. Two items therefore gave two images and one
        // pasteboard entry, which AppKit answers by terminating the process:
        // "There are 1 items on the pasteboard, but 2 drag images". A single
        // item is one image and one entry however many formats it holds.
        var item = new DataTransferItem();
        item.Set(DragFormat, pending.Item);
        item.SetText(pending.Item.DefaultXaml);

        var transfer = new DataTransfer();
        transfer.Add(item);

        try
        {
            await DragDrop.DoDragDropAsync(pending.Event, transfer, DragDropEffects.Copy);
        }
        catch (Exception)
        {
            // A drag the platform refuses is not worth losing the
            // application over: the toolbox still answers to a double-click,
            // and the user is left with a gesture that did nothing rather
            // than with a window that vanished.
            //
            // This catches managed failures only. On macOS the refusal
            // arrives as an Objective-C exception from AppKit, which no catch
            // here can stop — see DragOnMacOs below.
        }
    }

    /// <summary>One entry: its picture, then its name.</summary>
    /// <remarks>
    /// The picture is what makes the row findable. Reading is slow and a
    /// toolbox is used dozens of times an hour, so the shape of a button or a
    /// tree carries further across the panel than the word for it — which is
    /// also why the icon is the control seen from a distance rather than an
    /// abstract mark.
    ///
    /// The tooltip names the element as it appears in the markup: someone who
    /// knows Avalonia is looking for ToggleSwitch, not for whatever the
    /// interface's language calls it.
    /// </remarks>
    private static Control Row()
    {
        var icon = new ToolboxIconView { IconSize = 16, Width = 16, Height = 16 };

        icon.Bind(
            ToolboxIconView.ElementNameProperty,
            new Binding(nameof(ToolboxItem.ElementName)));

        var name = new TextBlock
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        name.Bind(TextBlock.TextProperty, new Binding(nameof(ToolboxItem.DisplayName)));

        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = Spacing.Normal,
            Children = { icon, name },
        };

        row.Bind(
            ToolTip.TipProperty,
            new Binding(nameof(ToolboxItem.ElementName)));

        return row;
    }

    /// <summary>A category heading between the groups.</summary>
    private static Control Heading()
    {
        var heading = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.6,
            Margin = new Thickness(0, Spacing.Tight, 0, 0),
        };

        heading.Bind(TextBlock.TextProperty, new Binding("."));

        return heading;
    }

    /// <summary>The catalogue with a heading before each category.</summary>
    /// <remarks>
    /// In catalogue order rather than sorted: the order there puts the
    /// controls people reach for first at the top of each group, which
    /// alphabetical order would scatter.
    /// </remarks>
    private static List<object> WithHeadings()
    {
        var rows = new List<object>();

        foreach (var group in ToolboxCatalog.ByCategory())
        {
            rows.Add(group.Key);
            rows.AddRange(group);
        }

        return rows;
    }
}
