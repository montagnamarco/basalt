using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Drag and drop on one item: starting a drag reuses the owning strip's own
/// <see cref="Control.DoDragDrop(object, DragDropEffects)"/> (a <see cref="ToolStrip"/>
/// is a <see cref="Control"/> and already runs the whole polling drag loop in
/// <c>Control.DragDrop.cs</c>), while accepting one is the same On* pipeline
/// Control exposes through <see cref="IDropTarget"/>, just driven by whatever
/// hit-tests items instead of child controls.
/// </summary>
public abstract partial class ToolStripItem : IDropTarget
{
    private bool _allowDrop;

    [System.ComponentModel.DefaultValue(false)]
    public virtual bool AllowDrop
    {
        get => _allowDrop;
        set => _allowDrop = value;
    }

    public DragDropEffects DoDragDrop(object data, DragDropEffects allowedEffects) =>
        DoDragDrop(data, allowedEffects, dragImage: null, cursorOffset: Point.Empty, useDefaultDragImage: false);

    /// <summary>
    /// There is no window of its own to capture the mouse: the item borrows
    /// the one belonging to whatever strip currently shows it.
    /// </summary>
    public DragDropEffects DoDragDrop(object data, DragDropEffects allowedEffects, Bitmap? dragImage, Point cursorOffset, bool useDefaultDragImage)
    {
        if (GetCurrentParent() is not { } parent) return DragDropEffects.None;

        return parent.DoDragDrop(data, allowedEffects, dragImage, cursorOffset, useDefaultDragImage);
    }

    protected virtual void OnDragDrop(DragEventArgs e) => ((DragEventHandler?)Events[s_dragDropEvent])?.Invoke(this, e);

    protected virtual void OnDragEnter(DragEventArgs e) => ((DragEventHandler?)Events[s_dragEnterEvent])?.Invoke(this, e);

    protected virtual void OnDragLeave(EventArgs e) => ((EventHandler?)Events[s_dragLeaveEvent])?.Invoke(this, e);

    protected virtual void OnDragOver(DragEventArgs e) => ((DragEventHandler?)Events[s_dragOverEvent])?.Invoke(this, e);

    protected virtual void OnGiveFeedback(GiveFeedbackEventArgs e) => ((GiveFeedbackEventHandler?)Events[s_giveFeedbackEvent])?.Invoke(this, e);

    protected virtual void OnQueryContinueDrag(QueryContinueDragEventArgs e) => ((QueryContinueDragEventHandler?)Events[s_queryContinueDragEvent])?.Invoke(this, e);

    void IDropTarget.OnDragDrop(DragEventArgs e) => OnDragDrop(e);

    void IDropTarget.OnDragEnter(DragEventArgs e) => OnDragEnter(e);

    void IDropTarget.OnDragLeave(EventArgs e) => OnDragLeave(e);

    void IDropTarget.OnDragOver(DragEventArgs e) => OnDragOver(e);

    public event DragEventHandler? DragDrop
    {
        add => Events.AddHandler(s_dragDropEvent, value);
        remove => Events.RemoveHandler(s_dragDropEvent, value);
    }

    public event DragEventHandler? DragEnter
    {
        add => Events.AddHandler(s_dragEnterEvent, value);
        remove => Events.RemoveHandler(s_dragEnterEvent, value);
    }

    public event EventHandler? DragLeave
    {
        add => Events.AddHandler(s_dragLeaveEvent, value);
        remove => Events.RemoveHandler(s_dragLeaveEvent, value);
    }

    public event DragEventHandler? DragOver
    {
        add => Events.AddHandler(s_dragOverEvent, value);
        remove => Events.RemoveHandler(s_dragOverEvent, value);
    }

    public event GiveFeedbackEventHandler? GiveFeedback
    {
        add => Events.AddHandler(s_giveFeedbackEvent, value);
        remove => Events.RemoveHandler(s_giveFeedbackEvent, value);
    }

    public event QueryContinueDragEventHandler? QueryContinueDrag
    {
        add => Events.AddHandler(s_queryContinueDragEvent, value);
        remove => Events.RemoveHandler(s_queryContinueDragEvent, value);
    }

    private static readonly object s_dragDropEvent = new();
    private static readonly object s_dragEnterEvent = new();
    private static readonly object s_dragLeaveEvent = new();
    private static readonly object s_dragOverEvent = new();
    private static readonly object s_giveFeedbackEvent = new();
    private static readonly object s_queryContinueDragEvent = new();

    /// <summary>Called by whatever hit-tests items on behalf of a drag (a future ToolStrip hook); not part of the public contract.</summary>
    internal void RaiseDragEnter(DragEventArgs e) => OnDragEnter(e);

    internal void RaiseDragOver(DragEventArgs e) => OnDragOver(e);

    internal void RaiseDragLeave(EventArgs e) => OnDragLeave(e);

    internal void RaiseDragDrop(DragEventArgs e) => OnDragDrop(e);

    internal void RaiseGiveFeedback(GiveFeedbackEventArgs e) => OnGiveFeedback(e);

    internal void RaiseQueryContinueDrag(QueryContinueDragEventArgs e) => OnQueryContinueDrag(e);
}
