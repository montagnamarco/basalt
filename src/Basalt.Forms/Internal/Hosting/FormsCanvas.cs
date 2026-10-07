using Avalonia;
using Avalonia.Input;
using AvaloniaKeyEventArgs = Avalonia.Input.KeyEventArgs;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// The one Avalonia element of a form's window. It paints the form's
/// recorded controls and passes every pointer and key event to the form's
/// own input routing. Native browser surfaces and accessibility owners are
/// hosted as children while ordinary controls remain drawn on this canvas.
/// </summary>
internal sealed class FormsCanvas : Avalonia.Controls.Control
{
    private readonly Control _form;
    private readonly FormsNativeViews _nativeViews;
    private readonly bool _includeFrame;

    /// <param name="form">The form or popup content this canvas paints.</param>
    /// <param name="includeFrame">
    /// Whether this canvas paints <paramref name="form"/>'s own non-client frame
    /// around its client area, rather than starting at the client area as a real
    /// top-level window's content does. A design surface sets this so the form
    /// being designed shows the window it would be at run time - border, caption,
    /// icon, title and control box - even though it is never actually shown.
    /// </param>
    public FormsCanvas(Control form, bool includeFrame = false)
    {
        _form = form;
        _includeFrame = includeFrame;
        _nativeViews = new FormsNativeViews(this);
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>The top-level control the canvas shows: a form, or the content of a popup window.</summary>
    public Control Root => _form;

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new FormsAutomationPeer(this);

    // These visual owners carry only accessibility. Painting and input stay
    // on the single canvas, while Avalonia can convert peer bounds to screen
    // coordinates without giving every peer the canvas's event subscriptions.
    internal void AttachAutomationOwner(Avalonia.Controls.Control owner) => VisualChildren.Add(owner);

    internal void DetachAutomationOwner(Avalonia.Controls.Control owner) => VisualChildren.Remove(owner);

    internal void AttachNativeView(Avalonia.Controls.Control view) => VisualChildren.Add(view);

    internal void DetachNativeView(Avalonia.Controls.Control view) => VisualChildren.Remove(view);

    internal void ReorderNativeView(Avalonia.Controls.Control view, int index)
    {
        var current = VisualChildren.IndexOf(view);
        if (current >= 0 && current != index) VisualChildren.Move(current, index);
    }

    internal void RequestFrame()
    {
        _nativeViews.Synchronize();
        InvalidateVisual();
    }

    /// <summary>
    /// Runs on the UI thread: invalid controls are painted (their Paint
    /// handlers run here, as WM_PAINT would), then the frame is handed to the
    /// render thread as one picture that replays the controls' pictures.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var frame = FormsInput.Guard(_form, () => RecordFrame(size));
        if (frame is null) return;

        context.Custom(new PictureOperation(new Rect(size), frame));
    }

    private SKPicture RecordFrame(Size size)
    {
        _form.RecordInvalidTree();

        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, (float)size.Width, (float)size.Height));
        _form.Compose(canvas, includeFrame: _includeFrame);

        return recorder.EndRecording();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        FormsInput.PointerMoved(_form, this, e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        FormsInput.PointerPressed(_form, this, e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        FormsInput.PointerReleased(_form, this, e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        FormsInput.PointerExited(_form);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        FormsInput.PointerWheel(_form, this, e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        FormsInput.CaptureLost(_form);
    }

    protected override void OnKeyDown(AvaloniaKeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.Handled = FormsInput.KeyDown(_form, e);
    }

    protected override void OnKeyUp(AvaloniaKeyEventArgs e)
    {
        base.OnKeyUp(e);
        e.Handled = FormsInput.KeyUp(_form, e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        e.Handled = FormsInput.TextInput(_form, e);
    }

    /// <summary>Replays a frame on the render thread. The picture is immutable, so it is safe there.</summary>
    private sealed class PictureOperation(Rect bounds, SKPicture picture) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;

            using var lease = feature.Lease();
            lease.SkCanvas.DrawPicture(picture);
        }

        public void Dispose() => picture.Dispose();
    }
}
