using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A TabPage about to be selected or deselected, that a handler may cancel.</summary>
public class TabControlCancelEventArgs : CancelEventArgs
{
    public TabControlCancelEventArgs(TabPage? tabPage, int tabPageIndex, bool cancel, TabControlAction action)
        : base(cancel)
    {
        TabPage = tabPage;
        TabPageIndex = tabPageIndex;
        Action = action;
    }

    public TabControlAction Action { get; }

    public TabPage? TabPage { get; }

    public int TabPageIndex { get; }
}
