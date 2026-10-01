using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A box the user ticks; AutoCheck toggles it on click and, with ThreeState, cycles through Indeterminate too.</summary>
[DefaultProperty("Checked")]
[DefaultEvent("CheckedChanged")]
[DefaultBindingProperty("CheckState")]
public class CheckBox : ButtonBase
{
    /// <summary>The classic check box glyph is always 13x13, whatever the control's size.</summary>
    private static readonly Size BoxSize = new(13, 13);

    private const int GlyphGap = 4;

    private Appearance _appearance = Appearance.Normal;
    private ContentAlignment _checkAlign = ContentAlignment.MiddleLeft;
    private CheckState _checkState = CheckState.Unchecked;

    public CheckBox()
    {
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        TextAlign = ContentAlignment.MiddleLeft;
    }

    protected override Size DefaultSize => new(104, 24);

    private protected override Size GlyphSize => BoxSize;

    private protected override int GlyphTextGap => GlyphGap;

    /// <summary>Drawn as a push button that stays down while checked, instead of the usual box-and-text look.</summary>
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
            OnAppearanceChanged(EventArgs.Empty);
        }
    }

    /// <summary>Whether a click toggles the check state by itself; false means code alone changes it.</summary>
    [DefaultValue(true)]
    public bool AutoCheck { get; set; } = true;

    [Bindable(true)]
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

    [Bindable(true)]
    [DefaultValue(CheckState.Unchecked)]
    [RefreshProperties(RefreshProperties.All)]
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(CheckState));
            if (_checkState == value) return;

            var wasChecked = Checked;
            _checkState = value;
            Invalidate();

            // WinForms raises the coarse event first: CheckedChanged, then CheckStateChanged.
            if (wasChecked != Checked) OnCheckedChanged(EventArgs.Empty);
            OnCheckStateChanged(EventArgs.Empty);
        }
    }

    [Bindable(true)]
    [SettingsBindable(true)]
    [DefaultValue(false)]
    [RefreshProperties(RefreshProperties.All)]
    public bool Checked
    {
        get => _checkState != CheckState.Unchecked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    [Localizable(true)]
    [DefaultValue(ContentAlignment.MiddleLeft)]
    public override ContentAlignment TextAlign
    {
        get => base.TextAlign;
        set => base.TextAlign = value;
    }

    /// <summary>Whether a click cycles through Indeterminate as well as Checked and Unchecked.</summary>
    [DefaultValue(false)]
    public bool ThreeState { get; set; }

    /// <summary>Drawn pushed, like Appearance.Button, while it stays checked.</summary>
    private protected override bool Pushed => base.Pushed || (_appearance == Appearance.Button && Checked);

    public event EventHandler? AppearanceChanged
    {
        add => Events.AddHandler(s_appearanceChangedEvent, value);
        remove => Events.RemoveHandler(s_appearanceChangedEvent, value);
    }

    public event EventHandler? CheckStateChanged
    {
        add => Events.AddHandler(s_checkStateChangedEvent, value);
        remove => Events.RemoveHandler(s_checkStateChangedEvent, value);
    }

    public event EventHandler? CheckedChanged
    {
        add => Events.AddHandler(s_checkedChangedEvent, value);
        remove => Events.RemoveHandler(s_checkedChangedEvent, value);
    }

    private static readonly object s_appearanceChangedEvent = new();
    private static readonly object s_checkStateChangedEvent = new();
    private static readonly object s_checkedChangedEvent = new();

    protected virtual void OnAppearanceChanged(EventArgs e) => ((EventHandler?)Events[s_appearanceChangedEvent])?.Invoke(this, e);

    protected virtual void OnCheckStateChanged(EventArgs e) => ((EventHandler?)Events[s_checkStateChangedEvent])?.Invoke(this, e);

    protected virtual void OnCheckedChanged(EventArgs e) => ((EventHandler?)Events[s_checkedChangedEvent])?.Invoke(this, e);

    /// <summary>A click cycles the check state when AutoCheck allows it, before Click itself fires.</summary>
    protected override void OnClick(EventArgs e)
    {
        if (AutoCheck)
        {
            CheckState = CheckState switch
            {
                CheckState.Unchecked => CheckState.Checked,
                CheckState.Checked => ThreeState ? CheckState.Indeterminate : CheckState.Unchecked,
                _ => CheckState.Unchecked,
            };
        }

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

    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (UseMnemonic && CanSelectForMnemonic() && IsMnemonic(charCode, Text))
        {
            Focus();
            OnClick(EventArgs.Empty);
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

        var layout = LayoutGlyph(bounds, BoxSize, _checkAlign, GlyphGap);

        var state = ButtonState.Normal;
        if (!Enabled) state |= ButtonState.Inactive;
        if (InputPressed) state |= ButtonState.Pushed;

        if (_checkState == CheckState.Indeterminate)
        {
            ControlPaint.DrawMixedCheckBox(graphics, layout.Glyph, state | ButtonState.Checked);
        }
        else
        {
            if (Checked) state |= ButtonState.Checked;
            ControlPaint.DrawCheckBox(graphics, layout.Glyph, state);
        }

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
