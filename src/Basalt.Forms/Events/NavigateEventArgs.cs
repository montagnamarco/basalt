namespace System.Windows.Forms;

// Not obsolete: a small, functioning event-args type that the legacy DataGrid
// (src/Basalt.Forms/Legacy/DataGrid.cs) needs for its public Navigate event
// and OnNavigate method. Added here, the usual home for event args, because
// DataGrid cannot declare its documented signature without it.
public class NavigateEventArgs : EventArgs
{
    public NavigateEventArgs(bool isForward)
    {
        Forward = isForward;
    }

    public bool Forward { get; }
}
