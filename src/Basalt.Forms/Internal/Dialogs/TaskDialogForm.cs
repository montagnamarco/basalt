using System.Drawing;

namespace System.Windows.Forms.Internal;

/// <summary>
/// The form behind TaskDialog: built from a TaskDialogPage the same way
/// MessageBoxForm is built from MessageBox's arguments, but with the extra
/// controls a task dialog page can carry. Content is rebuilt whenever the
/// page is navigated or a control on it changes something that affects
/// layout, since task dialogs are not shown by the real Windows common
/// control here, only reproduced with our own controls.
/// </summary>
internal sealed partial class TaskDialogForm : Form
{
    private const int Edge = 11;
    private const int IconSize = 32;
    private const int FootnoteIconSize = 16;
    private const int IconGap = 12;
    private const int RowGap = 8;
    private const int ButtonGap = 6;
    private const int ContentWidth = 340;

    private TaskDialogPage _page;
    private readonly TaskDialogStartupLocation _startupLocation;
    private bool _suspended;

    public TaskDialogForm(TaskDialogPage page, TaskDialogStartupLocation startupLocation)
    {
        _page = page;
        _startupLocation = startupLocation;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        Font = SystemFonts.MessageBoxFont ?? DefaultFont;
        StartPosition = startupLocation == TaskDialogStartupLocation.CenterOwner
            ? FormStartPosition.CenterParent
            : FormStartPosition.CenterScreen;

        BuildContent();
    }

    /// <summary>Set by TaskDialog right after construction, so events raised here can reach it.</summary>
    public TaskDialog? Dialog { get; set; }

    public TaskDialogPage Page => _page;

    public TaskDialogButton? ClickedButton { get; private set; }

    /// <summary>Replaces the page shown, tearing down the old controls and building the new ones in their place.</summary>
    public void Navigate(TaskDialogPage newPage)
    {
        _page = newPage;
        BuildContent();
    }

    /// <summary>Rebuilds the layout after a property changed on a bound control (value, text, checked...).</summary>
    public void RelayoutContent()
    {
        if (_suspended) return;
        BuildContent();
    }

    /// <summary>Closes the dialog as Cancel, as if the user had triggered its default cancellation.</summary>
    public void CloseDialog()
    {
        ClickedButton ??= TaskDialogButton.Cancel;
        if (IsHandleCreated) Close();
    }

    /// <summary>A button (standard, custom or command link) was clicked: raises Click, then closes unless told not to.</summary>
    private void ButtonClicked(TaskDialogButton button)
    {
        var pageBeforeClick = _page;

        button.RaiseClick();

        // The handler may already have called TaskDialog.Close() (which sets ClickedButton
        // itself) or page.Navigate(...) (which replaces this page's content, including the
        // button just clicked): either way, there is nothing left for us to do here.
        if (_page != pageBeforeClick || ClickedButton is not null) return;

        if (!button.AllowCloseDialog) return;

        ClickedButton = button;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The title bar's close box and Alt+F4 go through here; a button's
        // own Close() call already set ClickedButton, so only the cancel
        // path (no button clicked yet) needs the AllowCancel check.
        if (ClickedButton is null && !_page.AllowCancel)
        {
            e.Cancel = true;
        }
        else
        {
            ClickedButton ??= TaskDialogButton.Cancel;
        }

        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl?.Focus();
    }
}
