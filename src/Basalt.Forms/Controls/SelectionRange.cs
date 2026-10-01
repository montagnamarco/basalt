using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A span of dates, inclusive of both ends, that <see cref="MonthCalendar"/> reports and accepts.</summary>
[TypeConverter(typeof(SelectionRangeConverter))]
public sealed class SelectionRange
{
    public SelectionRange()
    {
        Start = DateTime.Now.Date;
        End = Start;
    }

    public SelectionRange(SelectionRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        Start = range.Start;
        End = range.End;
    }

    public SelectionRange(DateTime lower, DateTime upper)
    {
        if (lower < upper)
        {
            Start = lower;
            End = upper;
        }
        else
        {
            Start = upper;
            End = lower;
        }
    }

    public DateTime End { get; set; }

    public DateTime Start { get; set; }

    public override string ToString() => $"SelectionRange: Start: {Start}, End: {End}";
}
