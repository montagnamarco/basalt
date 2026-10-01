namespace System.Windows.Forms;

/// <summary>What a scroll bar or track bar did: which gesture, and the value before and after it.</summary>
public class ScrollEventArgs : EventArgs
{
    public ScrollEventArgs(ScrollEventType type, int newValue)
        : this(type, 0, newValue, ScrollOrientation.HorizontalScroll)
    {
    }

    public ScrollEventArgs(ScrollEventType type, int newValue, ScrollOrientation scroll)
        : this(type, 0, newValue, scroll)
    {
    }

    public ScrollEventArgs(ScrollEventType type, int oldValue, int newValue)
        : this(type, oldValue, newValue, ScrollOrientation.HorizontalScroll)
    {
    }

    public ScrollEventArgs(ScrollEventType type, int oldValue, int newValue, ScrollOrientation scroll)
    {
        Type = type;
        OldValue = oldValue;
        NewValue = newValue;
        ScrollOrientation = scroll;
    }

    /// <summary>The value the control is about to take; a handler may lower or raise it before it is applied.</summary>
    public int NewValue { get; set; }

    public int OldValue { get; }

    public ScrollOrientation ScrollOrientation { get; }

    public ScrollEventType Type { get; }
}
