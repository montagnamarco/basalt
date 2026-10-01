using System.Windows.Forms.Internal.Hosting;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// One choice in a group: checking it unchecks its AutoCheck siblings in the
/// same parent, the way a native radio button group does.
/// </summary>
[DefaultProperty("Checked")]
[DefaultEvent("CheckedChanged")]
[DefaultBindingProperty("Checked")]
public class RadioButton : ButtonBase
{
    /// <summary>The classic radio dot glyph is always 12x12, whatever the control's size.</summary>
    private static readonly Size DotSize = new(12, 12);

    private const int GlyphGap = 4;

    private Appearance _appearance = Appearance.Normal;
    private ContentAlignment _checkAlign = ContentAlignment.MiddleLeft;
    private bool _checked;

    public RadioButton()
    {
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        TextAlign = ContentAlignment.MiddleLeft;
        base.TabStop = false;
    }

    protected override Size DefaultSize => new(104, 24);

    private protected override Size GlyphSize => DotSize;

    private protected override int GlyphTextGap => GlyphGap;

    /// <summary>Drawn as a push button that stays down while checked, instead of the usual dot-and-text look.</summary>
    [DefaultValue(Appearance.Normal)]
    [Localizable(true)]
    public Appearance Appearance
    {
        get => _appearance;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Appearance));
            if (_appearance == value) return;

            _appearance = value;
            Invalidate();

            // Microsoft's RadioButton raises AppearanceChanged straight from the
            // setter: unlike CheckBox, it has no protected OnAppearanceChanged hook.
            ((EventHandler?)Events[s_appearanceChangedEvent])?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether a click checks this button and unchecks its AutoCheck siblings by itself.</summary>
    [DefaultValue(true)]
    public bool AutoCheck { get; set; } = true;

    [Localizable(true)]
    [DefaultValue(ContentAlignment.MiddleLeft)]
    public ContentAlignment CheckAlign
    {
        get => _checkAlign;
        set
        {
            if (_checkAlign == value) return;

            _checkAlign = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Checking this button unchecks its AutoCheck siblings in the same
    /// parent; a RadioButton only takes a Tab stop while it is checked, the
    /// rule Windows applies to a radio button group.
    /// </summary>
    [Bindable(true)]
    [SettingsBindable(true)]
    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;

            _checked = value;
            base.TabStop = value;
            Invalidate();

            // Only a radio button that takes part in automatic checking clears its group, as in WinForms.
            if (value && AutoCheck) UncheckAutoCheckSiblings();

            OnCheckedChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Localizable(true)]
    [DefaultValue(ContentAlignment.MiddleLeft)]
    public override ContentAlignment TextAlign
    {
        get => base.TextAlign;
        set => base.TextAlign = value;
    }

    /// <summary>Drawn pushed, like Appearance.Button, while it stays checked.</summary>
    private protected override bool Pushed => base.Pushed || (_appearance == Appearance.Button && Checked);

    public event EventHandler? AppearanceChanged
    {
        add => Events.AddHandler(s_appearanceChangedEvent, value);
        remove => Events.RemoveHandler(s_appearanceChangedEvent, value);
    }

    public event EventHandler? CheckedChanged
    {
        add => Events.AddHandler(s_checkedChangedEvent, value);
        remove => Events.RemoveHandler(s_checkedChangedEvent, value);
    }

    private static readonly object s_appearanceChangedEvent = new();
    private static readonly object s_checkedChangedEvent = new();

    protected virtual void OnCheckedChanged(EventArgs e) => ((EventHandler?)Events[s_checkedChangedEvent])?.Invoke(this, e);

    /// <summary>Unchecks every other RadioButton in the same parent that still opts into the group with AutoCheck.</summary>
    private void UncheckAutoCheckSiblings()
    {
        if (Parent is not { } parent) return;

        for (var i = 0; i < parent.Controls.Count; i++)
        {
            if (parent.Controls[i] is RadioButton sibling && sibling != this && sibling.AutoCheck) sibling.Checked = false;
        }
    }

    /// <summary>Checks this button, as a click or its mnemonic does, when it can be selected.</summary>
    public void PerformClick()
    {
        if (CanSelect || (Visible && Enabled)) OnClick(EventArgs.Empty);
    }

    /// <summary>A click checks this button when AutoCheck allows it, before Click itself fires.</summary>
    protected override void OnClick(EventArgs e)
    {
        if (AutoCheck) Checked = true;

        base.OnClick(e);
    }

    /// <summary>Clicks on release over the control, the same gesture a push button uses.</summary>
    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        var clicked = mevent.Button == MouseButtons.Left && InputPressed && ClientRectangle.Contains(mevent.Location);

        base.OnMouseUp(mevent);

        if (clicked)
        {
            OnClick(mevent);
            OnMouseClick(mevent);
        }
    }

    /// <summary>
    /// Entered without Tab and without the mouse, a radio button selects
    /// itself: that is how the arrows select through a group. WinForms keeps
    /// the rule even when the focus is set from code or when a form opens with
    /// an unchecked radio button as its first control, which then gets
    /// checked; applications rely on that (and work around it) as it is.
    /// </summary>
    protected override void OnEnter(EventArgs e)
    {
        if (MouseButtons == MouseButtons.None && !FormsInput.IsKeyHeld(Keys.Tab) && AutoCheck && !Checked) OnClick(EventArgs.Empty);

        base.OnEnter(e);
        Invalidate();
    }

    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (UseMnemonic && CanSelectForMnemonic() && IsMnemonic(charCode, Text))
        {
            PerformClick();
            return true;
        }

        return base.ProcessMnemonic(charCode);
    }

    private protected override void PaintButton(Graphics graphics)
    {
        if (_appearance == Appearance.Button)
        {
            base.PaintButton(graphics);
            return;
        }

        var bounds = ClientRectangle;
        bounds = new Rectangle(bounds.X + Padding.Left, bounds.Y + Padding.Top, bounds.Width - Padding.Horizontal, bounds.Height - Padding.Vertical);

        var layout = LayoutGlyph(bounds, DotSize, _checkAlign, GlyphGap);

        var state = ButtonState.Normal;
        if (!Enabled) state |= ButtonState.Inactive;
        if (InputPressed) state |= ButtonState.Pushed;
        if (Checked) state |= ButtonState.Checked;

        ControlPaint.DrawRadioButton(graphics, layout.Glyph, state);

        var flags = ContentAlignments.ToTextFormat(TextAlign) | TextFormatFlags.WordBreak;
        if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;

        if (Enabled)
        {
            TextRenderer.DrawText(graphics, Text, Font, layout.Text, ForeColor, flags);
        }
        else
        {
            var engraved = layout.Text;
            engraved.Offset(1, 1);
            TextRenderer.DrawText(graphics, Text, Font, engraved, SystemColors.ControlLightLight, flags);
            TextRenderer.DrawText(graphics, Text, Font, layout.Text, SystemColors.ControlDark, flags);
        }

        if (Focused && ShowFocusCuesInternal) DrawFocusDots(graphics, layout.Text);
    }

    public override string ToString() => $"{base.ToString()}, Text: {Text}, Checked: {Checked}";
}
