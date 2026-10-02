namespace System.Windows.Forms;

/// <summary>
/// The content shown by a task dialog: its heading, text and icon, and the
/// controls (buttons, radio buttons, verification check box, expander,
/// footnote and progress bar) that go with it. A page is bound to a
/// <see cref="TaskDialog"/> for as long as that dialog has it on screen,
/// whether as the page it was shown with or one reached through Navigate.
/// </summary>
public class TaskDialogPage
{
    private TaskDialogButtonCollection _buttons = new();
    private TaskDialogRadioButtonCollection _radioButtons = new();
    private TaskDialogExpander? _expander;
    private TaskDialogFootnote? _footnote;
    private TaskDialogProgressBar? _progressBar;
    private TaskDialogVerificationCheckBox? _verification;

    public TaskDialogPage() => _buttons.OwnerPage = _radioButtons.OwnerPage = this;

    public bool AllowCancel { get; set; }

    public bool AllowMinimize { get; set; }

    /// <summary>The dialog currently showing this page, or null; set by TaskDialog as it shows or stops showing it.</summary>
    public TaskDialog? BoundDialog { get; internal set; }

    public TaskDialogButtonCollection Buttons
    {
        get => _buttons;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _buttons.OwnerPage = null;
            _buttons = value;
            _buttons.OwnerPage = this;
        }
    }

    public string? Caption { get; set; }

    public TaskDialogButton? DefaultButton { get; set; }

    public bool EnableLinks { get; set; }

    public TaskDialogExpander? Expander
    {
        get => _expander;
        set { if (_expander is not null) _expander.OwnerPage = null; _expander = value; if (_expander is not null) _expander.OwnerPage = this; }
    }

    public TaskDialogFootnote? Footnote
    {
        get => _footnote;
        set { if (_footnote is not null) _footnote.OwnerPage = null; _footnote = value; if (_footnote is not null) _footnote.OwnerPage = this; }
    }

    public string? Heading { get; set; }

    public TaskDialogIcon? Icon { get; set; }

    public TaskDialogProgressBar? ProgressBar
    {
        get => _progressBar;
        set { if (_progressBar is not null) _progressBar.OwnerPage = null; _progressBar = value; if (_progressBar is not null) _progressBar.OwnerPage = this; }
    }

    public TaskDialogRadioButtonCollection RadioButtons
    {
        get => _radioButtons;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _radioButtons.OwnerPage = null;
            _radioButtons = value;
            _radioButtons.OwnerPage = this;
        }
    }

    public bool RightToLeftLayout { get; set; }

    public bool SizeToContent { get; set; }

    public string? Text { get; set; }

    public TaskDialogVerificationCheckBox? Verification
    {
        get => _verification;
        set { if (_verification is not null) _verification.OwnerPage = null; _verification = value; if (_verification is not null) _verification.OwnerPage = this; }
    }

    public event EventHandler? Created;

    public event EventHandler? Destroyed;

    public event EventHandler? HelpRequest;

    public event EventHandler<TaskDialogLinkClickedEventArgs>? LinkClicked;

    /// <summary>Replaces the content the bound dialog shows with this new page, while the dialog stays open.</summary>
    public void Navigate(TaskDialogPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        BoundDialog?.NavigateToPage(page);
    }

    protected void OnCreated(EventArgs e) => Created?.Invoke(this, e);

    protected void OnDestroyed(EventArgs e) => Destroyed?.Invoke(this, e);

    protected void OnHelpRequest(EventArgs e) => HelpRequest?.Invoke(this, e);

    protected void OnLinkClicked(TaskDialogLinkClickedEventArgs e) => LinkClicked?.Invoke(this, e);

    /// <summary>Called by TaskDialog once the native-equivalent content for this page is up.</summary>
    internal void RaiseCreated() => OnCreated(EventArgs.Empty);

    /// <summary>Called by TaskDialog as it stops showing this page, before another page or the dialog closes.</summary>
    internal void RaiseDestroyed() => OnDestroyed(EventArgs.Empty);

    internal void RaiseHelpRequest() => OnHelpRequest(EventArgs.Empty);

    internal void RaiseLinkClicked(string linkHref) => OnLinkClicked(new TaskDialogLinkClickedEventArgs(linkHref));

    /// <summary>Tells the bound dialog's form to redraw or relayout, after a control on this page changed.</summary>
    internal void NotifyBoundFormOfChange() => BoundDialog?.NotifyOfChange();
}
