using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>Layout: suspending and performing it, docking, anchoring, sizing to content.</summary>
public partial class Control
{
    private readonly LayoutSettingsStore _layout = new();
    private int _layoutSuspendCount;
    private LayoutEventArgs? _cachedLayoutArguments;

    /// <summary>Which bounds the user set directly, so that scaling the form scales those and not the ones layout computed.</summary>
    private BoundsSpecified _requiredScaling = BoundsSpecified.All;

    [Browsable(false)]
    public virtual LayoutEngine LayoutEngine => DefaultLayout.Instance;

    /// <summary>
    /// The engine that really arranges the children: the public one, unless a
    /// native control arranges them itself (a TabControl sizes its pages to the
    /// page area without touching their Dock), which Microsoft's surface does
    /// not let it express by overriding LayoutEngine.
    /// </summary>
    internal virtual LayoutEngine LayoutEngineInternal => LayoutEngine;

    LayoutSettingsStore IArrangedElement.LayoutStore => _layout;

    /// <summary>
    /// The control's own visibility, not the inherited one, as in WinForms: a
    /// child of a hidden panel still keeps its place in the panel's layout.
    /// </summary>
    bool IArrangedElement.ParticipatesInLayout => GetState(States.Visible);

    void IArrangedElement.PerformLayout(IArrangedElement affectedElement, string? propertyName) =>
        PerformLayout(new LayoutEventArgs(affectedElement, propertyName));

    internal bool IsLayoutSuspended => _layoutSuspendCount > 0;

    /// <summary>Stops layout until the matching ResumeLayout; layouts asked for meanwhile are done once, then.</summary>
    public void SuspendLayout()
    {
        _layoutSuspendCount++;
    }

    public void ResumeLayout() => ResumeLayout(true);

    /// <summary>
    /// Ends a SuspendLayout. Without performing the layout, the engine still
    /// measures every child again, so anchors set while suspended count from
    /// the bounds the children have now.
    /// </summary>
    public void ResumeLayout(bool performLayout)
    {
        var performed = false;

        if (_layoutSuspendCount > 0)
        {
            if (_layoutSuspendCount == 1)
            {
                // Held at two, so that what OnLayoutResuming does cannot start a layout of its own.
                _layoutSuspendCount++;

                try
                {
                    OnLayoutResuming(performLayout);
                }
                finally
                {
                    _layoutSuspendCount--;
                }
            }

            _layoutSuspendCount--;

            if (_layoutSuspendCount == 0 && GetState(States.LayoutDeferred) && performLayout)
            {
                PerformLayout();
                performed = true;
            }
        }

        if (!performed) SetState(States.ClearLayoutArguments, true);

        if (!performLayout)
        {
            _layout.PreferredSizeCache = null;

            if (_controls is { } children)
            {
                foreach (var child in children.Snapshot())
                {
                    LayoutEngineInternal.InitLayout(child, BoundsSpecified.All);
                    child._layout.PreferredSizeCache = null;
                }
            }
        }
    }

    /// <summary>Called once when the outermost ResumeLayout begins; containers scale their children here.</summary>
    internal virtual void OnLayoutResuming(bool performLayout) => _parent?.OnChildLayoutResuming(this, performLayout);

    internal virtual void OnChildLayoutResuming(Control child, bool performLayout) => _parent?.OnChildLayoutResuming(child, performLayout);

    /// <summary>Lays out with the arguments of the layout that was deferred, if one was.</summary>
    public void PerformLayout()
    {
        if (_cachedLayoutArguments is { } cached)
        {
            _cachedLayoutArguments = null;
            SetState(States.ClearLayoutArguments, false);
            PerformLayout(cached);
        }
        else
        {
            PerformLayout(null, null);
        }
    }

    public void PerformLayout(Control? affectedControl, string? affectedProperty) =>
        PerformLayout(new LayoutEventArgs(affectedControl, affectedProperty));

    /// <summary>
    /// Lays out now, or records that a layout is owed while suspended. The
    /// first cause recorded is the one reported, as in WinForms.
    /// </summary>
    internal void PerformLayout(LayoutEventArgs arguments)
    {
        if (IsDisposingInHierarchy()) return;

        if (_layoutSuspendCount > 0)
        {
            SetState(States.LayoutDeferred, true);

            if (_cachedLayoutArguments is null || GetState(States.ClearLayoutArguments))
            {
                _cachedLayoutArguments = arguments;
                SetState(States.ClearLayoutArguments, false);
            }

            return;
        }

        // Laying out suspends layout, so that moving children does not lay this control out again.
        _layoutSuspendCount = 1;

        try
        {
            OnLayout(arguments);
        }
        finally
        {
            SetState(States.LayoutDeferred | States.LayoutIsDirty, false);
            _layoutSuspendCount = 0;

            // The engine found this control must change size: its parent lays out again, now that it can.
            if (_parent is { } parent && parent.GetState(States.LayoutIsDirty))
            {
                parent.DoLayout(this, PropertyNames.PreferredSize);
            }
        }
    }

