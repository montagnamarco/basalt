namespace System.Windows.Forms;

/// <summary>ListView's own half of ListViewInsertionMark: the lazily-created instance, and the repaint its Index/AppearsAfterItem/Color setters trigger.</summary>
public partial class ListView
{
    private ListViewInsertionMark? _insertionMark;

    public ListViewInsertionMark InsertionMark => _insertionMark ??= new ListViewInsertionMark(this);

    /// <summary>Repaints just the strip the mark currently occupies; a no-op while Index is -1, matching Bounds being empty then.</summary>
    internal void InvalidateInsertionMarkCore()
    {
        if (_insertionMark is null) return;

        var bounds = _insertionMark.Bounds;
        if (!bounds.IsEmpty) Invalidate(bounds);
    }
}
