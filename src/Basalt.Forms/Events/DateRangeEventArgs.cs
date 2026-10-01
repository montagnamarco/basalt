namespace System.Windows.Forms;

public class DateRangeEventArgs : EventArgs
{
    public DateRangeEventArgs(DateTime start, DateTime end)
    {
        Start = start;
        End = end;
    }

    public DateTime End { get; }

    public DateTime Start { get; }
}
