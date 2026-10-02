using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// A modal dialog built from a <see cref="TaskDialogPage"/>: heading, text,
/// icon, radio buttons, a verification check box, an expander, a footnote, a
/// progress bar and buttons, drawn in the classic Windows 2000 look like the
/// rest of Basalt.Forms. Shown with ShowDialog, which returns the button the
/// user clicked.
/// </summary>
public class TaskDialog : IWin32Window
{
    private readonly TaskDialogForm _form;

    private TaskDialog(TaskDialogForm form) => _form = form;

    public IntPtr Handle => _form.Handle;

    /// <summary>Closes the dialog as if its default cancellation had been triggered.</summary>
    public void Close() => _form.CloseDialog();

    public static TaskDialogButton ShowDialog(TaskDialogPage page, TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner) =>
        ShowDialogCore(null, page, startupLocation);

    public static TaskDialogButton ShowDialog(IWin32Window owner, TaskDialogPage page, TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return ShowDialogCore(owner, page, startupLocation);
    }

    public static TaskDialogButton ShowDialog(IntPtr hwndOwner, TaskDialogPage page, TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner) =>
        ShowDialogCore(hwndOwner == IntPtr.Zero ? null : new NativeOwner(hwndOwner), page, startupLocation);

    public static Task<TaskDialogButton> ShowDialogAsync(TaskDialogPage page, TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterScreen) =>
        PostShowDialog(() => ShowDialog(page, startupLocation));

    public static Task<TaskDialogButton> ShowDialogAsync(IWin32Window owner, TaskDialogPage page, TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner) =>
        PostShowDialog(() => ShowDialog(owner, page, startupLocation));

    public static Task<TaskDialogButton> ShowDialogAsync(IntPtr hwndOwner, TaskDialogPage page, TaskDialogStartupLocation startupLocation = TaskDialogStartupLocation.CenterOwner) =>
        PostShowDialog(() => ShowDialog(hwndOwner, page, startupLocation));

    private static TaskDialogButton ShowDialogCore(IWin32Window? owner, TaskDialogPage page, TaskDialogStartupLocation startupLocation)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.BoundDialog is not null) throw new InvalidOperationException("This page is already bound to a task dialog.");

        using var form = new TaskDialogForm(page, startupLocation);
        var dialog = new TaskDialog(form);
        page.BoundDialog = dialog;
        form.Dialog = dialog;

        try
        {
            page.RaiseCreated();
            form.ShowDialog(owner);
        }
        finally
        {
            // Navigate may have replaced the page the form shows one or more times
            // since page.RaiseCreated() above: the one to destroy is whichever page
            // is current now, not the original parameter. Destroyed is raised while
            // that page is still the one the dialog is showing, as Windows raises it,
            // so a handler can still read BoundDialog.
            var currentPage = form.Page;
            currentPage.RaiseDestroyed();
            currentPage.BoundDialog = null;
        }

        return form.ClickedButton ?? TaskDialogButton.Cancel;
    }

    /// <summary>Queues the whole (blocking, modal) call on the UI thread so the caller gets a Task back immediately.</summary>
    private static Task<TaskDialogButton> PostShowDialog(Func<TaskDialogButton> showDialog)
    {
        var completion = new TaskCompletionSource<TaskDialogButton>();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => completion.SetResult(showDialog()));
        return completion.Task;
    }

    /// <summary>Swaps the page shown by the form for a new one, keeping the dialog open.</summary>
    internal void NavigateToPage(TaskDialogPage newPage)
    {
        ArgumentNullException.ThrowIfNull(newPage);
        if (newPage.BoundDialog is not null) throw new InvalidOperationException("This page is already bound to a task dialog.");

        var oldPage = _form.Page;
        oldPage.RaiseDestroyed();
        oldPage.BoundDialog = null;

        newPage.BoundDialog = this;
        _form.Navigate(newPage);
        newPage.RaiseCreated();
    }

    internal void NotifyOfChange() => _form.RelayoutContent();

    /// <summary>An IWin32Window wrapping a bare native handle, for the IntPtr owner overloads.</summary>
    private sealed class NativeOwner(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
