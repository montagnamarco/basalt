namespace System.Windows.Forms;

/// <summary>
/// The public face of the drag notifications a <see cref="Control"/> answers
/// on its protected On* methods. Microsoft exposes it so a host that is not a
/// <see cref="Control"/> subclass can still forward drag messages to one.
/// </summary>
public interface IDropTarget
{
    void OnDragDrop(DragEventArgs e);

    void OnDragEnter(DragEventArgs e);

    void OnDragLeave(EventArgs e);

    void OnDragOver(DragEventArgs e);
}
