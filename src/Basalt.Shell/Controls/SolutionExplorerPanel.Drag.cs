using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>
/// Drag a file or folder onto a folder of the tree to move it there, as
/// Visual Studio's Solution Explorer does.
///
/// A press on a row only remembers the press. A drag starts when the pointer
/// has moved past <see cref="DragThreshold"/> with the button still down, so a
/// click selects and a double-click opens without starting one. The drag
/// carries the row's path; a drop on a row raises the
/// <see cref="ExplorerCommand.DropOn"/> command the menu uses, with the dragged
/// node in <see cref="DraggedNode"/>, which the command reads before it awaits
/// anything. The move then goes through the same path as Cut and Paste.
/// </summary>
public sealed partial class SolutionExplorerPanel
{
    /// <summary>The data format a dragged entry is carried in: its path.</summary>
    internal static readonly DataFormat<string> DragFormat =
        DataFormat.CreateStringApplicationFormat("com.basalt.solution-explorer-path");

    /// <summary>
    /// How far, in device-independent pixels, the pointer must travel with the
    /// button down before a press becomes a drag. Avalonia exposes no platform
    /// value for it here, so this is the usual 4 px.
    /// </summary>
    internal const double DragThreshold = 4;

    /// <summary>
    /// Starts the platform's drag session. A seam for the tests, which observe
    /// the start without a real drag loop running under the headless platform.
    /// </summary>
    internal Func<PointerPressedEventArgs, DataTransfer, Task> StartDragAsync { get; set; } =
        (press, transfer) => DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move);

    /// <summary>
    /// The node a drop is for, while its command starts. Set only for the
    /// synchronous start of the <see cref="ExplorerCommand.DropOn"/> command.
    /// </summary>
    internal SolutionTreeNode? DraggedNode { get; private set; }

    /// <summary>The node the current drag started from, while one is in progress.</summary>
    private SolutionTreeNode? _dragSource;

    /// <summary>The press that may still become a drag, and where it was made.</summary>
    private PointerPressedEventArgs? _press;
    private SolutionTreeNode? _pressedNode;
    private Point _pressedAt;

    private void ConnectDragAndDrop()
    {
        DragDrop.SetAllowDrop(_tree, true);

        _tree.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            _pressedNode = e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed ? NodeUnder(e) : null;
            _press = _pressedNode is null ? null : e;
            _pressedAt = e.GetPosition(_tree);
        }, RoutingStrategies.Bubble, true);

        _tree.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (_press is not { } press || _pressedNode is not { } node) return;

            if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
            {
                ForgetPress();
                return;
            }

            var now = e.GetPosition(_tree);
            var dx = now.X - _pressedAt.X;
            var dy = now.Y - _pressedAt.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold) return;

            ForgetPress();

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(DragFormat, node.Path));

            _dragSource = node;
            try
            {
                await StartDragAsync(press, transfer);
            }
            finally
            {
                _dragSource = null;
            }
        }, RoutingStrategies.Bubble, true);

        _tree.AddHandler(InputElement.PointerReleasedEvent, (_, _) => ForgetPress(), RoutingStrategies.Bubble, true);

        _tree.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = _dragSource is not null && NodeAt(e.GetPosition(_tree)) is not null
                ? DragDropEffects.Move
                : DragDropEffects.None;
        });

        _tree.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_dragSource is not { } dragged) return;
            if (NodeAt(e.GetPosition(_tree)) is not { } target) return;

            DropOn(dragged, target);
            e.Handled = true;
        });
    }

    /// <summary>Drops the press that was waiting to become a drag, if one was.</summary>
    private void ForgetPress()
    {
        _press = null;
        _pressedNode = null;
    }

    /// <summary>
    /// Raises the drop as the <see cref="ExplorerCommand.DropOn"/> command,
    /// with the dragged node available to the command while it starts.
    /// </summary>
    internal void DropOn(SolutionTreeNode dragged, SolutionTreeNode target)
    {
        DraggedNode = dragged;
        try
        {
            CommandChosen?.Invoke(this, (ExplorerCommand.DropOn, target));
        }
        finally
        {
            DraggedNode = null;
        }
    }

    /// <summary>The tree row under a pointer event, as its node.</summary>
    private SolutionTreeNode? NodeUnder(PointerEventArgs e) => NodeAt(e.GetPosition(_tree));

    private SolutionTreeNode? NodeAt(Point point)
    {
        var control = _tree.InputHitTest(point) as Visual;
        while (control is not null && control is not TreeViewItem)
            control = control.GetVisualParent();

        return (control as TreeViewItem)?.DataContext as SolutionTreeNode;
    }
}
