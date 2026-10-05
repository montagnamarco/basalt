using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

/// <summary>
/// What <see cref="NumericUpDown"/> and <see cref="DomainUpDown"/> share: an
/// edit the user can type into, and a pair of spin buttons that call the
/// abstract <see cref="UpButton"/> and <see cref="DownButton"/>.
/// </summary>
/// <remarks>
/// The edit is a real child control (<see cref="UpDownEdit"/>, kept
/// internal); the two buttons are not controls at all, only an area this
/// control paints with <see cref="ControlPaint.DrawScrollButton"/> and hit-tests
/// itself. Focus always lands on the edit, never on this container.
/// </remarks>
public abstract partial class UpDownBase : ContainerControl
{
    private const int ButtonsWidth = 16;
    private const int DefaultControlWidth = 120;
    private const int InitialRepeatDelay = 500;
    private const int RepeatInterval = 100;

    private enum SpinButton
    {
        None,
        Up,
        Down,
    }

    private readonly UpDownEdit _upDownEdit;

    private BorderStyle _borderStyle = BorderStyle.Fixed3D;
    private bool _changingText;
    private bool _userEdit;
    private bool _readOnly;
    private bool _interceptArrowKeys = true;
    private HorizontalAlignment _textAlign = HorizontalAlignment.Left;
    private LeftRightAlignment _upDownAlign = LeftRightAlignment.Right;

    private Rectangle _spinnerBounds;
    private SpinButton _pressedButton = SpinButton.None;
    private Timer? _repeatTimer;

    public UpDownBase()
    {
        SetStyle(ControlStyles.FixedHeight, true);

        _upDownEdit = new UpDownEdit();
        _upDownEdit.TextAlign = _textAlign;
        _upDownEdit.KeyDown += (_, e) => OnTextBoxKeyDown(_upDownEdit, e);
        _upDownEdit.KeyPress += (_, e) => OnTextBoxKeyPress(_upDownEdit, e);
        _upDownEdit.KeyUp += (_, e) => OnKeyUp(e);
        _upDownEdit.LostFocus += (_, e) => OnTextBoxLostFocus(_upDownEdit, e);

        // The focus is the inner edit's, but the application sees it on the up-down control, both ways.
        _upDownEdit.GotFocus += (_, e) => OnGotFocus(e);
        _upDownEdit.Resize += (_, e) => OnTextBoxResize(_upDownEdit, e);
        _upDownEdit.TextChanged += (_, e) => OnTextBoxTextChanged(_upDownEdit, e);

        Controls.Add(_upDownEdit);
    }

    /// <summary>The height one line takes with the frame, exactly as TextBox computes it.</summary>
    [Browsable(false)]
    public int PreferredHeight
    {
        get
        {
            var height = FontHeight;
            if (_borderStyle != BorderStyle.None) height += NonClientArea.BorderWidth * 4 + 3;

            return height;
        }
    }

    protected override Size DefaultSize => new(DefaultControlWidth, PreferredHeight);

