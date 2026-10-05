namespace Basalt.Shell.ViewModels;

public partial class MainWindowViewModel
{
    // The shell binds these to the visible non-XAML designer. Clearing them
    // on view changes keeps menu and toolbar commands on the active document.
    internal Action? DesignerUndoRequested { get; set; }
    internal Action? DesignerRedoRequested { get; set; }
}
