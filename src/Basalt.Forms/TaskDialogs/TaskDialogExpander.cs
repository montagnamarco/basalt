namespace System.Windows.Forms;

/// <summary>
/// The collapsible details area a task dialog page can offer: a line of text
/// shown under the main text or the footnote, which the user expands to read
/// the rest.
/// </summary>
public sealed class TaskDialogExpander : TaskDialogControl
{
    private bool _expanded;

    public TaskDialogExpander()
    {
    }

    public TaskDialogExpander(string? text) => Text = text;

    public string? CollapsedButtonText { get; set; }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
            NotifyBoundFormOfChange();
        }
    }

    public string? ExpandedButtonText { get; set; }

    public TaskDialogExpanderPosition Position { get; set; }

    public string? Text { get; set; }

    public event EventHandler? ExpandedChanged;

    public override string ToString() => Text ?? string.Empty;
}
