using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Events the hosted <see cref="Control"/> raises that the host item surfaces
/// as its own, so code holding the <see cref="ToolStripItem"/> (not the
/// control itself) can still observe focus, keyboard and validation.
/// </summary>
public partial class ToolStripControlHost
{
    public event EventHandler? Enter
    {
        add => Events.AddHandler(s_enterEvent, value);
        remove => Events.RemoveHandler(s_enterEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public event EventHandler? GotFocus
    {
        add => Events.AddHandler(s_gotFocusEvent, value);
        remove => Events.RemoveHandler(s_gotFocusEvent, value);
    }

    public event KeyEventHandler? KeyDown
    {
        add => Events.AddHandler(s_keyDownEvent, value);
        remove => Events.RemoveHandler(s_keyDownEvent, value);
    }

    public event KeyPressEventHandler? KeyPress
    {
        add => Events.AddHandler(s_keyPressEvent, value);
        remove => Events.RemoveHandler(s_keyPressEvent, value);
    }

    public event KeyEventHandler? KeyUp
    {
        add => Events.AddHandler(s_keyUpEvent, value);
        remove => Events.RemoveHandler(s_keyUpEvent, value);
    }

    public event EventHandler? Leave
    {
        add => Events.AddHandler(s_leaveEvent, value);
        remove => Events.RemoveHandler(s_leaveEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public event EventHandler? LostFocus
    {
        add => Events.AddHandler(s_lostFocusEvent, value);
        remove => Events.RemoveHandler(s_lostFocusEvent, value);
    }

    public event EventHandler? Validated
    {
        add => Events.AddHandler(s_validatedEvent, value);
        remove => Events.RemoveHandler(s_validatedEvent, value);
    }

    public event CancelEventHandler? Validating
    {
        add => Events.AddHandler(s_validatingEvent, value);
        remove => Events.RemoveHandler(s_validatingEvent, value);
    }

    private static readonly object s_enterEvent = new();
    private static readonly object s_gotFocusEvent = new();
    private static readonly object s_keyDownEvent = new();
    private static readonly object s_keyPressEvent = new();
    private static readonly object s_keyUpEvent = new();
    private static readonly object s_leaveEvent = new();
    private static readonly object s_lostFocusEvent = new();
    private static readonly object s_validatedEvent = new();
    private static readonly object s_validatingEvent = new();

    protected virtual void OnEnter(EventArgs e) => ((EventHandler?)Events[s_enterEvent])?.Invoke(this, e);

    protected virtual void OnGotFocus(EventArgs e) => ((EventHandler?)Events[s_gotFocusEvent])?.Invoke(this, e);

    protected virtual void OnKeyDown(KeyEventArgs e) => ((KeyEventHandler?)Events[s_keyDownEvent])?.Invoke(this, e);

    protected virtual void OnKeyPress(KeyPressEventArgs e) => ((KeyPressEventHandler?)Events[s_keyPressEvent])?.Invoke(this, e);

    protected virtual void OnKeyUp(KeyEventArgs e) => ((KeyEventHandler?)Events[s_keyUpEvent])?.Invoke(this, e);

    protected virtual void OnLeave(EventArgs e) => ((EventHandler?)Events[s_leaveEvent])?.Invoke(this, e);

    protected virtual void OnLostFocus(EventArgs e) => ((EventHandler?)Events[s_lostFocusEvent])?.Invoke(this, e);

    protected virtual void OnValidated(EventArgs e) => ((EventHandler?)Events[s_validatedEvent])?.Invoke(this, e);

    protected virtual void OnValidating(CancelEventArgs e) => ((CancelEventHandler?)Events[s_validatingEvent])?.Invoke(this, e);

    /// <summary>Wires the hosted control's own events to the matching On* raiser here; called once, from the constructor.</summary>
    protected virtual void OnSubscribeControlEvents(Control? control)
    {
        if (control is null) return;

        control.Enter += HandleControlEnter;
        control.GotFocus += HandleControlGotFocus;
        control.KeyDown += HandleControlKeyDown;
        control.KeyPress += HandleControlKeyPress;
        control.KeyUp += HandleControlKeyUp;
        control.Leave += HandleControlLeave;
        control.LostFocus += HandleControlLostFocus;
        control.Validated += HandleControlValidated;
        control.Validating += HandleControlValidating;
        control.Resize += HandleControlResize;
    }

    /// <summary>Undoes <see cref="OnSubscribeControlEvents"/>; called once, as the item is disposed.</summary>
    protected virtual void OnUnsubscribeControlEvents(Control? control)
    {
        if (control is null) return;

        control.Enter -= HandleControlEnter;
        control.GotFocus -= HandleControlGotFocus;
        control.KeyDown -= HandleControlKeyDown;
        control.KeyPress -= HandleControlKeyPress;
        control.KeyUp -= HandleControlKeyUp;
        control.Leave -= HandleControlLeave;
        control.LostFocus -= HandleControlLostFocus;
        control.Validated -= HandleControlValidated;
        control.Validating -= HandleControlValidating;
        control.Resize -= HandleControlResize;
    }

    private void HandleControlEnter(object? sender, EventArgs e) => OnEnter(e);

    private void HandleControlGotFocus(object? sender, EventArgs e) => OnGotFocus(e);

    private void HandleControlKeyDown(object? sender, KeyEventArgs e) => OnKeyDown(e);

    private void HandleControlKeyPress(object? sender, KeyPressEventArgs e) => OnKeyPress(e);

    private void HandleControlKeyUp(object? sender, KeyEventArgs e) => OnKeyUp(e);

    private void HandleControlLeave(object? sender, EventArgs e) => OnLeave(e);

    private void HandleControlLostFocus(object? sender, EventArgs e) => OnLostFocus(e);

    private void HandleControlValidated(object? sender, EventArgs e) => OnValidated(e);

    private void HandleControlValidating(object? sender, CancelEventArgs e) => OnValidating(e);

    private void HandleControlResize(object? sender, EventArgs e) => OnHostedControlResize(e);
}
