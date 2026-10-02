namespace System.Windows.Forms;

/// <summary>The "Don't show this again" style check box a task dialog page can offer below its text.</summary>
public sealed class TaskDialogVerificationCheckBox : TaskDialogControl
{
    private bool _checked;

    public TaskDialogVerificationCheckBox()
    {
    }

    public TaskDialogVerificationCheckBox(string? text, bool isChecked = false)
    {
        Text = text;
        _checked = isChecked;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            NotifyBoundFormOfChange();
        }
    }

    public string? Text { get; set; }

    public event EventHandler? CheckedChanged;

    public override string ToString() => Text ?? string.Empty;

    public static implicit operator TaskDialogVerificationCheckBox(string verificationText) => new(verificationText);
}
