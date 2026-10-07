using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// In-process drag and drop: <see cref="DoDragDrop"/> runs its own loop under
/// mouse capture, polling the pointer the way the real OLE drag loop polls
/// native messages, because this control tree has no native window to send
/// WM_MOUSEMOVE to a hidden drag session. A control two frames up reading
/// <see cref="FormsInput.ScreenPosition"/> every pass is how it reacts to
/// input arriving from the platform's own dispatcher as well as from a test
/// driving it directly.
/// </summary>
public partial class Control : IDropTarget
{
    private bool _allowDrop;

    [DefaultValue(false)]
    [Category("Behavior")]
    public virtual bool AllowDrop
    {
        get => _allowDrop;
        set => _allowDrop = value;
    }

    public DragDropEffects DoDragDrop(object data, DragDropEffects allowedEffects) =>
        DoDragDrop(data, allowedEffects, dragImage: null, cursorOffset: Point.Empty, useDefaultDragImage: false);

    public DragDropEffects DoDragDropAsJson<T>(T data, DragDropEffects allowedEffects) =>
        DoDragDropAsJson(data, allowedEffects, null, Point.Empty, false);

    public DragDropEffects DoDragDropAsJson<T>(T data, DragDropEffects allowedEffects,
        Bitmap? dragImage, Point cursorOffset, bool useDefaultDragImage)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (typeof(T) == typeof(DataObject))
            throw new InvalidOperationException("Use DataObject.SetDataAsJson to add JSON data before starting a drag.");

