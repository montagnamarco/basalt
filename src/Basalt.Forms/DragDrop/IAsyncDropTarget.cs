namespace System.Windows.Forms;

/// <summary>A drop target that can run the drop asynchronously, instead of blocking the drag source until it finishes.</summary>
public interface IAsyncDropTarget : IDropTarget
{
    void OnAsyncDragDrop(DragEventArgs e);
}
