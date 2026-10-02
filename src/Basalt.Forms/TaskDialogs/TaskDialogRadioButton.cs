namespace System.Windows.Forms;

/// <summary>One radio button offered by a task dialog page; only one in the page's collection can be checked at a time.</summary>
public sealed class TaskDialogRadioButton : TaskDialogControl
{
    private bool _checked;
    private bool _enabled = true;

    public TaskDialogRadioButton()
    {
    }

    public TaskDialogRadioButton(string? text) => Text = text;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;

            OwnerPage?.RadioButtons.UncheckOthers(this, value);
            SetChecked(value);
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            NotifyBoundFormOfChange();
        }
    }

    public string? Text { get; set; }

    public event EventHandler? CheckedChanged;

    /// <summary>Sets the backing field and raises the event, without touching the other radio buttons.</summary>
    internal void SetChecked(bool value)
    {
        if (_checked == value) return;
        _checked = value;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        NotifyBoundFormOfChange();
    }

    public override string ToString() => Text ?? string.Empty;
}
