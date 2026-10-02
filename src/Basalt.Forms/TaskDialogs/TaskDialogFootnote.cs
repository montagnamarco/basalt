namespace System.Windows.Forms;

/// <summary>The small line of text with an optional icon a task dialog page can show above its buttons.</summary>
public sealed class TaskDialogFootnote : TaskDialogControl
{
    public TaskDialogFootnote()
    {
    }

    public TaskDialogFootnote(string? text) => Text = text;

    public TaskDialogIcon? Icon { get; set; }

    public string? Text { get; set; }

    public override string ToString() => Text ?? string.Empty;

    public static implicit operator TaskDialogFootnote(string footnoteText) => new(footnoteText);
}
