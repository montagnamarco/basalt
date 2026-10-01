namespace System.Windows.Forms;

/// <summary>A TabPage that was selected or deselected, and which of the two happened.</summary>
public class TabControlEventArgs : EventArgs
{
    public TabControlEventArgs(TabPage? tabPage, int tabPageIndex, TabControlAction action)
    {
        TabPage = tabPage;
        TabPageIndex = tabPageIndex;
        Action = action;
    }

    public TabControlAction Action { get; }

    public TabPage? TabPage { get; }

    public int TabPageIndex { get; }
}
