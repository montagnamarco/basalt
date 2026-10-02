using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Automation;

namespace System.Windows.Forms;

/// <summary>
/// A label on a <see cref="StatusStrip"/>: like <see cref="ToolStripLabel"/>,
/// but it may draw a sunken or raised border on any side, and may spring to
/// take up the status bar's leftover width.
/// </summary>
public class ToolStripStatusLabel : ToolStripLabel, IAutomationLiveRegion
{
    private ToolStripStatusLabelBorderSides _borderSides = ToolStripStatusLabelBorderSides.None;
    private Border3DStyle _borderStyle = Border3DStyle.Flat;
    private bool _spring;
    private AutomationLiveSetting _liveSetting = AutomationLiveSetting.Off;

    public ToolStripStatusLabel()
    {
    }

    public ToolStripStatusLabel(string? text)
        : base(text)
    {
    }

    public ToolStripStatusLabel(Image? image)
        : base(image)
    {
    }

    public ToolStripStatusLabel(string? text, Image? image)
        : base(text, image)
    {
    }

    public ToolStripStatusLabel(string? text, Image? image, EventHandler? onClick)
        : base(text, image, false, onClick)
    {
    }

    public ToolStripStatusLabel(string? text, Image? image, EventHandler? onClick, string? name)
        : base(text, image, false, onClick, name)
    {
    }

    [DefaultValue(ToolStripStatusLabelBorderSides.None)]
    public ToolStripStatusLabelBorderSides BorderSides
    {
        get => _borderSides;
        set
        {
            if (value == _borderSides) return;

            _borderSides = value;
            Invalidate();
        }
    }

    [DefaultValue(Border3DStyle.Flat)]
    public Border3DStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (value == _borderStyle) return;

            _borderStyle = value;
            Invalidate();
        }
    }

    /// <summary>Only the designer default differs from the base's: the value itself lives there, which the strip's layout reads.</summary>
    [DefaultValue(ToolStripItemAlignment.Left)]
    public new ToolStripItemAlignment Alignment
    {
        get => base.Alignment;
        set => base.Alignment = value;
    }

    protected override Padding DefaultMargin => new(0, 2, 0, 2);

    /// <summary>Whether, and how urgently, a screen reader should announce this label's text changing on its own.</summary>
    [DefaultValue(AutomationLiveSetting.Off)]
    public AutomationLiveSetting LiveSetting
    {
        get => _liveSetting;
        set => _liveSetting = value;
    }

    /// <summary>Whether this label grows to use whatever width its StatusStrip siblings leave unclaimed.</summary>
    [DefaultValue(false)]
    public bool Spring
    {
        get => _spring;
        set
        {
            if (value == _spring) return;

            _spring = value;
            Owner?.DoLayout(this, nameof(Spring));
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripItemAccessibleObject(this);

    /// <summary>A status label with a live region announces its own text changing, the same as a Label does.</summary>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        if (_liveSetting != AutomationLiveSetting.Off) AccessibilityObject.RaiseLiveRegionChanged();
    }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        var size = base.GetPreferredSize(constrainingSize);

        return new Size(size.Width + BorderInsets().Horizontal, size.Height + BorderInsets().Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_borderSides == ToolStripStatusLabelBorderSides.None) return;

        // Painting runs on the strip's own untranslated Graphics: Bounds (already absolute) is the right rectangle.
        var bounds = Bounds;

        DrawSide(e.Graphics, ToolStripStatusLabelBorderSides.Left, new Rectangle(bounds.Left, bounds.Top, 1, bounds.Height));
        DrawSide(e.Graphics, ToolStripStatusLabelBorderSides.Right, new Rectangle(bounds.Right - 1, bounds.Top, 1, bounds.Height));
        DrawSide(e.Graphics, ToolStripStatusLabelBorderSides.Top, new Rectangle(bounds.Left, bounds.Top, bounds.Width, 1));
        DrawSide(e.Graphics, ToolStripStatusLabelBorderSides.Bottom, new Rectangle(bounds.Left, bounds.Bottom - 1, bounds.Width, 1));
    }

    private void DrawSide(Graphics graphics, ToolStripStatusLabelBorderSides side, Rectangle line)
    {
        if ((_borderSides & side) == 0 || line.Width <= 0 || line.Height <= 0) return;

        var dark = _borderStyle == Border3DStyle.Sunken;
        using var pen = new Pen(dark ? SystemColors.ControlDark : SystemColors.ControlLightLight);
        graphics.DrawLine(pen, line.Left, line.Top, line.Right - 1, line.Bottom - 1);
    }

    private Padding BorderInsets()
    {
        var left = (_borderSides & ToolStripStatusLabelBorderSides.Left) != 0 ? 1 : 0;
        var right = (_borderSides & ToolStripStatusLabelBorderSides.Right) != 0 ? 1 : 0;
        var top = (_borderSides & ToolStripStatusLabelBorderSides.Top) != 0 ? 1 : 0;
        var bottom = (_borderSides & ToolStripStatusLabelBorderSides.Bottom) != 0 ? 1 : 0;

        return new Padding(left, top, right, bottom);
    }
}
