namespace System.Windows.Forms;

/// <summary>The progress bar a task dialog page can show below its text, in the same states System.Windows.Forms.ProgressBar supports.</summary>
public sealed class TaskDialogProgressBar : TaskDialogControl
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private TaskDialogProgressBarState _state;

    public TaskDialogProgressBar() => _state = TaskDialogProgressBarState.Normal;

    public TaskDialogProgressBar(TaskDialogProgressBarState state) => _state = state;

    public int MarqueeSpeed { get; set; } = 100;

    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; NotifyBoundFormOfChange(); }
    }

    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; NotifyBoundFormOfChange(); }
    }

    public int Value
    {
        get => _value;
        set
        {
            if (value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            _value = value;
            NotifyBoundFormOfChange();
        }
    }

    public TaskDialogProgressBarState State
    {
        get => _state;
        set { _state = value; NotifyBoundFormOfChange(); }
    }
}
