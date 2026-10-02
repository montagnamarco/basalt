using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>Plain text (and/or an image) on a <see cref="ToolStrip"/>, not a button: it never looks pressed.</summary>
public class ToolStripLabel : ToolStripItem
{
    private Color _activeLinkColor = Color.Red;
    private bool _isLink;
    private LinkBehavior _linkBehavior = LinkBehavior.SystemDefault;
    private Color _linkColor = Color.FromArgb(0, 0, 255);
    private bool _linkVisited;
    private Color _visitedLinkColor = Color.FromArgb(128, 0, 128);

    /// <summary>Whether the pointer is currently over a link label: painted in <see cref="ActiveLinkColor"/> while true.</summary>
    private bool _linkActive;
    private Font? _linkFont;

    public ToolStripLabel()
    {
    }

    public ToolStripLabel(string? text)
        : base(text, null, null)
    {
    }

    public ToolStripLabel(Image? image)
        : base(null, image, null)
    {
    }

    public ToolStripLabel(string? text, Image? image)
        : base(text, image, null)
    {
    }

    public ToolStripLabel(string? text, Image? image, bool isLink)
        : base(text, image, null)
    {
        _isLink = isLink;
    }

    public ToolStripLabel(string? text, Image? image, bool isLink, EventHandler? onClick)
        : base(text, image, onClick)
    {
        _isLink = isLink;
    }

    public ToolStripLabel(string? text, Image? image, bool isLink, EventHandler? onClick, string? name)
        : base(text, image, onClick, name)
    {
        _isLink = isLink;
    }

    public Color ActiveLinkColor
    {
        get => _activeLinkColor;
        set
        {
            if (value == _activeLinkColor) return;

            _activeLinkColor = value;
            Invalidate();
        }
    }

    /// <summary>A label never takes the mouse down/press look: it has nothing to click for.</summary>
    public override bool CanSelect => false;

    [DefaultValue(false)]
    public bool IsLink
    {
        get => _isLink;
        set
        {
            if (value == _isLink) return;

            _isLink = value;
            Invalidate();
        }
    }

    [DefaultValue(LinkBehavior.SystemDefault)]
    public LinkBehavior LinkBehavior
    {
        get => _linkBehavior;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(LinkBehavior));
            if (value == _linkBehavior) return;

            _linkBehavior = value;
            Invalidate();
        }
    }

    public Color LinkColor
    {
        get => _linkColor;
        set
        {
            if (value == _linkColor) return;

            _linkColor = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool LinkVisited
    {
        get => _linkVisited;
        set
        {
            if (value == _linkVisited) return;

            _linkVisited = value;
            Invalidate();
        }
    }

    public Color VisitedLinkColor
    {
        get => _visitedLinkColor;
        set
        {
            if (value == _visitedLinkColor) return;

            _visitedLinkColor = value;
            Invalidate();
        }
    }

    /// <summary>A link's own role; a plain label keeps the base's (PushButton, inherited from ToolStripItemAccessibleObject).</summary>
    protected override AccessibleObject CreateAccessibilityInstance()
    {
        var accessibleObject = new ToolStripItemAccessibleObject(this);
        if (_isLink) accessibleObject.AddState(AccessibleStates.Linked);

        return accessibleObject;
    }

    /// <summary>The underlined variant a link paints with is cached from Font and must be rebuilt once Font itself changes.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        _linkFont?.Dispose();
        _linkFont = null;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);

        if (!_isLink) return;

        _linkActive = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (!_isLink) return;

        _linkActive = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var renderer = Owner?.Renderer;
        if (renderer is null)
        {
            base.OnPaint(e);
            return;
        }

        renderer.DrawLabelBackground(new ToolStripItemRenderEventArgs(e.Graphics, this));

        if (_isLink) PaintAsLink(renderer, e.Graphics);
        else ToolStripItemPainting.DrawImageAndText(renderer, e.Graphics, this);

        base.OnPaint(e);
    }

    /// <summary>Underlined, in whichever of the three link colours matches the current state - the same scheme LinkLabel paints with.</summary>
    private void PaintAsLink(ToolStripRenderer renderer, Graphics graphics)
    {
        var color = _linkActive ? _activeLinkColor : _linkVisited ? _visitedLinkColor : _linkColor;
        var underlineFont = _linkFont ??= new Font(Font, Font.Style | FontStyle.Underline);

        if (ImageToDraw is { } image)
        {
            ToolStripItemPainting.DrawImageAndText(renderer, graphics, this);
        }

        if (string.IsNullOrEmpty(Text)) return;

        var format = TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter,
        } | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(graphics, this, Text, ContentRectangleInStripCoordinates(), color, underlineFont, format));
    }

    /// <summary>Painting runs on the strip's own untranslated Graphics: Bounds (already absolute), not the item-relative ContentRectangle.</summary>
    private Rectangle ContentRectangleInStripCoordinates()
    {
        var padding = Padding;
        var bounds = Bounds;

        return new Rectangle(
            bounds.Left + padding.Left,
            bounds.Top + padding.Top,
            Math.Max(bounds.Width - padding.Horizontal, 0),
            Math.Max(bounds.Height - padding.Vertical, 0));
    }

    /// <summary>A plain label's mnemonic does nothing of its own (as Label's moves focus elsewhere, not here); a link's clicks through.</summary>
    protected internal override bool ProcessMnemonic(char charCode) => _isLink && base.ProcessMnemonic(charCode);
}
