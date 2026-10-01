using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// What buttons, check boxes and radio buttons share: text and alignment,
/// the pressed and hot states the mouse and Space key drive, and painting
/// in the classic look.
/// </summary>
public abstract class ButtonBase : Control
{
    private ContentAlignment _textAlign = ContentAlignment.MiddleCenter;
    private FlatStyle _flatStyle = FlatStyle.Standard;
    private bool _useMnemonic = true;
    private bool _isDefault;

    /// <summary>The button is held down: by the mouse while over it, or by the Space key.</summary>
    private protected bool Pushed { get; private set; }

    private bool _mouseDownInside;
    private bool _spaceDown;

    protected ButtonBase()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.Opaque | ControlStyles.ResizeRedraw
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.CacheText | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.Opaque, false);
        SetStyle(ControlStyles.UserMouse | ControlStyles.UserPaint, true);
    }

    protected override Size DefaultSize => new(75, 23);

    [DefaultValue(ContentAlignment.MiddleCenter)]
    [Localizable(true)]
    public virtual ContentAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            _textAlign = value;
            Invalidate();
        }
    }

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle
    {
        get => _flatStyle;
        set
        {
            _flatStyle = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool UseMnemonic
    {
        get => _useMnemonic;
        set
        {
            _useMnemonic = value;
            Invalidate();
        }
    }

    /// <summary>Whether this is the form's default button, drawn with a dark frame.</summary>
    protected bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (value == _isDefault) return;

            _isDefault = value;
            Invalidate();
        }
    }

    [Localizable(true)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _mouseDownInside = true;
            SetPushed(true);
        }

        base.OnMouseDown(e);
    }

    /// <summary>Dragging out of a held button raises it; back in, it is down again.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mouseDownInside && Capture) SetPushed(ClientRectangle.Contains(e.Location));

        base.OnMouseMove(e);
    }

    /// <summary>
    /// The mouse ends the press: a Space key still held no longer clicks on
    /// release, so one gesture with both never clicks twice.
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _mouseDownInside = false;
        _spaceDown = false;
        SetPushed(false);
        base.OnMouseUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _spaceDown = false;
        SetPushed(false);
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    /// <summary>Space pushes the button and its release clicks it, as a Windows button does.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && !e.Alt && !e.Control && !_mouseDownInside)
        {
            _spaceDown = true;
            SetPushed(true);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && _spaceDown)
        {
            _spaceDown = false;
            SetPushed(false);
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    private void SetPushed(bool value)
    {
        if (value == Pushed) return;

        Pushed = value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintButton(e.Graphics);
        base.OnPaint(e);
    }

    /// <summary>The classic push button: raised edges, sunken when pushed, a dark frame when default, the focus dots inside.</summary>
    private protected virtual void PaintButton(Graphics graphics)
    {
        var bounds = ClientRectangle;

        using (var face = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(face, bounds);
        }

        if (_isDefault || Focused && this is IButtonControl)
        {
            Edge(graphics, bounds, SystemColors.WindowFrame, SystemColors.WindowFrame);
            bounds.Inflate(-1, -1);
        }

        if (Pushed)
        {
            Edge(graphics, bounds, SystemColors.ControlDark, SystemColors.ControlDark);
        }
        else if (_flatStyle == FlatStyle.Flat)
        {
            Edge(graphics, bounds, ForeColor, ForeColor);
        }
        else
        {
            Edge(graphics, bounds, SystemColors.ControlLightLight, SystemColors.ControlDarkDark);
            var inner = Rectangle.Inflate(bounds, -1, -1);
            Edge(graphics, inner, SystemColors.ControlLight, SystemColors.ControlDark);
        }

        var textBounds = Rectangle.Inflate(bounds, -3, -3);
        if (Pushed) textBounds.Offset(1, 1);

        var flags = ContentAlignments.ToTextFormat(_textAlign) | TextFormatFlags.WordBreak;
        if (!_useMnemonic) flags |= TextFormatFlags.NoPrefix;

        if (Enabled)
        {
            TextRenderer.DrawText(graphics, Text, Font, textBounds, ForeColor, flags);
        }
        else
        {
            // Disabled text in the classic look: engraved, white offset by one under grey.
            var engraved = textBounds;
            engraved.Offset(1, 1);
            TextRenderer.DrawText(graphics, Text, Font, engraved, SystemColors.ControlLightLight, flags);
            TextRenderer.DrawText(graphics, Text, Font, textBounds, SystemColors.ControlDark, flags);
        }

        if (Focused && ShowFocusCuesInternal)
        {
            var focus = Rectangle.Inflate(bounds, -3, -3);
            DrawFocusDots(graphics, focus);
        }
    }

    private protected bool ShowFocusCuesInternal => true;

    private static void Edge(Graphics graphics, Rectangle bounds, Color topLeft, Color bottomRight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var first = new SolidBrush(topLeft);
        using var second = new SolidBrush(bottomRight);
        graphics.FillRectangle(first, bounds.Left, bounds.Top, bounds.Width - 1, 1);
        graphics.FillRectangle(first, bounds.Left, bounds.Top, 1, bounds.Height - 1);
        graphics.FillRectangle(second, bounds.Left, bounds.Bottom - 1, bounds.Width, 1);
        graphics.FillRectangle(second, bounds.Right - 1, bounds.Top, 1, bounds.Height);
    }

    /// <summary>The dotted focus rectangle: every other pixel, as Win32 DrawFocusRect.</summary>
    private static void DrawFocusDots(Graphics graphics, Rectangle bounds)
    {
        using var brush = new SolidBrush(SystemColors.ControlText);

        for (var x = bounds.Left; x < bounds.Right; x += 2)
        {
            graphics.FillRectangle(brush, x, bounds.Top, 1, 1);
            graphics.FillRectangle(brush, x, bounds.Bottom - 1, 1, 1);
        }

        for (var y = bounds.Top; y < bounds.Bottom; y += 2)
        {
            graphics.FillRectangle(brush, bounds.Left, y, 1, 1);
            graphics.FillRectangle(brush, bounds.Right - 1, y, 1, 1);
        }
    }
}
