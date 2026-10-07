using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Tab order, focus and validation.
/// </summary>
/// <remarks>
/// Each window keeps which of its controls has the focus, as Windows does.
/// Moving the focus follows WinForms' two paths: Focus() moves the window's
/// focus first (LostFocus, GotFocus come around the container's Leave,
/// Validating, Validated and Enter), while Select() and the Tab key let the
/// container activate the control first and then move the focus.
/// </remarks>
public partial class Control
{
    private int _tabIndex = -1;

    /// <summary>For a top-level control: which control in its window has the keyboard focus.</summary>
    private Control? _windowFocus;

    /// <summary>For a top-level control: the control that last received Enter, so the next change knows what to leave.</summary>
    private Control? _enteredControl;

    /// <summary>The control's place in its container's tab order; one added without one goes after the others.</summary>
    [Localizable(true)]
    [Category("Behavior")]
    public int TabIndex
    {
        get => _tabIndex == -1 ? 0 : _tabIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (_tabIndex == value) return;

            _tabIndex = value;
            OnTabIndexChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    [Category("Behavior")]
    public bool TabStop
    {
        get => GetState(States.TabStop);
        set
        {
            if (TabStop == value) return;

            SetState(States.TabStop, value);
            OnTabStopChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    [Category("Behavior")]
    public bool CausesValidation
    {
        get => GetState(States.CausesValidation);
        set
        {
            if (CausesValidation == value) return;

            SetState(States.CausesValidation, value);
            OnCausesValidationChanged(EventArgs.Empty);
        }
    }

    /// <summary>Whether this control has the keyboard focus of its window.</summary>
    [Browsable(false)]
    public virtual bool Focused => IsHandleCreated && TopLevelControl?._windowFocus == this;

    /// <summary>Whether this control or one of its children has the focus.</summary>
    [Browsable(false)]
    public bool ContainsFocus
    {
        get
        {
            var focused = TopLevelControl?._windowFocus;

            return focused is not null && (focused == this || Contains(focused));
        }
    }

    /// <summary>Whether the control can take the focus now: created, visible and enabled.</summary>
    [Browsable(false)]
    public bool CanFocus => IsHandleCreated && Visible && Enabled;

    /// <summary>Whether the control can be selected: selectable, and it and every ancestor visible and enabled.</summary>
    [Browsable(false)]
    public bool CanSelect
    {
        get
        {
            if (!GetStyle(ControlStyles.Selectable)) return false;

            for (var control = this; control is not null; control = control._parent)
            {
                if (!control.GetState(States.Enabled) || !control.GetState(States.Visible)) return false;
            }

            return true;
        }
    }

    /// <summary>Gives the control the focus, as a click does; false when it could not take it.</summary>
    public bool Focus() => FocusInternal();

    internal bool FocusInternal()
    {
        if (CanFocus) SetWindowFocus();

        if (Focused && _parent is not null && GetContainerControl() is ContainerControl container)
        {
            container.SetActiveControl(this);
        }

        return Focused;
    }

    /// <summary>
    /// Moves the window's focus here, raising what Windows' focus messages
    /// raise: LostFocus on the old control, then the container's activation
    /// (Leave, validation, Enter), then GotFocus.
    /// </summary>
    internal void SetWindowFocus()
    {
        if (TopLevelControl is not { } window) return;

        var old = window._windowFocus;
        if (old == this) return;

        window._windowFocus = this;

        if (old is not null)
        {
            old.DefaultLostFocus();
            old.OnLostFocus(EventArgs.Empty);
        }

        if (_parent is not null && GetContainerControl() is ContainerControl container && !container.ActivateControl(this))
        {
            return;
        }

        DefaultGotFocus();
        OnGotFocus(EventArgs.Empty);
    }

    /// <summary>Selects the control, as the Tab key does: its container activates it, then it takes the focus.</summary>
    public void Select() => Select(false, false);

    protected virtual void Select(bool directed, bool forward)
    {
        if (GetContainerControl() is { } container) container.ActiveControl = this;
    }

    internal void SelectInternal(bool directed, bool forward) => Select(directed, forward);

    /// <summary>The nearest ancestor that manages focus for this control: a container control, usually the form.</summary>
    public IContainerControl? GetContainerControl()
    {
        for (var control = _parent; control is not null; control = control._parent)
        {
            if (control.GetStyle(ControlStyles.ContainerControl) && control is IContainerControl container) return container;
        }

        return null;
    }

    /// <summary>Whether the control manages the focus of its own children, so tabbing does not descend into it.</summary>
    private static bool IsFocusManagingContainer(Control control) => control.GetStyle(ControlStyles.ContainerControl) && control is IContainerControl;

    /// <summary>
    /// The next control in tab order after <paramref name="ctl"/>, among this
    /// control's descendants, going into children before siblings; null at the end.
    /// </summary>
    public Control? GetNextControl(Control? ctl, bool forward)
    {
        if (ctl is null || !Contains(ctl)) ctl = this;

        if (forward)
        {
            if (ctl.HasChildren && (ctl == this || !IsFocusManagingContainer(ctl)))
            {
                return ctl.ChildrenInTabOrder().FirstOrDefault();
            }

            while (ctl != this)
            {
                var siblings = ctl._parent!.ChildrenInTabOrder();
                var index = Array.IndexOf(siblings, ctl);
                if (index + 1 < siblings.Length) return siblings[index + 1];

                ctl = ctl._parent!;
            }

            return null;
        }

        if (ctl != this)
        {
            var siblings = ctl._parent!.ChildrenInTabOrder();
            var index = Array.IndexOf(siblings, ctl);

            if (index > 0)
            {
                ctl = siblings[index - 1];
            }
            else
            {
                return ctl._parent == this ? null : ctl._parent;
            }
        }

        // Backwards, a control's deepest last child comes before it.
        while (ctl.HasChildren && (ctl == this || !IsFocusManagingContainer(ctl)))
        {
            ctl = ctl.ChildrenInTabOrder()[^1];
        }

        return ctl == this ? null : ctl;
    }

    /// <summary>The children ordered by tab index, ties kept in the order they were added.</summary>
    private Control[] ChildrenInTabOrder()
    {
        var children = ChildrenSnapshot();

        return [.. children.Select((child, index) => (child, index)).OrderBy(pair => pair.child.TabIndex).ThenBy(pair => pair.index).Select(pair => pair.child)];
    }

    /// <summary>
    /// Selects the next selectable control after <paramref name="ctl"/>;
    /// with <paramref name="wrap"/>, past the last one comes the first.
    /// </summary>
    public bool SelectNextControl(Control? ctl, bool forward, bool tabStopOnly, bool nested, bool wrap)
    {
        if (ctl is not null && (!Contains(ctl) || (!nested && ctl._parent != this))) ctl = null;

        var start = ctl;
        var wrapped = false;

        do
        {
            ctl = GetNextControl(ctl, forward);

            if (ctl is null)
            {
                if (!wrap || wrapped) return false;

                wrapped = true;
            }
            else if (ctl.CanSelect && (!tabStopOnly || ctl.TabStop) && (nested || ctl._parent == this))
            {
                ctl.Select(true, forward);
                return true;
            }
        }
        while (ctl != start);

        return false;
    }

    /// <summary>Before a control is hidden or disabled: the focus moves on, as Windows would take it away.</summary>
    private void SelectNextIfFocused()
    {
        if (!ContainsFocus || _parent is null) return;

        if (GetContainerControl() is Control container)
        {
            container.SelectNextControl(this, true, true, true, true);
        }
    }

    /// <summary>
    /// Raises Leave (and validation) on the controls being left and Enter on
    /// the controls being entered, from the old control and the new one up to
    /// their common ancestor; false when validation kept the focus where it was.
    /// </summary>
    internal bool MoveEnteredControl(Control entering)
    {
        var window = TopLevelControl ?? this;
        var leaving = window._enteredControl;
        if (leaving == entering) return true;

        var leavingChain = new List<Control>();
        for (var control = leaving; control is not null && control != window && !control.ContainsOrIs(entering); control = control._parent)
        {
            leavingChain.Add(control);
        }

        foreach (var control in leavingChain) control.OnLeave(EventArgs.Empty);

        if (entering.CausesValidation)
        {
            foreach (var control in leavingChain)
            {
                if (!control.ValidateThis()) return false;
            }
        }

        var enteringChain = new List<Control>();
        for (var control = entering; control is not null && control != window && !control.ContainsOrIs(leaving); control = control._parent)
        {
            enteringChain.Add(control);
        }

        window._enteredControl = entering;
        for (var i = enteringChain.Count - 1; i >= 0; i--) enteringChain[i].OnEnter(EventArgs.Empty);

        return true;
    }

    /// <summary>Whether a mnemonic may reach the control: it and its ancestors visible and enabled.</summary>
    internal bool CanSelectForMnemonic()
    {
        for (var control = this; control is not null; control = control._parent)
        {
            if (!control.GetState(States.Enabled) || !control.GetState(States.Visible)) return false;
        }

        return true;
    }

    private bool ContainsOrIs(Control? other) => other is not null && (other == this || Contains(other));

    /// <summary>Raises Validating, and Validated when not cancelled; false when cancelled.</summary>
    internal bool ValidateThis()
    {
        var validating = new CancelEventArgs();
        OnValidating(validating);
        if (validating.Cancel) return false;

        OnValidated(EventArgs.Empty);

        return true;
    }

    protected virtual void OnTabIndexChanged(EventArgs e) => ((EventHandler?)Events[s_tabIndexEvent])?.Invoke(this, e);

    protected virtual void OnTabStopChanged(EventArgs e) => ((EventHandler?)Events[s_tabStopEvent])?.Invoke(this, e);

    protected virtual void OnCausesValidationChanged(EventArgs e) => ((EventHandler?)Events[s_causesValidationEvent])?.Invoke(this, e);

    protected virtual void OnGotFocus(EventArgs e) => ((EventHandler?)Events[s_gotFocusEvent])?.Invoke(this, e);

    protected virtual void OnLostFocus(EventArgs e) => ((EventHandler?)Events[s_lostFocusEvent])?.Invoke(this, e);

    protected virtual void OnEnter(EventArgs e) => ((EventHandler?)Events[s_enterEvent])?.Invoke(this, e);

    protected virtual void OnLeave(EventArgs e) => ((EventHandler?)Events[s_leaveEvent])?.Invoke(this, e);

    protected virtual void OnValidating(CancelEventArgs e) => ((CancelEventHandler?)Events[s_validatingEvent])?.Invoke(this, e);

    protected virtual void OnValidated(EventArgs e) => ((EventHandler?)Events[s_validatedEvent])?.Invoke(this, e);

    protected void InvokeGotFocus(Control? toInvoke, EventArgs e) => toInvoke?.OnGotFocus(e);

    protected void InvokeLostFocus(Control? toInvoke, EventArgs e) => toInvoke?.OnLostFocus(e);

    [Category("Property Changed")]
    public event EventHandler? TabIndexChanged
    {
        add => Events.AddHandler(s_tabIndexEvent, value);
        remove => Events.RemoveHandler(s_tabIndexEvent, value);
    }

    [Category("Property Changed")]
    public event EventHandler? TabStopChanged
    {
        add => Events.AddHandler(s_tabStopEvent, value);
        remove => Events.RemoveHandler(s_tabStopEvent, value);
    }

    [Category("Property Changed")]
    public event EventHandler? CausesValidationChanged
    {
        add => Events.AddHandler(s_causesValidationEvent, value);
        remove => Events.RemoveHandler(s_causesValidationEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [Category("Focus")]
    public event EventHandler? GotFocus
    {
        add => Events.AddHandler(s_gotFocusEvent, value);
        remove => Events.RemoveHandler(s_gotFocusEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [Category("Focus")]
    public event EventHandler? LostFocus
    {
        add => Events.AddHandler(s_lostFocusEvent, value);
        remove => Events.RemoveHandler(s_lostFocusEvent, value);
    }

    [Category("Focus")]
    public event EventHandler? Enter
    {
        add => Events.AddHandler(s_enterEvent, value);
        remove => Events.RemoveHandler(s_enterEvent, value);
    }

    [Category("Focus")]
    public event EventHandler? Leave
    {
        add => Events.AddHandler(s_leaveEvent, value);
        remove => Events.RemoveHandler(s_leaveEvent, value);
    }

    [Category("Focus")]
    public event CancelEventHandler? Validating
    {
        add => Events.AddHandler(s_validatingEvent, value);
        remove => Events.RemoveHandler(s_validatingEvent, value);
    }

    [Category("Focus")]
    public event EventHandler? Validated
    {
        add => Events.AddHandler(s_validatedEvent, value);
        remove => Events.RemoveHandler(s_validatedEvent, value);
    }

    private static readonly object s_tabIndexEvent = new();
    private static readonly object s_tabStopEvent = new();
    private static readonly object s_causesValidationEvent = new();
    private static readonly object s_gotFocusEvent = new();
    private static readonly object s_lostFocusEvent = new();
    private static readonly object s_enterEvent = new();
    private static readonly object s_leaveEvent = new();
    private static readonly object s_validatingEvent = new();
    private static readonly object s_validatedEvent = new();

    /// <summary>For a top-level control: the control with the focus in its window.</summary>
    internal Control? WindowFocus => _windowFocus;

    /// <summary>For a top-level control: forgets a control leaving the window, so nothing points at it.</summary>
    internal void ForgetFocusIn(Control removed)
    {
        if (_windowFocus is { } focused && removed.ContainsOrIs(focused)) _windowFocus = null;
        if (_enteredControl is { } entered && removed.ContainsOrIs(entered)) _enteredControl = null;
    }
}
