using System.Collections;

namespace System.Windows.Forms.Design;

/// <summary>What a design host offers a component editor or type editor so it can parent a dialog, report an error, or ask the designer to show its own component editor - without needing a reference to the host's actual Form.</summary>
public interface IUIService
{
    IDictionary Styles { get; }

    bool CanShowComponentEditor(object? @object);

    IWin32Window? GetDialogOwnerWindow();

    void SetUIDirty();

    bool ShowComponentEditor(object? @object, IWin32Window? parent);

    DialogResult ShowDialog(Form form);

    void ShowError(Exception ex);

    void ShowError(Exception ex, string message);

    void ShowError(string message);

    void ShowMessage(string message);

    void ShowMessage(string message, string caption);

    DialogResult ShowMessage(string message, string caption, MessageBoxButtons buttons);

    bool ShowToolWindow(Guid toolWindow);
}