    /// <summary>Raises Layout, then arranges the children.</summary>
    protected virtual void OnLayout(LayoutEventArgs levent)
    {
        ((LayoutEventHandler?)Events[s_layoutEvent])?.Invoke(this, levent);

        var parentMustLayOut = LayoutEngineInternal.Layout(this, levent);
        if (parentMustLayOut && _parent is { } parent) parent.SetState(States.LayoutIsDirty, true);
    }

    /// <summary>Lets the parent's engine measure this control once it is placed in it.</summary>
    protected virtual void InitLayout() => LayoutEngineInternal.InitLayout(this, BoundsSpecified.All);

    /// <summary>A layout of this control, caused by <paramref name="cause"/>: what WinForms calls a layout transaction.</summary>
    internal void DoLayout(IArrangedElement cause, string property)
    {
        _layout.PreferredSizeCache = null;
        PerformLayout(new LayoutEventArgs(cause, property));
    }

    public event LayoutEventHandler? Layout
    {
        add => Events.AddHandler(s_layoutEvent, value);
        remove => Events.RemoveHandler(s_layoutEvent, value);
    }

    private static readonly object s_layoutEvent = new();

    [Localizable(true)]
    [DefaultValue(DockStyle.None)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public virtual DockStyle Dock
    {
        get => _layout.Dock;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DockStyle));

            if (value == _layout.Dock) return;

            var wasDocked = _layout.Dock != DockStyle.None;
            SuspendLayout();

            try
            {
                _layout.Dock = value;

                // Docking forgets the anchor; undocking goes back to the bounds the user set.
                if (value != DockStyle.None) _layout.Anchor = LayoutSettingsStore.DefaultAnchor;
                else if (wasDocked) SetBoundsCore(_layout.SpecifiedBounds.X, _layout.SpecifiedBounds.Y, _layout.SpecifiedBounds.Width, _layout.SpecifiedBounds.Height, BoundsSpecified.None);

                _layout.AnchorInfo = null;
                if (value == DockStyle.None && _parent is not null) DefaultLayout.MeasureAnchors(this);
            }
            finally
            {
                ResumeLayout(false);
            }

