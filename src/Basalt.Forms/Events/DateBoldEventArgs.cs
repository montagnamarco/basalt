namespace System.Windows.Forms;

/// <summary>
/// The dates MonthCalendar asked an owner-draw handler to decide, back when
/// WinForms 1.1 let an application compute bold days on request instead of
/// setting <see cref="MonthCalendar.BoldedDates"/> directly. Not raised by
/// this implementation (BoldedDates and UpdateBoldedDates replace it), but
/// kept so code written against the older callback shape still compiles.
/// </summary>
public class DateBoldEventArgs : EventArgs
{
    internal DateBoldEventArgs(DateTime startDate, int size)
    {
        StartDate = startDate;
        Size = size;
        DaysToBold = new int[size];
    }

    public DateTime StartDate { get; }

    public int Size { get; }

    public int[] DaysToBold { get; set; }
}
