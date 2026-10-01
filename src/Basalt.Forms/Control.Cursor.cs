using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>The mouse pointer shown over the control.</summary>
public partial class Control
{
    private Cursor? _cursor;
    private bool _useWaitCursor;

    /// <summary>The control's own cursor, else the one its kind shows by default, else its parent's.</summary>
    [AmbientValue(null)]
    public virtual Cursor Cursor
    {
        get
        {
            if (_cursor is not null) return _cursor;

            var byDefault = DefaultCursor;
            if (byDefault != Cursors.Default) return byDefault;

            return _parent?.Cursor ?? Cursors.Default;
        }
        set
        {
            var old = Cursor;
            _cursor = value;
            if (old != Cursor) OnCursorChanged(EventArgs.Empty);
        }
    }

    public virtual void ResetCursor() => Cursor = null!;

    internal bool ShouldSerializeCursor() => _cursor is not null;

    protected virtual Cursor DefaultCursor => Cursors.Default;

    /// <summary>Shows the wait cursor over the control and its children.</summary>
    [DefaultValue(false)]
    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    public bool UseWaitCursor
    {
        get => _useWaitCursor;
        set
        {
            if (value == _useWaitCursor) return;

            _useWaitCursor = value;
            foreach (var child in ChildrenSnapshot()) child.UseWaitCursor = value;

            FormsInput.RefreshCursor();
        }
    }

    /// <summary>What the pointer looks like over the control now: a wait cursor wins over the control's own.</summary>
    internal Cursor EffectiveCursor => _useWaitCursor ? Cursors.WaitCursor : Cursor;

    protected virtual void OnCursorChanged(EventArgs e)
    {
        ((EventHandler?)Events[s_cursorEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentCursorChanged(e);

        FormsInput.RefreshCursor();
    }

    protected virtual void OnParentCursorChanged(EventArgs e)
    {
        if (_cursor is null) OnCursorChanged(e);
    }

    public event EventHandler? CursorChanged
    {
        add => Events.AddHandler(s_cursorEvent, value);
        remove => Events.RemoveHandler(s_cursorEvent, value);
    }

    private static readonly object s_cursorEvent = new();
}
