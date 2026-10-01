using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// The base of every control: a rectangle in its parent, with children, that
/// paints itself and receives input.
/// </summary>
/// <remarks>
/// There is no Win32 window underneath. What WinForms takes from Windows
/// (the frame decided by the window styles, the client area, the z-order, a
/// handle that exists from the moment the control is shown) is kept here
/// with the same rules and the same moments, because applications and
/// third-party controls depend on them. The handle is a number that
/// identifies the control; nothing can be sent to it.
/// </remarks>
[DefaultProperty("Text")]
[DefaultEvent("Click")]
[ToolboxItemFilter("System.Windows.Forms")]
public partial class Control : Component, IWin32Window, IArrangedElement
{
    [Flags]
    private enum States
    {
        None = 0,
        Created = 1 << 0,
        Visible = 1 << 1,
        Enabled = 1 << 2,
        TabStop = 1 << 3,
        Disposing = 1 << 4,
        Disposed = 1 << 5,
        LayoutDeferred = 1 << 6,
        LayoutIsDirty = 1 << 7,
        CausesValidation = 1 << 8,
        TopLevel = 1 << 9,
        HandleCreated = 1 << 10,
        CreatingHandle = 1 << 11,
        RecreatingHandle = 1 << 12,
        ClearLayoutArguments = 1 << 13,
    }

    private States _state;
    private ControlStyles _controlStyle;

    private Control? _parent;
    private string? _text;
    private string? _name;
    private IntPtr _handle;

    public Control()
    {
        _state = States.Visible | States.Enabled | States.TabStop | States.CausesValidation;
        _controlStyle = ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.StandardClick
            | ControlStyles.StandardDoubleClick | ControlStyles.UseTextForAccessibility | ControlStyles.Selectable;

        // The virtual defaults are read here, as WinForms does: a derived
        // control's DefaultSize decides its size before its own constructor runs.
        _layout.Margin = DefaultMargin;
        _layout.Padding = DefaultPadding;
        _layout.MinimumSize = DefaultMinimumSize;
        _layout.MaximumSize = DefaultMaximumSize;

        var size = DefaultSize;
        _width = size.Width;
        _height = size.Height;
        _layout.SpecifiedBounds = new Rectangle(0, 0, _width, _height);

        UpdateBounds(_x, _y, _width, _height);
    }

    public Control(string? text)
        : this(null, text)
    {
    }

    public Control(string? text, int left, int top, int width, int height)
        : this(null, text, left, top, width, height)
    {
    }

    public Control(Control? parent, string? text)
        : this()
    {
        Parent = parent;
        Text = text;
    }

    public Control(Control? parent, string? text, int left, int top, int width, int height)
        : this(parent, text)
    {
        Bounds = new Rectangle(left, top, width, height);
    }

    /// <summary>The control's name, or its site's when it has none of its own (the designer's).</summary>
    [Browsable(false)]
    public string Name
    {
        get => string.IsNullOrEmpty(_name) ? Site?.Name ?? string.Empty : _name;
        set => _name = string.IsNullOrEmpty(value) ? null : value;
    }

    [Localizable(true)]
    [Bindable(true)]
    [AllowNull]
    public virtual string Text
    {
        get => _text ?? string.Empty;
        set
        {
            value ??= string.Empty;
            if (value == Text) return;

            _text = value;
            OnTextChanged(EventArgs.Empty);
        }
    }

    [Localizable(false)]
    [Bindable(true)]
    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag { get; set; }

    [Browsable(false)]
    public bool IsDisposed => GetState(States.Disposed);

    [Browsable(false)]
    public bool Disposing => GetState(States.Disposing);

    [Browsable(false)]
    public bool Created => GetState(States.Created);

    [Browsable(false)]
    public bool IsHandleCreated => GetState(States.HandleCreated);

    [Browsable(false)]
    public bool RecreatingHandle => GetState(States.RecreatingHandle);

    /// <summary>
    /// The control's handle, created on first access as in WinForms. It is
    /// unique and <see cref="FromHandle"/> finds the control from it; it is not
    /// a Win32 window, so passing it to native code does nothing useful.
    /// </summary>
    [Browsable(false)]
    public IntPtr Handle
    {
        get
        {
            if (!IsHandleCreated) CreateHandle();

            return _handle;
        }
    }

