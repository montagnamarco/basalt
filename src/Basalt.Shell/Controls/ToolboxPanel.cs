using Avalonia;
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
        _list = new ListBox { ItemsSource = ToolboxCatalog.Items };

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
}
