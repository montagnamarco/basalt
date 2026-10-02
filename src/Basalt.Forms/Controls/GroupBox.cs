using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// A frame with a caption that groups related controls: the classic etched
/// border, cut at the top left for the caption to sit over.
/// </summary>
[DefaultEvent("Enter")]
[DefaultProperty("Text")]
public partial class GroupBox : Control
{
    /// <summary>How far the caption starts from the frame's left edge, the value WinForms draws it at.</summary>
    private const int CaptionOffset = 8;

    private FlatStyle _flatStyle = FlatStyle.Standard;
    private bool _useCompatibleTextRendering;

    public GroupBox()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.ContainerControl, true);
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer,
            true);
        base.TabStop = false;
    }

    protected override Size DefaultSize => new(200, 100);

    /// <summary>The default thickness of the frame on every side; the top one is replaced by the font's height when drawing.</summary>
    protected override Padding DefaultPadding => new(3);

    [Localizable(true)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    [DefaultValue(AutoSizeMode.GrowOnly)]
    [Localizable(true)]
    public AutoSizeMode AutoSizeMode
    {
        get => GetAutoSizeMode();
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AutoSizeMode));

            SetAutoSizeMode(value);
        }
    }

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle
    {
        get => _flatStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FlatStyle));

            if (value == _flatStyle) return;

            _flatStyle = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    /// <summary>Kept for source compatibility; the one skin this renders in never needed GDI+ text measuring.</summary>
    [DefaultValue(false)]
    public bool UseCompatibleTextRendering
    {
        get => _useCompatibleTextRendering;
        set
        {
            if (value == _useCompatibleTextRendering) return;

            _useCompatibleTextRendering = value;
            Invalidate();
        }
    }

    /// <summary>
    /// The area children are arranged in: inset 3 pixels on the left, right
    /// and bottom, and by the caption's line height on top, as WinForms'
    /// GroupBox measures it.
    /// </summary>
    public override Rectangle DisplayRectangle
    {
        get
        {
            var padding = Padding;
            var client = ClientRectangle;
            var top = Math.Max(FontHeight, padding.Top);

            return new Rectangle(
                client.X + padding.Left,
                client.Y + top,
                Math.Max(client.Width - padding.Horizontal, 0),
                Math.Max(client.Height - top - padding.Bottom, 0));
        }
    }

    /// <summary>
    /// What an auto-sized GroupBox needs to show its children: the same
    /// extent the default layout would give a panel, corrected for the
    /// caption's line height replacing the stored top padding.
    /// </summary>
    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        var preferred = DefaultLayout.PreferredSize(this, proposedSize);
        var extraTop = Math.Max(FontHeight - Padding.Top, 0);

        return new Size(preferred.Width, preferred.Height + extraTop);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        // The caption's line height moves the frame and so the children: the parent must measure this control again too.
        if (AutoSize) Parent?.DoLayout(this, PropertyNames.Font);
    }

    /// <summary>A GroupBox cannot take the focus itself; its mnemonic selects the first control after it instead.</summary>
    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (!CanSelectForMnemonic() || !IsMnemonic(charCode, Text)) return false;

        return SelectNextControl(null, true, true, true, false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var text = Text;
        var textSize = string.IsNullOrEmpty(text) ? Size.Empty : TextRenderer.MeasureText(text, Font);

        // The etched frame starts low enough that the caption's text sits astride its top edge.
        var frameTop = textSize.Height / 2;
        var frame = new Rectangle(0, frameTop, ClientSize.Width, Math.Max(ClientSize.Height - frameTop, 0));

        // The classic Windows 2000 look draws every GroupBox with the same etched frame, whatever FlatStyle asks for.
        ControlPaint.DrawBorder3D(graphics, frame, Border3DStyle.Etched);

        if (!string.IsNullOrEmpty(text))
        {
            var textBounds = new Rectangle(CaptionOffset, 0, textSize.Width, textSize.Height);

            // The border line drawn under the caption is covered by the control's own background before the text is drawn over it.
            using var backBrush = new SolidBrush(BackColor.A == 255 ? BackColor : SystemColors.Control);
            graphics.FillRectangle(backBrush, textBounds);

            var color = Enabled ? ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(graphics, text, Font, textBounds, color, TextFormatFlags.Default);
        }

        base.OnPaint(e);
    }

    public override string ToString() => $"{base.ToString()}, Text: {Text}";
}
