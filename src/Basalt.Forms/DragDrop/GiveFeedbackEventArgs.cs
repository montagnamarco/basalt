using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Lets the drag's source react to the effect the pointer is currently over,
/// typically by choosing the cursor; <see cref="Control.DoDragDrop"/> raises
/// it on the source control once per iteration of the drag loop.
/// </summary>
public class GiveFeedbackEventArgs : EventArgs
{
    public GiveFeedbackEventArgs(DragDropEffects effect, bool useDefaultCursors)
    {
        Effect = effect;
        UseDefaultCursors = useDefaultCursors;
    }

    public GiveFeedbackEventArgs(DragDropEffects effect, bool useDefaultCursors, Bitmap? dragImage, Point cursorOffset, bool useDefaultDragImage)
        : this(effect, useDefaultCursors)
    {
        DragImage = dragImage;
        CursorOffset = cursorOffset;
        UseDefaultDragImage = useDefaultDragImage;
    }

    public DragDropEffects Effect { get; }

    /// <summary>False when a handler already set the cursor itself.</summary>
    public bool UseDefaultCursors { get; set; }

    public Bitmap? DragImage { get; set; }

    public Point CursorOffset { get; set; }

    public bool UseDefaultDragImage { get; set; }
}
