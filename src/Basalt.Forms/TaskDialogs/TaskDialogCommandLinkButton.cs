namespace System.Windows.Forms;

/// <summary>
/// A button shown as a command link: a bold title with a smaller line of
/// description text underneath, stacked with the other buttons instead of
/// sitting in a row.
/// </summary>
public sealed class TaskDialogCommandLinkButton : TaskDialogButton
{
    public TaskDialogCommandLinkButton()
    {
    }

    public TaskDialogCommandLinkButton(string? text, string? descriptionText = null, bool enabled = true, bool allowCloseDialog = true)
        : base(text, enabled, allowCloseDialog)
    {
        DescriptionText = descriptionText;
    }

    public string? DescriptionText { get; set; }
}