            _parent?.DoLayout(this, PropertyNames.Dock);
            OnDockChanged(EventArgs.Empty);
        }
    }

    [Localizable(true)]
    [DefaultValue(AnchorStyles.Top | AnchorStyles.Left)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public virtual AnchorStyles Anchor
    {
        get => _layout.Anchor;
        set
        {
            if (value == _layout.Anchor && _layout.Dock == DockStyle.None) return;

            // An anchor replaces docking: the last of the two set wins.
            if (_layout.Dock != DockStyle.None) Dock = DockStyle.None;

            _layout.Anchor = value;
            if (_parent is not null) DefaultLayout.MeasureAnchors(this);

            _parent?.DoLayout(this, PropertyNames.Anchor);
        }
    }

    protected virtual void OnDockChanged(EventArgs e) => ((EventHandler?)Events[s_dockEvent])?.Invoke(this, e);

    public event EventHandler? DockChanged
    {
        add => Events.AddHandler(s_dockEvent, value);
        remove => Events.RemoveHandler(s_dockEvent, value);
    }

    private static readonly object s_dockEvent = new();

    [Localizable(true)]
    [DefaultValue(false)]
    [RefreshProperties(RefreshProperties.All)]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual bool AutoSize
    {
        get => _layout.AutoSize;
        set
        {
            if (value == _layout.AutoSize) return;

            _layout.AutoSize = value;

            if (_parent is not null)
            {
                // Turned on, the control grows to its content at the next layout of its parent.
                if (value) _parent.LayoutEngineInternal.InitLayout(this, BoundsSpecified.Size);

                _parent.DoLayout(this, PropertyNames.AutoSize);
            }

            OnAutoSizeChanged(EventArgs.Empty);
        }
    }

    protected AutoSizeMode GetAutoSizeMode() => _layout.AutoSizeMode;

    protected void SetAutoSizeMode(AutoSizeMode mode)
    {
        if (mode == _layout.AutoSizeMode) return;

        _layout.AutoSizeMode = mode;
        if (_layout.AutoSize) _parent?.DoLayout(this, PropertyNames.AutoSize);
    }

    protected virtual void OnAutoSizeChanged(EventArgs e) => ((EventHandler?)Events[s_autoSizeEvent])?.Invoke(this, e);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public event EventHandler? AutoSizeChanged
    {
        add => Events.AddHandler(s_autoSizeEvent, value);
        remove => Events.RemoveHandler(s_autoSizeEvent, value);
    }

    private static readonly object s_autoSizeEvent = new();

    [Localizable(true)]
    public Padding Margin
    {
        get => _layout.Margin;
        set
        {
            // Negative sides mean nothing to a layout.
            value = ClampToZero(value);
            if (value == _layout.Margin) return;

            _layout.Margin = value;
            _parent?.DoLayout(this, PropertyNames.Margin);
            OnMarginChanged(EventArgs.Empty);
        }
    }

    [Localizable(true)]
    public Padding Padding
    {
        get => _layout.Padding;
        set
        {
            value = ClampToZero(value);
            if (value == _layout.Padding) return;

            _layout.Padding = value;

            // The children move inside the new padding, and a control that sizes itself changes size.
            DoLayout(this, PropertyNames.Padding);
            if (_layout.AutoSize) _parent?.DoLayout(this, PropertyNames.Padding);

            OnPaddingChanged(EventArgs.Empty);
        }
    }

    private static Padding ClampToZero(Padding padding)
    {
        if (padding.Left >= 0 && padding.Top >= 0 && padding.Right >= 0 && padding.Bottom >= 0) return padding;

        // Four equal sides stay a padding set "all at once", as the designer writes it.
        if (padding.Left == padding.Top && padding.Left == padding.Right && padding.Left == padding.Bottom) return new Padding(0);

        return new Padding(Math.Max(padding.Left, 0), Math.Max(padding.Top, 0), Math.Max(padding.Right, 0), Math.Max(padding.Bottom, 0));
    }

    protected virtual Padding DefaultMargin => new(3);

    protected virtual Padding DefaultPadding => Padding.Empty;

    protected virtual Size DefaultMinimumSize => Size.Empty;

    protected virtual Size DefaultMaximumSize => Size.Empty;

    protected virtual void OnMarginChanged(EventArgs e) => ((EventHandler?)Events[s_marginEvent])?.Invoke(this, e);

    protected virtual void OnPaddingChanged(EventArgs e)
    {
        if (GetStyle(ControlStyles.ResizeRedraw)) Invalidate();

        ((EventHandler?)Events[s_paddingEvent])?.Invoke(this, e);
    }

    public event EventHandler? MarginChanged
    {
        add => Events.AddHandler(s_marginEvent, value);
        remove => Events.RemoveHandler(s_marginEvent, value);
    }

    public event EventHandler? PaddingChanged
    {
        add => Events.AddHandler(s_paddingEvent, value);
        remove => Events.RemoveHandler(s_paddingEvent, value);
    }

    private static readonly object s_marginEvent = new();
    private static readonly object s_paddingEvent = new();

    /// <summary>The smallest size the control takes; raising it above the maximum raises the maximum too.</summary>
    [Localizable(true)]
    public virtual Size MinimumSize
    {
        get => _layout.MinimumSize;
        set
        {
            if (value == _layout.MinimumSize) return;

            _layout.MinimumSize = value;

            var maximum = _layout.MaximumSize;
            if (maximum.Width > 0 && value.Width > maximum.Width) maximum.Width = value.Width;
            if (maximum.Height > 0 && value.Height > maximum.Height) maximum.Height = value.Height;
            _layout.MaximumSize = maximum;

            if (_width < value.Width || _height < value.Height) Size = new Size(Math.Max(_width, value.Width), Math.Max(_height, value.Height));

            _parent?.DoLayout(this, PropertyNames.MinimumSize);
        }
    }

    /// <summary>The largest size the control takes, zero meaning none; lowering it below the minimum lowers the minimum too.</summary>
    [Localizable(true)]
    public virtual Size MaximumSize
    {
        get => _layout.MaximumSize;
        set
        {
            if (value == _layout.MaximumSize) return;

            _layout.MaximumSize = value;

            var minimum = _layout.MinimumSize;
            if (value.Width > 0 && minimum.Width > value.Width) minimum.Width = value.Width;
            if (value.Height > 0 && minimum.Height > value.Height) minimum.Height = value.Height;
            _layout.MinimumSize = minimum;

            var width = value.Width > 0 ? Math.Min(_width, value.Width) : _width;
            var height = value.Height > 0 ? Math.Min(_height, value.Height) : _height;
            if (width != _width || height != _height) Size = new Size(width, height);

            _parent?.DoLayout(this, PropertyNames.MaximumSize);
        }
    }

    /// <summary>The size the control would like within <paramref name="proposedSize"/>, zero meaning no limit.</summary>
    public virtual Size GetPreferredSize(Size proposedSize)
    {
        if (GetState(States.Disposing | States.Disposed)) return _layout.PreferredSizeCache ?? Size;

        var proposed = new Size(
            proposedSize.Width == 0 ? int.MaxValue : proposedSize.Width,
            proposedSize.Height == 0 ? int.MaxValue : proposedSize.Height);
        proposed = ApplySizeConstraints(proposed);

        var unconstrained = proposed.Width == int.MaxValue && proposed.Height == int.MaxValue;
        if (unconstrained && _layout.PreferredSizeCache is { } cached) return cached;

        var preferred = ApplySizeConstraints(GetPreferredSizeCore(proposed));
        if (unconstrained) _layout.PreferredSizeCache = preferred;

        return preferred;
    }

    /// <summary>What a control without content of its own prefers: the size the user gave it.</summary>
    internal virtual Size GetPreferredSizeCore(Size proposedSize) => _layout.SpecifiedBounds.Size;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size PreferredSize => GetPreferredSize(Size.Empty);

    /// <summary>Remembers which bounds the user set, for the next automatic scaling.</summary>
    private void InitScaling(BoundsSpecified specified) => _requiredScaling |= specified & BoundsSpecified.All;
}
