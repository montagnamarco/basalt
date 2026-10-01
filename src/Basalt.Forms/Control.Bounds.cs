using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>Position, size and the client area inside the frame.</summary>
public partial class Control
{
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private int _clientWidth;
    private int _clientHeight;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rectangle Bounds
    {
        get => new(_x, _y, _width, _height);
        set => SetBounds(value.X, value.Y, value.Width, value.Height, BoundsSpecified.All);
    }

    [Localizable(true)]
    public Point Location
    {
        get => new(_x, _y);
        set => SetBounds(value.X, value.Y, _width, _height, BoundsSpecified.Location);
    }

    [Localizable(true)]
    public Size Size
    {
        get => new(_width, _height);
        set => SetBounds(_x, _y, value.Width, value.Height, BoundsSpecified.Size);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Left
    {
        get => _x;
        set => SetBounds(value, _y, _width, _height, BoundsSpecified.X);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Top
    {
        get => _y;
        set => SetBounds(_x, value, _width, _height, BoundsSpecified.Y);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Width
    {
        get => _width;
        set => SetBounds(_x, _y, value, _height, BoundsSpecified.Width);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Height
    {
        get => _height;
        set => SetBounds(_x, _y, _width, value, BoundsSpecified.Height);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Right => _x + _width;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Bottom => _y + _height;

    /// <summary>The client area, inside the frame the control's styles give it, at the origin.</summary>
    [Browsable(false)]
    public Rectangle ClientRectangle => new(0, 0, _clientWidth, _clientHeight);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size ClientSize
    {
        get => new(_clientWidth, _clientHeight);
        set => SetClientSizeCore(value.Width, value.Height);
    }

    /// <summary>The area children are arranged in. Containers subtract padding and add scrolling.</summary>
    [Browsable(false)]
    public virtual Rectangle DisplayRectangle => new(0, 0, _clientWidth, _clientHeight);

    protected virtual Size DefaultSize => Size.Empty;

    public void SetBounds(int x, int y, int width, int height)
    {
        if (_x != x || _y != y || _width != width || _height != height)
        {
            SetBoundsCore(x, y, width, height, BoundsSpecified.All);
        }
        else
        {
            // The same bounds set again still make them the user's.
            InitScaling(BoundsSpecified.All);
        }
    }

    /// <summary>Sets the parts of the bounds named by <paramref name="specified"/>, keeping the others.</summary>
    public void SetBounds(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if ((specified & BoundsSpecified.X) == 0) x = _x;
        if ((specified & BoundsSpecified.Y) == 0) y = _y;
        if ((specified & BoundsSpecified.Width) == 0) width = _width;
        if ((specified & BoundsSpecified.Height) == 0) height = _height;

        if (_x != x || _y != y || _width != width || _height != height)
        {
            SetBoundsCore(x, y, width, height, specified);
        }
        else
        {
            InitScaling(specified);
        }
    }

    /// <summary>
    /// Moves and sizes the control. The parent does not lay out until its
    /// engine has measured the new bounds (an anchored control remembers its
    /// distances), so the parent is suspended around the change.
    /// </summary>
    protected virtual void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_x == x && _y == y && _width == width && _height == height) return;

        var parent = _parent;
        parent?.SuspendLayout();

        try
        {
            RememberSpecifiedBounds(x, y, width, height, specified);

            var constrained = ApplyBoundsConstraints(x, y, width, height);

            // A window cannot be smaller than nothing: once it exists, Windows clamps.
            if (IsHandleCreated)
            {
                constrained.Width = Math.Max(constrained.Width, 0);
                constrained.Height = Math.Max(constrained.Height, 0);
            }

            UpdateBounds(constrained.X, constrained.Y, constrained.Width, constrained.Height);
        }
        finally
        {
            InitScaling(specified);

            if (parent is not null)
            {
                parent._layout.PreferredSizeCache = null;
                parent.LayoutEngine.InitLayout(this, specified);
                parent.ResumeLayout(true);
            }
        }
    }

    /// <summary>The bounds the user set, side by side, so that undocking or a layout can return to them.</summary>
    private void RememberSpecifiedBounds(int x, int y, int width, int height, BoundsSpecified specified)
    {
        var bounds = _layout.SpecifiedBounds;

        if ((specified & BoundsSpecified.X) != 0) bounds.X = x;
        if ((specified & BoundsSpecified.Y) != 0) bounds.Y = y;
        if ((specified & BoundsSpecified.Width) != 0) bounds.Width = width;
        if ((specified & BoundsSpecified.Height) != 0) bounds.Height = height;

        _layout.SpecifiedBounds = bounds;
    }

    /// <summary>Sets the size so that the client area is the one given, through the frame the styles describe.</summary>
    protected virtual void SetClientSizeCore(int x, int y)
    {
        Size = SizeFromClientSize(new Size(x, y));
        _clientWidth = x;
        _clientHeight = y;
        OnClientSizeChanged(EventArgs.Empty);
    }

    protected virtual Size SizeFromClientSize(Size clientSize)
    {
        var frame = NonClient;

        return new Size(clientSize.Width + frame.Horizontal, clientSize.Height + frame.Vertical);
    }

    /// <summary>Reads the bounds again: without a window to ask, they are the ones recorded.</summary>
    protected void UpdateBounds() => UpdateBounds(_x, _y, _width, _height);

    /// <summary>Records new bounds, working out the client area from the frame of the control's styles.</summary>
    protected void UpdateBounds(int x, int y, int width, int height)
    {
        var frame = NonClient;
        var clientWidth = Math.Max(width - frame.Horizontal, 0);
        var clientHeight = Math.Max(height - frame.Vertical, 0);

        UpdateBounds(x, y, width, height, clientWidth, clientHeight);
    }

    /// <summary>
    /// Records new bounds and client size and raises what changed: Move and
    /// LocationChanged, then Resize, SizeChanged and ClientSizeChanged, and
    /// asks the parent to lay out.
    /// </summary>
    protected void UpdateBounds(int x, int y, int width, int height, int clientWidth, int clientHeight)
    {
        var moved = _x != x || _y != y;
        var resized = _width != width || _height != height || _clientWidth != clientWidth || _clientHeight != clientHeight;

        var oldBounds = Bounds;

        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _clientWidth = clientWidth;
        _clientHeight = clientHeight;

        if (moved || resized) InvalidateParentArea(oldBounds);

        if (moved) OnLocationChanged(EventArgs.Empty);

        if (resized)
        {
            OnSizeChanged(EventArgs.Empty);
            OnClientSizeChanged(EventArgs.Empty);

            _layout.PreferredSizeCache = null;
            _parent?.DoLayout(this, PropertyNames.Bounds);
        }
    }

    /// <summary>Repaints where the control was and where it is now, in its parent.</summary>
    private void InvalidateParentArea(Rectangle oldBounds)
    {
        if (_parent is not { } parent) return;

        parent.Invalidate(oldBounds, true);
        parent.Invalidate(Bounds, true);
    }

    /// <summary>The bounds after MinimumSize and MaximumSize, where they are set.</summary>
    internal virtual Rectangle ApplyBoundsConstraints(int suggestedX, int suggestedY, int proposedWidth, int proposedHeight)
    {
        var minimum = MinimumSize;
        var maximum = MaximumSize;
        if (minimum.IsEmpty && maximum.IsEmpty) return new Rectangle(suggestedX, suggestedY, proposedWidth, proposedHeight);

        var width = proposedWidth;
        var height = proposedHeight;

        // A zero maximum means no maximum on that side.
        if (maximum.Width > 0) width = Math.Min(width, maximum.Width);
        if (maximum.Height > 0) height = Math.Min(height, maximum.Height);

        width = Math.Max(width, minimum.Width);
        height = Math.Max(height, minimum.Height);

        return new Rectangle(suggestedX, suggestedY, width, height);
    }

    internal Size ApplySizeConstraints(Size proposedSize) => ApplyBoundsConstraints(0, 0, proposedSize.Width, proposedSize.Height).Size;

    protected virtual void OnLocationChanged(EventArgs e)
    {
        OnMove(EventArgs.Empty);
        ((EventHandler?)Events[s_locationEvent])?.Invoke(this, e);
    }

    protected virtual void OnMove(EventArgs e) => ((EventHandler?)Events[s_moveEvent])?.Invoke(this, e);

    /// <summary>Resize first, then SizeChanged, as WinForms raises them.</summary>
    protected virtual void OnSizeChanged(EventArgs e)
    {
        OnResize(EventArgs.Empty);
        ((EventHandler?)Events[s_sizeEvent])?.Invoke(this, e);
    }

    /// <summary>A resize repaints when the control asked for it, and lays out the children.</summary>
    protected virtual void OnResize(EventArgs e)
    {
        if (GetStyle(ControlStyles.ResizeRedraw)) Invalidate();

        DoLayout(this, PropertyNames.Bounds);
        ((EventHandler?)Events[s_resizeEvent])?.Invoke(this, e);
    }

    protected virtual void OnClientSizeChanged(EventArgs e) => ((EventHandler?)Events[s_clientSizeEvent])?.Invoke(this, e);

    public event EventHandler? LocationChanged
    {
        add => Events.AddHandler(s_locationEvent, value);
        remove => Events.RemoveHandler(s_locationEvent, value);
    }

    public event EventHandler? Move
    {
        add => Events.AddHandler(s_moveEvent, value);
        remove => Events.RemoveHandler(s_moveEvent, value);
    }

    public event EventHandler? SizeChanged
    {
        add => Events.AddHandler(s_sizeEvent, value);
        remove => Events.RemoveHandler(s_sizeEvent, value);
    }

    public event EventHandler? Resize
    {
        add => Events.AddHandler(s_resizeEvent, value);
        remove => Events.RemoveHandler(s_resizeEvent, value);
    }

    public event EventHandler? ClientSizeChanged
    {
        add => Events.AddHandler(s_clientSizeEvent, value);
        remove => Events.RemoveHandler(s_clientSizeEvent, value);
    }

    private static readonly object s_locationEvent = new();
    private static readonly object s_moveEvent = new();
    private static readonly object s_sizeEvent = new();
    private static readonly object s_resizeEvent = new();
    private static readonly object s_clientSizeEvent = new();

    /// <summary>Where the client area's origin is on the screen: the frames and positions of every ancestor added up.</summary>
    internal Point ClientOriginOnScreen()
    {
        var frame = NonClient;
        var origin = new Point(_x + frame.Left, _y + frame.Top);

        if (_parent is not null && !GetTopLevel())
        {
            var parentOrigin = _parent.ClientOriginOnScreen();
            origin.Offset(parentOrigin);
        }

        return origin;
    }

    /// <summary>A point in client coordinates as the frame sees it: from the control's top-left corner.</summary>
    internal Point ClientToOuter(Point p)
    {
        var frame = NonClient;

        return new Point(p.X + frame.Left, p.Y + frame.Top);
    }

    /// <summary>Moves the control without it or its parent's layout taking it as a new position: what scrolling a window does to its children.</summary>
    internal void ScrollBy(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;

        UpdateBounds(_x + dx, _y + dy, _width, _height, _clientWidth, _clientHeight);
    }

    public Point PointToScreen(Point p)
    {
        var origin = ClientOriginOnScreen();

        return new Point(p.X + origin.X, p.Y + origin.Y);
    }

    public Point PointToClient(Point p)
    {
        var origin = ClientOriginOnScreen();

        return new Point(p.X - origin.X, p.Y - origin.Y);
    }

    public Rectangle RectangleToScreen(Rectangle r) => new(PointToScreen(r.Location), r.Size);

    public Rectangle RectangleToClient(Rectangle r) => new(PointToClient(r.Location), r.Size);

    Rectangle IArrangedElement.Bounds => Bounds;

    void IArrangedElement.SetBounds(Rectangle bounds, BoundsSpecified specified) =>
        SetBoundsCore(bounds.X, bounds.Y, bounds.Width, bounds.Height, specified);

    Rectangle IArrangedElement.DisplayRectangle => DisplayRectangle;
}
