using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// A button offered by a task dialog: either one of the standard ones below,
/// built with the kind that makes two separate instances compare equal, or a
/// custom button built from text.
/// </summary>
public class TaskDialogButton : TaskDialogControl
{
    private readonly StandardButton _kind;
    private bool _enabled = true;
    private bool _visible = true;

    public static TaskDialogButton OK => new(StandardButton.OK);
    public static TaskDialogButton Cancel => new(StandardButton.Cancel);
    public static TaskDialogButton Abort => new(StandardButton.Abort);
    public static TaskDialogButton Retry => new(StandardButton.Retry);
    public static TaskDialogButton Ignore => new(StandardButton.Ignore);
    public static TaskDialogButton Yes => new(StandardButton.Yes);
    public static TaskDialogButton No => new(StandardButton.No);
    public static TaskDialogButton TryAgain => new(StandardButton.TryAgain);
    public static TaskDialogButton Continue => new(StandardButton.Continue);
    public static TaskDialogButton Close => new(StandardButton.Close);
    public static TaskDialogButton Help => new(StandardButton.Help);

    public TaskDialogButton()
    {
    }

    public TaskDialogButton(string? text, bool enabled = true, bool allowCloseDialog = true)
    {
        Text = text;
        _enabled = enabled;
        AllowCloseDialog = allowCloseDialog;
    }

    /// <summary>Built by the static properties above; the caption comes from the same table MessageBox uses.</summary>
    private TaskDialogButton(StandardButton kind)
    {
        _kind = kind;
        Text = StandardButtonCaptions.For(kind);
    }

    public bool AllowCloseDialog { get; set; } = true;

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

    public bool ShowShieldIcon { get; set; }

    public string? Text { get; set; }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            NotifyBoundFormOfChange();
        }
    }

    public event EventHandler? Click;

    /// <summary>Raises Click as if the user had pressed this button; closes the dialog unless AllowCloseDialog is false.</summary>
    public void PerformClick() => RaiseClick();

    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

    public override bool Equals(object? obj) =>
        obj is TaskDialogButton other && (_kind != StandardButton.None ? _kind == other._kind : ReferenceEquals(this, other));

    public override int GetHashCode() => _kind != StandardButton.None ? _kind.GetHashCode() : base.GetHashCode();

    public override string ToString() => Text ?? string.Empty;

    public static bool operator ==(TaskDialogButton? b1, TaskDialogButton? b2) =>
        ReferenceEquals(b1, b2) || (b1 is not null && b2 is not null && b1.Equals(b2));

    public static bool operator !=(TaskDialogButton? b1, TaskDialogButton? b2) => !(b1 == b2);
}