    [DefaultValue(BorderStyle.Fixed3D)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (value == _borderStyle) return;

            _borderStyle = value;
            UpdateStyles();
            PerformLayout();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;
            else if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;

            return parameters;
        }
    }

    /// <summary>Turning this on sizes the control to one line now, as TextBox does; it is not kept fixed afterwards.</summary>
    public override bool AutoSize
    {
        get => base.AutoSize;
        set
        {
            if (value == base.AutoSize) return;

            base.AutoSize = value;
            if (value) Height = PreferredHeight;
        }
    }

    /// <summary>
    /// Kept this control's own (so the edit, which inherits an ambient colour
    /// from its parent, has something other than itself to inherit from) and
    /// mirrored onto the edit, which is what actually paints it.
    /// </summary>
    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            if (_upDownEdit is not null) _upDownEdit.BackColor = value;
        }
    }

    public override Color ForeColor
    {
        get => base.ForeColor;
        set
        {
            base.ForeColor = value;
            if (_upDownEdit is not null) _upDownEdit.ForeColor = value;
        }
    }

    [Browsable(false)]
    public override bool Focused => _upDownEdit.Focused;

    [Localizable(true)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _upDownEdit.Text;
        set => _upDownEdit.Text = value;
    }

    [DefaultValue(HorizontalAlignment.Left)]
    [Localizable(true)]
    public HorizontalAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(HorizontalAlignment));
            if (value == _textAlign) return;

            _textAlign = value;
            _upDownEdit.TextAlign = value;
        }
    }

    [DefaultValue(LeftRightAlignment.Right)]
    [Localizable(true)]
    public LeftRightAlignment UpDownAlign
    {
        get => _upDownAlign;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(LeftRightAlignment));
            if (value == _upDownAlign) return;

            _upDownAlign = value;
            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool InterceptArrowKeys
    {
        get => _interceptArrowKeys;
        set => _interceptArrowKeys = value;
    }

    [DefaultValue(false)]
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (value == _readOnly) return;

            _readOnly = value;
            _upDownEdit.ReadOnly = value;
        }
    }

    /// <summary>True between a programmatic change to the edit's text and the TextChanged it raises: tells the two apart from one the user typed.</summary>
    protected bool ChangingText
    {
        get => _changingText;
        set => _changingText = value;
    }

    /// <summary>True once the user has typed in the edit, until the text is parsed or replaced.</summary>
    protected bool UserEdit
    {
        get => _userEdit;
        set => _userEdit = value;
    }

    public void Select(int start, int length) => _upDownEdit.Select(start, length);

    public abstract void UpButton();

    public abstract void DownButton();

    /// <summary>Puts the current value into the edit, guarded so the TextChanged it raises is not mistaken for user input.</summary>
    protected abstract void UpdateEditText();

    /// <summary>Parses the edit's text and applies it, or restores the last valid text; the base implementation does neither.</summary>
    protected virtual void ValidateEditText()
    {
    }

    /// <summary>A hook for subclasses; this base implementation does nothing.</summary>
    protected virtual void OnChanged(object? source, EventArgs e)
    {
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateEditText();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        StopRepeatTimer();
        base.OnHandleDestroyed(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutUpDownControls();
    }

    private void LayoutUpDownControls()
    {
        // Control's own constructor sets the initial bounds before this type's
        // constructor body creates the edit: OnLayout runs virtually that early.
        if (_upDownEdit is null) return;

        var client = ClientRectangle;
        var buttonsWidth = Math.Min(ButtonsWidth, client.Width);
        var editWidth = Math.Max(client.Width - buttonsWidth, 0);

        if (_upDownAlign == LeftRightAlignment.Right)
        {
            _spinnerBounds = new Rectangle(client.Right - buttonsWidth, client.Top, buttonsWidth, client.Height);
            _upDownEdit.Bounds = new Rectangle(client.Left, client.Top, editWidth, client.Height);
        }
        else
        {
            _spinnerBounds = new Rectangle(client.Left, client.Top, buttonsWidth, client.Height);
            _upDownEdit.Bounds = new Rectangle(client.Right - editWidth, client.Top, editWidth, client.Height);
        }

        Invalidate();
    }

    private Rectangle UpButtonBounds()
    {
        var upHeight = _spinnerBounds.Height / 2;

        return new Rectangle(_spinnerBounds.X, _spinnerBounds.Y, _spinnerBounds.Width, upHeight);
    }

    private Rectangle DownButtonBounds()
    {
        var upHeight = _spinnerBounds.Height / 2;

        return new Rectangle(_spinnerBounds.X, _spinnerBounds.Y + upHeight, _spinnerBounds.Width, _spinnerBounds.Height - upHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (PortableVisualStylesEnabled && !HasExplicitAppearanceBackColor)
        {
            var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
            PaintPortableSpinButton(e.Graphics, UpButtonBounds(), pointingUp: true,
                _pressedButton == SpinButton.Up, palette);
            PaintPortableSpinButton(e.Graphics, DownButtonBounds(), pointingUp: false,
                _pressedButton == SpinButton.Down, palette);
            base.OnPaint(e);
            return;
        }

        var upState = ButtonState.Normal;
        var downState = ButtonState.Normal;

        if (!Enabled)
        {
            upState = ButtonState.Inactive;
            downState = ButtonState.Inactive;
        }
        else
        {
            if (_pressedButton == SpinButton.Up) upState = ButtonState.Pushed;
            if (_pressedButton == SpinButton.Down) downState = ButtonState.Pushed;
        }

        ControlPaint.DrawScrollButton(e.Graphics, UpButtonBounds(), ScrollButton.Up, upState);
        ControlPaint.DrawScrollButton(e.Graphics, DownButtonBounds(), ScrollButton.Down, downState);

        base.OnPaint(e);
    }

    private void PaintPortableSpinButton(Graphics graphics, Rectangle bounds, bool pointingUp,
        bool pressed, PortableVisualStylePalette palette)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var faceColor = Enabled && pressed ? palette.PressedFace : palette.Face;
        using var face = new SolidBrush(faceColor);
        using var border = new Pen(palette.Border);
        graphics.FillRectangle(face, bounds);
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        // The fixed-size arrow must not extend into the adjacent button when
        // a host constrains the control to just a few pixels.
        if (bounds.Width < 8 || bounds.Height < 8) return;

        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2 + (pressed ? 1 : 0);
        var points = pointingUp
            ? new[] { new Point(centerX - 3, centerY + 1), new Point(centerX + 3, centerY + 1), new Point(centerX, centerY - 2) }
            : new[] { new Point(centerX - 3, centerY - 1), new Point(centerX + 3, centerY - 1), new Point(centerX, centerY + 2) };
        var arrowColor = !Enabled ? palette.DisabledText
            : HasExplicitAppearanceForeColor ? ForeColor : palette.Text;
        using var arrow = new SolidBrush(arrowColor);
        graphics.FillPolygon(arrow, points);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left) return;

        if (_upDownEdit.CanFocus) _upDownEdit.Focus();

        if (UpButtonBounds().Contains(e.Location)) BeginSpin(SpinButton.Up);
        else if (DownButtonBounds().Contains(e.Location)) BeginSpin(SpinButton.Down);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        EndSpin();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!Enabled) return;

        if (e.Delta > 0) UpButton();
        else if (e.Delta < 0) DownButton();
    }

    private void BeginSpin(SpinButton button)
    {
        _pressedButton = button;
        Invalidate();
        Spin(button);

        _repeatTimer ??= new Timer();
        _repeatTimer.Tick -= OnRepeatTick;
        _repeatTimer.Tick += OnRepeatTick;
        _repeatTimer.Interval = InitialRepeatDelay;
        _repeatTimer.Start();
    }

    private void OnRepeatTick(object? sender, EventArgs e)
    {
        if (_repeatTimer is null || _pressedButton == SpinButton.None) return;
        if (_repeatTimer.Interval != RepeatInterval) _repeatTimer.Interval = RepeatInterval;

        Spin(_pressedButton);
    }

    private void Spin(SpinButton button)
    {
        if (button == SpinButton.Up) UpButton();
        else if (button == SpinButton.Down) DownButton();
    }

    /// <summary>The capture taken away while a button is held (another window, a dialog): the repeating stops.</summary>
    internal override void DefaultCaptureLost() => EndSpin();

    private void EndSpin()
    {
        if (_pressedButton == SpinButton.None) return;

        _pressedButton = SpinButton.None;
        StopRepeatTimer();
        Invalidate();
    }

    private void StopRepeatTimer()
    {
        if (_repeatTimer is null) return;

        _repeatTimer.Stop();
        _repeatTimer.Tick -= OnRepeatTick;
        _repeatTimer.Dispose();
        _repeatTimer = null;
    }

    /// <summary>The edit's KeyDown, forwarded here before it would move the caret: InterceptArrowKeys turns Up and Down into spins.</summary>
    protected virtual void OnTextBoxKeyDown(object? source, KeyEventArgs e)
    {
        OnKeyDown(e);
        if (e.Handled || !_interceptArrowKeys) return;

        switch (e.KeyCode)
        {
            case Keys.Up:
                UpButton();
                e.Handled = true;
                break;

            case Keys.Down:
                DownButton();
                e.Handled = true;
                break;
        }
    }

    /// <summary>The edit's KeyPress, forwarded here; the base implementation only raises this control's own KeyPress.</summary>
    protected virtual void OnTextBoxKeyPress(object? source, KeyPressEventArgs e) => OnKeyPress(e);

    /// <summary>The edit lost focus: a chance to parse and commit what the user typed.</summary>
    protected virtual void OnTextBoxLostFocus(object? source, EventArgs e)
    {
        OnLostFocus(e);
        if (_userEdit) ValidateEditText();
    }

    /// <summary>The edit resized; the base implementation does nothing, since layout already follows this control's own bounds.</summary>
    protected virtual void OnTextBoxResize(object? source, EventArgs e)
    {
    }

    /// <summary>The edit's text changed, by the user unless <see cref="ChangingText"/> says it was this control's own doing.</summary>
    protected virtual void OnTextBoxTextChanged(object? source, EventArgs e)
    {
        if (_changingText) _changingText = false;
        else _userEdit = true;

        OnChanged(source, EventArgs.Empty);
        OnTextChanged(e);
    }
}