    public static Control? FromHandle(IntPtr handle) => HandleTable.Find(handle);

    /// <summary>The control the handle belongs to, or the nearest ancestor that has one.</summary>
    public static Control? FromChildHandle(IntPtr handle) => HandleTable.Find(handle);

    /// <summary>Creates the control's handle and then those of its visible children; raises CreateControl once.</summary>
    public void CreateControl() => CreateControl(ignoreVisible: false);

    internal void CreateControl(bool ignoreVisible)
    {
        if ((_state & (States.Created | States.Disposed)) != 0) return;
        if (!ignoreVisible && !Visible) return;

        _state |= States.Created;

        try
        {
            if (!IsHandleCreated) CreateHandle();

            if (_controls is { } children)
            {
                foreach (var child in children.Snapshot())
                {
                    if (child.Visible) child.CreateControl();
                }
            }

            OnCreateControl();
        }
        catch
        {
            _state &= ~States.Created;
            throw;
        }
    }

    protected virtual void OnCreateControl()
    {
    }

    /// <summary>Gives the control a handle and raises HandleCreated.</summary>
    protected virtual void CreateHandle()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (IsHandleCreated || GetState(States.CreatingHandle)) return;

        _state |= States.CreatingHandle;

        try
        {
            _handle = HandleTable.Register(this);
            _owningThreadId = Environment.CurrentManagedThreadId;
            _state |= States.HandleCreated;

            // Windows reports the client area of the new window: its styles may give it another frame.
            UpdateBounds(_x, _y, _width, _height);
            OnHandleCreated(EventArgs.Empty);
        }
        finally
        {
            _state &= ~States.CreatingHandle;
        }
    }

    /// <summary>Destroys the handles of the children and then this control's; raises HandleDestroyed.</summary>
    protected virtual void DestroyHandle()
    {
        if (!IsHandleCreated) return;

        if (_controls is { } children)
        {
            foreach (var child in children.Snapshot()) child.DestroyHandle();
        }

        OnHandleDestroyed(EventArgs.Empty);

        HandleTable.Unregister(_handle);
        _handle = IntPtr.Zero;
        _state &= ~(States.HandleCreated | States.Created);

        OnHandleDestroyedInternal();
    }

    /// <summary>After the handle is destroyed: a window closes its native window here.</summary>
    internal virtual void OnHandleDestroyedInternal()
    {
    }

    /// <summary>Destroys and recreates the handle, as WinForms does when a style that Windows fixes at creation changes.</summary>
    protected void RecreateHandle()
    {
        if (!IsHandleCreated) return;

        _state |= States.RecreatingHandle;

        try
        {
            var created = Created;
            DestroyHandle();
            CreateHandle();
            if (created) CreateControl(ignoreVisible: true);
        }
        finally
        {
            _state &= ~States.RecreatingHandle;
        }
    }

    protected virtual void OnHandleCreated(EventArgs e) => ((EventHandler?)Events[s_handleCreatedEvent])?.Invoke(this, e);

    protected virtual void OnHandleDestroyed(EventArgs e) => ((EventHandler?)Events[s_handleDestroyedEvent])?.Invoke(this, e);

    [Browsable(false)]
    public event EventHandler? HandleCreated
    {
        add => Events.AddHandler(s_handleCreatedEvent, value);
        remove => Events.RemoveHandler(s_handleCreatedEvent, value);
    }

    [Browsable(false)]
    public event EventHandler? HandleDestroyed
    {
        add => Events.AddHandler(s_handleDestroyedEvent, value);
        remove => Events.RemoveHandler(s_handleDestroyedEvent, value);
    }

    /// <summary>
    /// The window styles the control asks for. Not a Win32 window, but the
    /// border, client area and scroll bar bits decide the control's frame, so
    /// an override that adds WS_BORDER still gets its one-pixel border.
    /// </summary>
    protected virtual CreateParams CreateParams
    {
        get
        {
            var parameters = new CreateParams
            {
                Caption = _text,
                X = _x,
                Y = _y,
                Width = _width,
                Height = _height,
                Style = WindowStyles.ClipChildren | WindowStyles.ClipSiblings,
            };

            if (GetStyle(ControlStyles.ContainerControl)) parameters.ExStyle |= WindowStyles.ExControlParent;
            if (GetState(States.TabStop)) parameters.Style |= WindowStyles.TabStop;
            if (GetState(States.Visible)) parameters.Style |= WindowStyles.Visible;
            if (!GetState(States.Enabled)) parameters.Style |= WindowStyles.Disabled;
            if (_parent is not null && !GetTopLevel()) parameters.Style |= WindowStyles.Child;

            return parameters;
        }
    }

    /// <summary>The frame the control's styles give it, for working out its client area.</summary>
    internal NonClientArea NonClient
    {
        get
        {
            var parameters = CreateParams;

            return NonClientArea.FromStyles(parameters.Style, parameters.ExStyle);
        }
    }

    /// <summary>Applies changed styles: the frame and so the client area may change.</summary>
    protected void UpdateStyles()
    {
        UpdateBounds(_x, _y, _width, _height);
        Invalidate(true);
        OnStyleChanged(EventArgs.Empty);
    }

    protected bool GetStyle(ControlStyles flag) => (_controlStyle & flag) == flag;

    protected void SetStyle(ControlStyles flag, bool value)
    {
        _controlStyle = value ? _controlStyle | flag : _controlStyle & ~flag;
    }

    protected bool GetTopLevel() => GetState(States.TopLevel);

    /// <summary>Makes the control a window of its own; a top-level control cannot have a parent.</summary>
    protected void SetTopLevel(bool value)
    {
        if (value && _parent is not null) throw new ArgumentException("Top-level control cannot be added to a control.", nameof(value));

        SetState(States.TopLevel, value);
    }

    protected override bool CanRaiseEvents => !GetState(States.Disposing) || base.CanRaiseEvents;

    public override ISite? Site
    {
        get => base.Site;
        set
        {
            var oldAmbientFont = Font;
            base.Site = value;
            if (!Font.Equals(oldAmbientFont)) OnFontChanged(EventArgs.Empty);
        }
    }

    /// <summary>Whether this control or any of its ancestors is being designed.</summary>
    [Browsable(false)]
    public bool IsAncestorSiteInDesignMode
    {
        get
        {
            for (var control = this; control is not null; control = control._parent)
            {
                if (control.Site?.DesignMode == true) return true;
            }

            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        // The menu outlives the control: it must not keep a handler bound to it.
        if (disposing) DetachContextMenuStripOnDispose();

        if (GetState(States.Disposed)) return;

        if (disposing)
        {
            _state |= States.Disposing;

            try
            {
                ReleaseBindings();

                // Children are disposed without the parent raising ControlRemoved for each, as in WinForms.
                if (_controls is { } children)
                {
                    foreach (var child in children.Snapshot())
                    {
                        child._parent = null;
                        child.Dispose();
                    }

                    children.ClearWithoutNotification();
                }

                _parent?.Controls.Remove(this);
                DestroyHandle();

                // A popup's own window goes with it.
                if (PopupWindow is { } popupWindow)
                {
                    Internal.Hosting.Popups.Close(this);
                    PopupWindow = null;
                    popupWindow.Close();
                }
            }
            finally
            {
                _state &= ~States.Disposing;
                _state |= States.Disposed;
            }
        }
        else
        {
            // Finalized without Dispose: only the handle number is released, from the finalizer thread.
            if (_handle != IntPtr.Zero) HandleTable.Unregister(_handle);

            _state |= States.Disposed;
        }

        base.Dispose(disposing);
    }

    private bool GetState(States flag) => (_state & flag) != 0;

    /// <summary>Whether CreateHandle is running, when closing a form is refused.</summary>
    internal bool GetCreatingHandle() => GetState(States.CreatingHandle);

    private void SetState(States flag, bool value)
    {
        _state = value ? _state | flag : _state & ~flag;
    }

    /// <summary>Whether this control or one of its ancestors is being disposed: nothing should be laid out or painted.</summary>
    internal bool IsDisposingInHierarchy()
    {
        for (var control = this; control is not null; control = control._parent)
        {
            if (control.GetState(States.Disposing | States.Disposed)) return true;
        }

        return false;
    }

    private static readonly object s_handleCreatedEvent = new();
    private static readonly object s_handleDestroyedEvent = new();
}
