using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Skins;
using SkiaSharp;

namespace System.Windows.Forms;

/// <summary>
/// Invalidation and painting.
/// </summary>
/// <remarks>
/// A control's paint is recorded, not drawn: when part of the control is
/// invalid, OnPaintBackground and OnPaint run on the UI thread against a
/// recorder clipped to that part, and the recording replaces the part in the
/// control's picture. A frame is then a composition of pictures, which the
/// render thread replays. That keeps WinForms' rules (paint happens on the UI
/// thread, only invalid parts are painted, ClipRectangle says which) while
/// the screen is drawn by the GPU.
/// </remarks>
public partial class Control
{
    /// <summary>The part of the client area that must be painted again, in client coordinates; empty when none.</summary>
    private Rectangle _invalidArea;

    /// <summary>The recorded client area, or null before the first paint.</summary>
    private SKPicture? _clientPicture;

    /// <summary>
    /// How many partial repaints are layered in the picture. Each one replays
    /// the previous picture outside the invalid part; past a few layers the
    /// whole control is painted again so the layers do not pile up.
    /// </summary>
    private int _pictureLayers;

    private const int MaximumPictureLayers = 4;

    internal Rectangle InvalidArea => _invalidArea;

    public void Invalidate() => Invalidate(false);

    public void Invalidate(bool invalidateChildren) => Invalidate(ClientRectangle, invalidateChildren);

    public void Invalidate(Rectangle rc) => Invalidate(rc, false);

    /// <summary>Marks part of the client area for painting, and the children under it when asked.</summary>
    public void Invalidate(Rectangle rc, bool invalidateChildren)
    {
        if (rc.IsEmpty) rc = ClientRectangle;

        rc.Intersect(ClientRectangle);
        if (rc.IsEmpty) return;

        _invalidArea = _invalidArea.IsEmpty ? rc : Rectangle.Union(_invalidArea, rc);

        if (invalidateChildren)
        {
            foreach (var child in ChildrenSnapshot())
            {
                var inChild = rc;
                inChild.Intersect(child.Bounds);
                if (inChild.IsEmpty) continue;

                var frame = child.NonClient;
                inChild.Offset(-child.Left - frame.Left, -child.Top - frame.Top);
                child.Invalidate(inChild, true);
            }
        }

        NotifyInvalidate(rc);
    }

    public void Invalidate(Region? region) => Invalidate(region, false);

    public void Invalidate(Region? region, bool invalidateChildren)
    {
        if (region is null)
        {
            Invalidate(invalidateChildren);
            return;
        }

        Invalidate(Rectangle.Ceiling(region.GetBounds(MeasurementGraphics)), invalidateChildren);
    }

    /// <summary>Raises Invalidated and asks the window this control is on to paint at its next opportunity.</summary>
    protected virtual void NotifyInvalidate(Rectangle invalidatedArea)
    {
        OnInvalidated(new InvalidateEventArgs(invalidatedArea));
        TopLevelControl?.RequestPaint();
    }

    protected virtual void OnInvalidated(InvalidateEventArgs e) => ((InvalidateEventHandler?)Events[s_invalidatedEvent])?.Invoke(this, e);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public event InvalidateEventHandler? Invalidated
    {
        add => Events.AddHandler(s_invalidatedEvent, value);
        remove => Events.RemoveHandler(s_invalidatedEvent, value);
    }

    /// <summary>Paints what is invalid now, so Paint handlers have run when Update returns.</summary>
    public void Update()
    {
        if (!IsHandleCreated) return;

        RecordInvalidTree();
        TopLevelControl?.RequestPaint();
    }

    /// <summary>Invalidates the control and its children and paints them now.</summary>
    public virtual void Refresh()
    {
        Invalidate(true);
        Update();
    }

    /// <summary>The window this control is the top of was told something needs painting; a window overrides it.</summary>
    internal virtual void RequestPaint()
    {
    }

    public event PaintEventHandler? Paint
    {
        add => Events.AddHandler(s_paintEvent, value);
        remove => Events.RemoveHandler(s_paintEvent, value);
    }

    protected virtual void OnPaint(PaintEventArgs e) => ((PaintEventHandler?)Events[s_paintEvent])?.Invoke(this, e);

