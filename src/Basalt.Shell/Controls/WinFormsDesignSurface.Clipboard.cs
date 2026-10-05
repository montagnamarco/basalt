using Avalonia.Input;
using Avalonia.Interactivity;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsDesignSurface
{
    public event Action? CopyRequested;
    public event Action? CutRequested;
    public event Action? PasteRequested;
    public Func<bool>? CanPasteControls { get; set; }

    private void InitializeClipboardKeyboard()
    {
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Handled || TabOrderMode
                || args.KeyModifiers is not (KeyModifiers.Control or KeyModifiers.Meta)) return;
            if (args.Key == Key.C && _selection.Any(control => !ReferenceEquals(control, _rootForm)))
                CopyRequested?.Invoke();
            else if (args.Key == Key.X && _selection.Any(control => !ReferenceEquals(control, _rootForm)))
                CutRequested?.Invoke();
            else if (args.Key == Key.V && CanPasteControls?.Invoke() == true)
                PasteRequested?.Invoke();
            else
                return;
            args.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
}
