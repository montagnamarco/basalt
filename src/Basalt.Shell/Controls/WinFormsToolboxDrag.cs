using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Basalt.Designer.Toolbox;

namespace Basalt.Shell.Controls;

/// <summary>Starts an in-process Forms toolbox drag only after a real pointer movement.</summary>
internal static class WinFormsToolboxDrag
{
    internal static readonly DataFormat<WinFormsToolboxItem> Format =
        DataFormat.CreateInProcessFormat<WinFormsToolboxItem>("basalt.winforms-toolbox-item");

    internal static void Attach(ListBox toolbox,
        Func<PointerPressedEventArgs, IDataTransfer, DragDropEffects, Task<DragDropEffects>>? startDrag = null)
    {
        startDrag ??= DragDrop.DoDragDropAsync;
        (PointerPressedEventArgs Event, WinFormsToolboxItem Item, Point At)? pending = null;

        toolbox.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        {
            pending = (args.Source as Control)?.DataContext is WinFormsToolboxItem item
                && args.GetCurrentPoint(toolbox).Properties.IsLeftButtonPressed
                ? (args, item, args.GetPosition(toolbox)) : null;
        }, RoutingStrategies.Tunnel);

        toolbox.PointerMoved += async (_, args) =>
        {
            if (pending is not { } press) return;
            if (!args.GetCurrentPoint(toolbox).Properties.IsLeftButtonPressed)
            {
                pending = null;
                return;
            }

            var distance = args.GetPosition(toolbox) - press.At;
            if (Math.Abs(distance.X) < 4 && Math.Abs(distance.Y) < 4) return;
            pending = null;

            // AppKit requires one pasteboard item per drag image. Keep the
            // live item and its serializable text in the same transfer item.
            var transferItem = new DataTransferItem();
            transferItem.Set(Format, press.Item);
            transferItem.SetText(press.Item.DisplayName);
            var transfer = new DataTransfer();
            transfer.Add(transferItem);
            try { await startDrag(press.Event, transfer, DragDropEffects.Copy); }
            catch (Exception ex)
            {
                // Avalonia's native drag sources (Win32, X11, AppKit) do not agree on a
                // shared exception type for a refused drag: the Win32 and AppKit COM-style
                // interop thunks raise System.Runtime.InteropServices.COMException, while
                // their own async wrappers (and the X11 backend) fall back to a bare
                // System.Exception. Catching Exception is therefore unavoidable to keep the
                // documented refusal behaviour (double-click and Enter still start the
                // toolbox action) on every platform. Report it instead of swallowing it so an
                // unrelated bug does not vanish silently.
                Logger.TryGet(LogEventLevel.Warning, "Basalt.WinFormsToolboxDrag")?
                    .Log(toolbox, "Toolbox drag start was refused or failed: {Exception}", ex);
            }
        };

        toolbox.PointerReleased += (_, _) => pending = null;
        toolbox.PointerCaptureLost += (_, _) => pending = null;
    }
}