    /// <summary>
    /// Fills the background: the parent's paint shows through a transparent
    /// colour, then the colour, then the background image.
    /// </summary>
    protected virtual void OnPaintBackground(PaintEventArgs pevent)
    {
        ArgumentNullException.ThrowIfNull(pevent);

        var backColor = BackColor;
        if (backColor.A < 255) PaintParentBackground(pevent);

        if (backColor.A > 0)
        {
            using var brush = new SolidBrush(backColor);
            pevent.Graphics.FillRectangle(brush, ClientRectangle);
        }

        if (BackgroundImage is { } image) PaintBackgroundImage(pevent.Graphics, image);
    }

    /// <summary>
    /// What WinForms does for a transparent background: paints the parent's
    /// background and foreground, shifted to this control, underneath.
    /// Siblings behind are not drawn, as on Windows.
    /// </summary>
    private void PaintParentBackground(PaintEventArgs e)
    {
        if (_parent is not { } parent) return;

        var graphics = e.Graphics;
        var state = graphics.Save();

        try
        {
            var frame = NonClient;
            graphics.TranslateTransform(-Left - frame.Left, -Top - frame.Top);

            var clip = e.ClipRectangle;
            clip.Offset(Left + frame.Left, Top + frame.Top);

            using var parentArguments = new PaintEventArgs(graphics, clip);
            parent.OnPaintBackground(parentArguments);
            parent.OnPaint(parentArguments);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private void PaintBackgroundImage(Graphics graphics, Image image)
    {
        var client = ClientRectangle;

        switch (BackgroundImageLayout)
        {
            case ImageLayout.None:
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
                break;

            case ImageLayout.Center:
                graphics.DrawImage(image, (client.Width - image.Width) / 2, (client.Height - image.Height) / 2, image.Width, image.Height);
                break;

            case ImageLayout.Stretch:
                graphics.DrawImage(image, client);
                break;

            case ImageLayout.Zoom:
            {
                var scale = Math.Min((float)client.Width / image.Width, (float)client.Height / image.Height);
                var width = (int)(image.Width * scale);
                var height = (int)(image.Height * scale);
                graphics.DrawImage(image, (client.Width - width) / 2, (client.Height - height) / 2, width, height);
                break;
            }

            default:
                using (var brush = new TextureBrush(image, Drawing.Drawing2D.WrapMode.Tile))
                {
                    graphics.FillRectangle(brush, client);
                }

                break;
        }
    }

    protected void InvokePaint(Control c, PaintEventArgs e) => c.OnPaint(e);

    protected void InvokePaintBackground(Control c, PaintEventArgs e) => c.OnPaintBackground(e);

    private static readonly object s_paintEvent = new();
    private static readonly object s_invalidatedEvent = new();

    /// <summary>Repaints the background on every resize instead of only the newly exposed part.</summary>
    protected bool ResizeRedraw
    {
        get => GetStyle(ControlStyles.ResizeRedraw);
        set => SetStyle(ControlStyles.ResizeRedraw, value);
    }

    /// <summary>Every paint is recorded off screen already, so double buffering only remembers the setting.</summary>
    protected virtual bool DoubleBuffered
    {
        get => GetStyle(ControlStyles.OptimizedDoubleBuffer);
        set => SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, value);
    }

    /// <summary>Records the invalid parts of this control and of every visible control under it.</summary>
    /// <remarks>
    /// The control it starts from is painted even when hidden, as DrawToBitmap
    /// needs; below it, children hidden by their own setting are skipped.
    /// </remarks>
    internal void RecordInvalidTree()
    {
        if (IsDisposingInHierarchy()) return;

        if (!_invalidArea.IsEmpty || _clientPicture is null) RecordClient();

        foreach (var child in ChildrenSnapshot())
        {
            if (child.DesiredVisibility) child.RecordInvalidTree();
        }
    }

    /// <summary>Runs the paint of the invalid part into a new recording that keeps the rest of the old one.</summary>
    private void RecordClient()
    {
        var client = ClientRectangle;
        var invalid = _invalidArea.IsEmpty ? client : _invalidArea;

        if (_clientPicture is null || _pictureLayers >= MaximumPictureLayers)
        {
            invalid = client;
        }

        _invalidArea = Rectangle.Empty;

        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, Math.Max(client.Width, 1), Math.Max(client.Height, 1)));

