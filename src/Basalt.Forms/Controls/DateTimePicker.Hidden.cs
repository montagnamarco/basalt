using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Members DateTimePicker re-declares only to keep the public shape
/// Microsoft declares on this type: Padding and the colour/paint events are
/// hidden (this control paints its own field text, not arbitrary children),
/// and RightToLeftLayout is stored and raised only, the same known gap as
/// MonthCalendar.RightToLeftLayout.
/// </summary>
/// <remarks>
/// OnSystemColorsChanged and WndProc are skipped: both override Control
/// members p0-control.txt lists as not yet implemented.
/// </remarks>
public partial class DateTimePicker
{
    protected override AccessibleObject CreateAccessibilityInstance() => new DateTimePickerAccessibleObject(this);

    protected override void CreateHandle() => base.CreateHandle();

    protected override void DestroyHandle() => base.DestroyHandle();

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);

    protected override bool DoubleBuffered
    {
        get => base.DoubleBuffered;
        set => base.DoubleBuffered = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    // Stored and raised only: this control is not mirrored yet (the same known gap as MonthCalendar.RightToLeftLayout).
    private bool _rightToLeftLayout;

    private static readonly object s_rightToLeftLayoutChangedEvent = new();

    [DefaultValue(false)]
    [Localizable(true)]
    public virtual bool RightToLeftLayout
    {
        get => _rightToLeftLayout;
        set
        {
            if (value == _rightToLeftLayout) return;

            _rightToLeftLayout = value;
            OnRightToLeftLayoutChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) => ((EventHandler?)Events[s_rightToLeftLayoutChangedEvent])?.Invoke(this, e);

    public event EventHandler? RightToLeftLayoutChanged
    {
        add => Events.AddHandler(s_rightToLeftLayoutChangedEvent, value);
        remove => Events.RemoveHandler(s_rightToLeftLayoutChangedEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackColorChanged
    {
        add => base.BackColorChanged += value;
        remove => base.BackColorChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageChanged
    {
        add => base.BackgroundImageChanged += value;
        remove => base.BackgroundImageChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageLayoutChanged
    {
        add => base.BackgroundImageLayoutChanged += value;
        remove => base.BackgroundImageLayoutChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? Click
    {
        add => base.Click += value;
        remove => base.Click -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DoubleClick
    {
        add => base.DoubleClick += value;
        remove => base.DoubleClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? ForeColorChanged
    {
        add => base.ForeColorChanged += value;
        remove => base.ForeColorChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseClick
    {
        add => base.MouseClick += value;
        remove => base.MouseClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseDoubleClick
    {
        add => base.MouseDoubleClick += value;
        remove => base.MouseDoubleClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? PaddingChanged
    {
        add => base.PaddingChanged += value;
        remove => base.PaddingChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event PaintEventHandler? Paint
    {
        add => base.Paint += value;
        remove => base.Paint -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }

    /// <summary>The accessible object for the whole control: a drop-down whose default action opens the calendar.</summary>
    public class DateTimePickerAccessibleObject : ControlAccessibleObject
    {
        public DateTimePickerAccessibleObject(DateTimePicker owner)
            : base(owner)
        {
        }

        private new DateTimePicker Owner => (DateTimePicker)base.Owner!;

        public override string? DefaultAction => "Open";

        public override void DoDefaultAction() => Owner.ToggleDropDown();

        public override string? KeyboardShortcut => base.KeyboardShortcut;

        // Overriding only the getter (re-declaring the one ControlAccessibleObject already gives):
        // a "new" member here would hide it from any base-typed reference, which is exactly how a
        // screen reader (and AccessibleObject.Name itself) reads it.
        public override string? Name => base.Name;

        public override AccessibleRole Role => AccessibleRole.DropList;

        public override AccessibleStates State =>
            base.State | (Owner.Focused ? AccessibleStates.Focused : AccessibleStates.None);

        // Overriding only the getter: AccessibleObject.Value's setter is still reachable through a
        // base-typed reference, unlike a "new" member, which would hide this control's formatted
        // date from exactly the callers (a screen reader reading AccessibilityObject.Value) that need it.
        public override string? Value => Owner.Text;
    }
}
