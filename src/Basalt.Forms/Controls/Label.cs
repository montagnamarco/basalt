using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Automation;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A line of text that cannot be selected; its mnemonic moves the focus to the next control.</summary>
[DefaultProperty("Text")]
[DefaultEvent("Click")]
public partial class Label : Control, IAutomationLiveRegion
{
    private ContentAlignment _textAlign = ContentAlignment.TopLeft;
    private BorderStyle _borderStyle = BorderStyle.None;
    private bool _useMnemonic = true;
    private bool _autoEllipsis;

    public Label()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable | ControlStyles.FixedHeight, false);
        base.TabStop = false;
    }

    protected override Size DefaultSize => new(100, 23);

    protected override Padding DefaultMargin => new(3, 0, 3, 0);

    /// <summary>Sizes the label to its text as soon as it is set, whatever the parent's layout.</summary>
    [DefaultValue(false)]
    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set
        {
            if (value == base.AutoSize) return;

            base.AutoSize = value;
            AdjustSize();
        }
    }

    [DefaultValue(ContentAlignment.TopLeft)]
    [Localizable(true)]
    public virtual ContentAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            if (value == _textAlign) return;

            _textAlign = value;
            Invalidate();
            OnTextAlignChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(BorderStyle.None)]
    public virtual BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (value == _borderStyle) return;

            _borderStyle = value;
            UpdateStyles();
            AdjustSize();
        }
    }

    [DefaultValue(true)]
    public bool UseMnemonic
    {
        get => _useMnemonic;
        set
        {
            if (value == _useMnemonic) return;

            _useMnemonic = value;
            AdjustSize();
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool AutoEllipsis
    {
        get => _autoEllipsis;
        set
        {
            _autoEllipsis = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Localizable(true)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;
            else if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExStaticEdge;

            return parameters;
        }
    }

    /// <summary>The height of one line of text, with the frame.</summary>
    [Browsable(false)]
    public virtual int PreferredHeight => PreferredSize.Height;

    [Browsable(false)]
    public virtual int PreferredWidth => PreferredSize.Width;

    public override Size GetPreferredSize(Size proposedSize) => base.GetPreferredSize(proposedSize);

    /// <summary>
    /// The text measured as GDI draws it, plus the padding and the frame.
    /// Within a limited width the text wraps, so a layout asking for the
    /// height at a given width gets the height of the wrapped lines.
    /// </summary>
    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        var frame = NonClient;
        var text = string.IsNullOrEmpty(Text) ? " " : Text;
        var extra = new Size(Padding.Horizontal + frame.Horizontal, Padding.Vertical + frame.Vertical);

        var flags = TextFlags();
        var available = new Size(int.MaxValue, int.MaxValue);

        if (proposedSize.Width > 0 && proposedSize.Width < int.MaxValue)
        {
            available.Width = Math.Max(proposedSize.Width - extra.Width, 1);
        }
        else
        {
            flags &= ~TextFormatFlags.WordBreak;
        }

        var measured = TextRenderer.MeasureText(text, Font, available, flags);

        // An empty label is still one line high, but nothing wide.
        if (string.IsNullOrEmpty(Text)) measured.Width = 0;

        return new Size(measured.Width + extra.Width, measured.Height + extra.Height);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        AdjustSize();
        Invalidate();

        // A live region's whole point is announcing its new text without
        // needing focus; Off means this label is not one, so nothing to tell.
        if (_liveSetting != AutomationLiveSetting.Off) AccessibilityObject.RaiseLiveRegionChanged();

        base.OnTextChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        AdjustSize();
        base.OnFontChanged(e);
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        AdjustSize();
        base.OnPaddingChanged(e);
    }

    /// <summary>
    /// An auto-sized label takes its preferred size at once, as WinForms'
    /// Label does, so code that reads its Width after setting Text sees the new one.
    /// </summary>
    private void AdjustSize()
    {
        if (!AutoSize || Dock != DockStyle.None) return;

        var preferred = PreferredSize;
        if (preferred != Size) Size = preferred;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var color = Enabled ? ForeColor : SystemColors.GrayText;
        var bounds = ClientRectangle;
        bounds = new Rectangle(bounds.X + Padding.Left, bounds.Y + Padding.Top, bounds.Width - Padding.Horizontal, bounds.Height - Padding.Vertical);

        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, color, TextFlags());
        base.OnPaint(e);
    }

    private TextFormatFlags TextFlags()
    {
        var flags = ContentAlignments.ToTextFormat(_textAlign) | TextFormatFlags.WordBreak;
        if (!_useMnemonic) flags |= TextFormatFlags.NoPrefix;
        if (_autoEllipsis) flags |= TextFormatFlags.EndEllipsis;

        return flags;
    }

    /// <summary>The label's mnemonic selects the next control in tab order, the one it labels.</summary>
    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (!_useMnemonic || !IsMnemonic(charCode, Text) || !CanSelectForMnemonic()) return false;

        if (Parent is { } parent && parent.SelectNextControl(this, true, false, true, false)) return true;

        return false;
    }

    public override string ToString() => $"{base.ToString()}, Text: {Text}";
}