        var partial = invalid != client && _clientPicture is not null;
        if (partial)
        {
            canvas.Save();
            canvas.ClipRect(ToSkia(invalid), SKClipOperation.Difference);
            canvas.DrawPicture(_clientPicture!);
            canvas.Restore();
        }

        canvas.Save();
        canvas.ClipRect(ToSkia(invalid));

        using (var graphics = Graphics.FromCanvas(canvas))
        {
            using var arguments = new PaintEventArgs(graphics, invalid);
            PaintClient(arguments);
        }

        canvas.Restore();

        _clientPicture?.Dispose();
        _clientPicture = recorder.EndRecording();
        _pictureLayers = partial ? _pictureLayers + 1 : 0;
    }

    /// <summary>Background then foreground, each with the graphics as the paint found it.</summary>
    private void PaintClient(PaintEventArgs arguments)
    {
        var graphics = arguments.Graphics;

        // A control Windows would draw itself: its default procedure paints, and Paint is not raised.
        if (!GetStyle(ControlStyles.UserPaint))
        {
            DefaultPaint(arguments);
            return;
        }

        if (!GetStyle(ControlStyles.Opaque))
        {
            var state = graphics.Save();
            OnPaintBackground(arguments);
            graphics.Restore(state);
        }

        OnPaint(arguments);
    }

    /// <summary>
    /// Draws this control with its frame and its children onto a canvas whose
    /// origin is the control's top-left corner. Children are drawn from the
    /// back of the z-order to the front, clipped to this control's client area.
    /// </summary>
    internal void Compose(SKCanvas canvas, bool includeFrame)
    {
        if (includeFrame) ComposeFrame(canvas);

        var frame = includeFrame ? NonClient : default;
        canvas.Save();
        canvas.Translate(frame.Left, frame.Top);
        canvas.ClipRect(new SKRect(0, 0, _clientWidth, _clientHeight));

        if (_clientPicture is not null) canvas.DrawPicture(_clientPicture);

        var children = ChildrenSnapshot();
        for (var i = children.Length - 1; i >= 0; i--)
        {
            var child = children[i];
            if (!child.DesiredVisibility) continue;

            canvas.Save();
            canvas.Translate(child.Left, child.Top);
            canvas.ClipRect(new SKRect(0, 0, child.Width, child.Height));
            child.Compose(canvas, includeFrame: true);
            canvas.Restore();
        }

        canvas.Restore();
    }

    private void ComposeFrame(SKCanvas canvas)
    {
        var parameters = CreateParams;
        if (NonClient == default) return;

        using var graphics = Graphics.FromCanvas(canvas);
        Skin.Current.DrawNonClientFrame(graphics, Size, parameters.Style, parameters.ExStyle);
    }

    /// <summary>Draws the control, frame and children included, into the bitmap at the target bounds.</summary>
    public void DrawToBitmap(Bitmap bitmap, Rectangle targetBounds)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (targetBounds.Width <= 0 || targetBounds.Height <= 0 || targetBounds.X < 0 || targetBounds.Y < 0)
        {
            throw new ArgumentException("targetBounds must have a positive size and lie inside the bitmap.", nameof(targetBounds));
        }

        CreateControl(ignoreVisible: true);
        RecordInvalidTree();

        using var canvas = new SKCanvas(bitmap.Pixels);
        canvas.Translate(targetBounds.X, targetBounds.Y);
        canvas.ClipRect(new SKRect(0, 0, targetBounds.Width, targetBounds.Height));
        Compose(canvas, includeFrame: !GetTopLevel());
        canvas.Flush();
    }

    private static SKRect ToSkia(Rectangle rectangle) => new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    [ThreadStatic]
    private static Graphics? t_measurementGraphics;

    /// <summary>
    /// A graphics for measuring only, at the screen's 96 dpi, one per thread
    /// and never disposed: measuring happens on every layout and must not
    /// allocate a bitmap each time.
    /// </summary>
    internal static Graphics MeasurementGraphics => t_measurementGraphics ??= Graphics.FromImage(new Bitmap(1, 1));
}