        var wrapped = new DataObject();
        wrapped.SetDataAsJson(data);
        return DoDragDrop(wrapped, allowedEffects, dragImage, cursorOffset, useDefaultDragImage);
    }

    public DragDropEffects DoDragDrop(object data, DragDropEffects allowedEffects, Bitmap? dragImage, Point cursorOffset, bool useDefaultDragImage)
    {
        ArgumentNullException.ThrowIfNull(data);

        var dataObject = data as IDataObject ?? new DataObject(data);
        var previousCursor = Cursor.Current;
        Control? target = null;
        var effect = DragDropEffects.None;

        FormsInput.Dragging = true;
        Capture = true;

        try
        {
            while (true)
            {
                WaitForDragInput();

                var buttons = FormsInput.Buttons;
                var escapePressed = FormsInput.IsKeyHeld(Keys.Escape);
                var defaultAction = escapePressed ? DragAction.Cancel
                    : buttons == MouseButtons.None ? DragAction.Drop
                    : DragAction.Continue;

                var query = new QueryContinueDragEventArgs(KeyState(buttons), escapePressed, defaultAction);
                OnQueryContinueDrag(query);

                if (query.Action == DragAction.Cancel)
                {
                    target?.OnDragLeave(EventArgs.Empty);
                    effect = DragDropEffects.None;
                    break;
                }

                var screenPosition = FormsInput.ScreenPosition;
                var found = DropTargetAt(screenPosition);
                var dragArguments = new DragEventArgs(dataObject, KeyState(buttons), screenPosition.X, screenPosition.Y, allowedEffects, effect);

                if (found != target)
                {
                    target?.OnDragLeave(EventArgs.Empty);
                    target = found;

                    if (target is not null) target.OnDragEnter(dragArguments);
                }
                else
                {
                    target?.OnDragOver(dragArguments);
                }

                effect = target is null ? DragDropEffects.None : dragArguments.Effect & allowedEffects;

                var feedback = new GiveFeedbackEventArgs(effect, useDefaultCursors: true, dragImage, cursorOffset, useDefaultDragImage);
                OnGiveFeedback(feedback);
                if (feedback.UseDefaultCursors) Cursor.Current = effect == DragDropEffects.None ? Cursors.No : Cursors.Default;

                if (query.Action != DragAction.Drop) continue;

                if (target is not null && effect != DragDropEffects.None)
                {
                    var dropArguments = new DragEventArgs(dataObject, KeyState(buttons), screenPosition.X, screenPosition.Y, allowedEffects, effect);
                    target.OnDragDrop(dropArguments);
                    effect = dropArguments.Effect & allowedEffects;
                }
                else
                {
                    target?.OnDragLeave(EventArgs.Empty);
                    effect = DragDropEffects.None;
                }

                break;
            }
        }
        finally
        {
            FormsInput.Dragging = false;
            Capture = false;
            Cursor.Current = previousCursor;
        }

        return effect;
    }

    /// <summary>
    /// The nearest control at a screen point, in this control's own window,
    /// that accepts drops; null off the window or when nothing under the
    /// point (nor any of its ancestors) has <see cref="AllowDrop"/> set.
    /// </summary>
    private Control? DropTargetAt(Point screenPosition)
    {
        if (TopLevelControl is not { } window) return null;

        var clientPoint = window.PointToClient(screenPosition);
        if (!window.ClientRectangle.Contains(clientPoint)) return null;

        var (hit, _) = window.HitTest(clientPoint);
        for (var candidate = hit; candidate is not null; candidate = candidate.Parent)
        {
            if (candidate.AllowDrop) return candidate;
        }

        return null;
    }

    /// <summary>The MK_* bits Windows packs into a drag message's key state: buttons, then Shift, Ctrl, middle button, Alt.</summary>
    private static int KeyState(MouseButtons buttons)
    {
        var state = 0;
        if ((buttons & MouseButtons.Left) != 0) state |= 0x0001;
        if ((buttons & MouseButtons.Right) != 0) state |= 0x0002;
        if ((FormsInput.Modifiers & Keys.Shift) != 0) state |= 0x0004;
        if ((FormsInput.Modifiers & Keys.Control) != 0) state |= 0x0008;
        if ((buttons & MouseButtons.Middle) != 0) state |= 0x0010;
        if ((FormsInput.Modifiers & Keys.Alt) != 0) state |= 0x0020;

        return state;
    }

    protected virtual void OnDragDrop(DragEventArgs drgevent) => ((DragEventHandler?)Events[s_dragDropEvent])?.Invoke(this, drgevent);

    protected virtual void OnDragEnter(DragEventArgs drgevent) => ((DragEventHandler?)Events[s_dragEnterEvent])?.Invoke(this, drgevent);

    protected virtual void OnDragLeave(EventArgs e) => ((EventHandler?)Events[s_dragLeaveEvent])?.Invoke(this, e);

    protected virtual void OnDragOver(DragEventArgs drgevent) => ((DragEventHandler?)Events[s_dragOverEvent])?.Invoke(this, drgevent);

    protected virtual void OnGiveFeedback(GiveFeedbackEventArgs gfbevent) => ((GiveFeedbackEventHandler?)Events[s_giveFeedbackEvent])?.Invoke(this, gfbevent);

    protected virtual void OnQueryContinueDrag(QueryContinueDragEventArgs qcdevent) => ((QueryContinueDragEventHandler?)Events[s_queryContinueDragEvent])?.Invoke(this, qcdevent);

    void IDropTarget.OnDragDrop(DragEventArgs e) => OnDragDrop(e);

    void IDropTarget.OnDragEnter(DragEventArgs e) => OnDragEnter(e);

    void IDropTarget.OnDragLeave(EventArgs e) => OnDragLeave(e);

    void IDropTarget.OnDragOver(DragEventArgs e) => OnDragOver(e);

    [Category("DragDrop")]
    public event DragEventHandler? DragDrop
    {
        add => Events.AddHandler(s_dragDropEvent, value);
        remove => Events.RemoveHandler(s_dragDropEvent, value);
    }

    [Category("DragDrop")]
    public event DragEventHandler? DragEnter
    {
        add => Events.AddHandler(s_dragEnterEvent, value);
        remove => Events.RemoveHandler(s_dragEnterEvent, value);
    }

    [Category("DragDrop")]
    public event EventHandler? DragLeave
    {
        add => Events.AddHandler(s_dragLeaveEvent, value);
        remove => Events.RemoveHandler(s_dragLeaveEvent, value);
    }

    [Category("DragDrop")]
    public event DragEventHandler? DragOver
    {
        add => Events.AddHandler(s_dragOverEvent, value);
        remove => Events.RemoveHandler(s_dragOverEvent, value);
    }

    [Category("DragDrop")]
    public event GiveFeedbackEventHandler? GiveFeedback
    {
        add => Events.AddHandler(s_giveFeedbackEvent, value);
        remove => Events.RemoveHandler(s_giveFeedbackEvent, value);
    }

    [Category("DragDrop")]
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

    /// <summary>
    /// Runs the message loop until input arrives or a short interval passes, as OLE's drag loop
    /// sleeps in its message wait: the drag sees each mouse move without spinning a processor.
    /// </summary>
    private static void WaitForDragInput()
    {
        var frame = new Avalonia.Threading.DispatcherFrame();
        var timer = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(15), Avalonia.Threading.DispatcherPriority.Input, (_, _) => frame.Continue = false);
        timer.Start();

        try
        {
            Avalonia.Threading.Dispatcher.UIThread.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
    }
}
