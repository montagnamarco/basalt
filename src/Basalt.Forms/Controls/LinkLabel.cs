using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Hosting;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// A Label whose text contains one or more clickable runs, painted like a
/// hyperlink and underlined per <see cref="LinkBehavior"/>; clicking or
/// activating one with the keyboard raises <see cref="LinkClicked"/>.
/// </summary>
/// <remarks>
/// A run is either a <see cref="Link"/> the application added to
/// <see cref="Links"/>, or, as long as none has been added, the single
/// implicit run <see cref="LinkArea"/> describes. GDI+ measures where each
/// run falls (so painting, hit-testing and the focus rectangle agree) with
/// <see cref="StringFormat.SetMeasurableCharacterRanges"/>, the same way the
/// real control measures its runs.
/// </remarks>
[DefaultEvent("LinkClicked")]
public partial class LinkLabel : Label, IButtonControl
{
    // GDI+ (and Basalt.Drawing, which mirrors it) measures at most 32
    // character ranges per call; a LinkLabel with more links than this would
    // need several measuring passes, which this implementation does not do.
    private const int MaxMeasurableLinks = 32;

    private readonly LinkCollection _links;
    private LinkArea _linkArea = new(0, -1);
    private Link? _autoLink;

    private Color _linkColor = Color.FromArgb(0, 0, 255);
    private Color _activeLinkColor = Color.Red;
    private Color _visitedLinkColor = Color.FromArgb(128, 0, 128);
    private Color _disabledLinkColor = Color.FromArgb(133, 133, 133);
    private LinkBehavior _linkBehavior;

    private Link? _hoverLink;
    private Link? _mouseActiveLink;
    private int _focusedLinkIndex = -1;

    private FlatStyle _flatStyle;
    private bool _useCompatibleTextRendering;
    private DialogResult _dialogResult;

    public LinkLabel()
    {
        _links = new LinkCollection(this);

        // Unlike Label, a LinkLabel takes the focus and the mouse itself: it
        // paints its own pressed/hover state instead of a native control's.
        SetStyle(ControlStyles.Selectable, true);
        SetStyle(ControlStyles.UserMouse, true);
        TabStop = true;
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

    public Color DisabledLinkColor
    {
        get => _disabledLinkColor;
        set
        {
            if (value == _disabledLinkColor) return;

            _disabledLinkColor = value;
            Invalidate();
        }
    }

    /// <summary>Kept for binary shape only: a LinkLabel is never flat or 3‑D, so changing this paints nothing differently.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public FlatStyle FlatStyle
    {
        get => _flatStyle;
        set => _flatStyle = value;
    }

    [RefreshProperties(RefreshProperties.Repaint)]
    public LinkArea LinkArea
    {
        get => _linkArea;
        set
        {
            if (value == _linkArea) return;

            _linkArea = value;
            _autoLink = null;
            InvalidateLinkLayout();
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

    /// <summary>
    /// Whether the link the control shows is visited: with no links added by
    /// hand, the single implicit one from <see cref="LinkArea"/>; otherwise
    /// every link in <see cref="Links"/> together.
    /// </summary>
    [DefaultValue(false)]
    public bool LinkVisited
    {
        get
        {
            var links = VisibleLinks();

            return links.Count > 0 && links.All(link => link.Visited);
        }
        set
        {
            foreach (var link in VisibleLinks()) link.Visited = value;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LinkCollection Links => _links;

    /// <summary>
    /// The cursor shown in place of <see cref="Control.Cursor"/> while it is set:
    /// the hand over an enabled link. Cursor itself is never touched, so a
    /// cursor the application chose comes back when the pointer leaves the link.
    /// </summary>
    protected Cursor? OverrideCursor
    {
        get => _overrideCursor;
        set
        {
            if (value == _overrideCursor) return;

            _overrideCursor = value;
            FormsInput.RefreshCursor();
        }
    }

    private Cursor? _overrideCursor;

    internal override Cursor? CursorOverrideInternal => _overrideCursor;

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    /// <summary>Kept for binary shape only: this implementation always measures and draws through Basalt.Drawing.</summary>
    [RefreshProperties(RefreshProperties.Repaint)]
    public bool UseCompatibleTextRendering
    {
        get => _useCompatibleTextRendering;
        set => _useCompatibleTextRendering = value;
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

    private static readonly object s_linkClickedEvent = new();

    public event LinkLabelLinkClickedEventHandler? LinkClicked
    {
        add => Events.AddHandler(s_linkClickedEvent, value);
        remove => Events.RemoveHandler(s_linkClickedEvent, value);
    }

    protected virtual void OnLinkClicked(LinkLabelLinkClickedEventArgs e) =>
        ((LinkLabelLinkClickedEventHandler?)Events[s_linkClickedEvent])?.Invoke(this, e);

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);

        // Disabled under the pointer: the hand goes at once, not at the next mouse move.
        if (!Enabled)
        {
            _hoverLink = null;
            OverrideCursor = null;
        }

        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        // The character ranges a run occupies depend on the font; Label only
        // repaints here when AutoSize resizes the control, which is not
        // enough to move the link runs when it does not.
        InvalidateLinkLayout();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        InvalidateLinkLayout();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        _autoLink = null;
        _hoverLink = null;
        _mouseActiveLink = null;
        _focusedLinkIndex = -1;

        base.OnTextChanged(e);
        InvalidateLinkLayout();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);

        if (_focusedLinkIndex < 0 && VisibleLinks().Count > 0) _focusedLinkIndex = 0;

        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        _mouseActiveLink = null;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = FindLinkAt(e.Location);

        if (!ReferenceEquals(hit, _hoverLink))
        {
            _hoverLink = hit;
            Invalidate();

            // A disabled control still hears mouse moves, but shows no hand over its links.
            OverrideCursor = Enabled && hit is { Enabled: true } ? Cursors.Hand : null;
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverLink is not null)
        {
            _hoverLink = null;
            Invalidate();
            OverrideCursor = null;
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && FindLinkAt(e.Location) is { Enabled: true } link)
        {
            _mouseActiveLink = link;

            var index = VisibleLinks().IndexOf(link);
            if (index >= 0) _focusedLinkIndex = index;

            Invalidate();
        }

        base.OnMouseDown(e);
    }

    /// <summary>A link clicks when it is released over the same link it was pressed on, as a button does.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var pressed = _mouseActiveLink;

        if (pressed is not null)
        {
            _mouseActiveLink = null;
            Invalidate();

            if (e.Button == MouseButtons.Left && FindLinkAt(e.Location) == pressed)
            {
                OnLinkClicked(new LinkLabelLinkClickedEventArgs(pressed, e.Button));
            }
        }

        base.OnMouseUp(e);
    }

    /// <summary>Tab moves among a LinkLabel's own links before it moves to the next control, as the real control does.</summary>
    protected override void Select(bool directed, bool forward)
    {
        if (directed)
        {
            var links = VisibleLinks();
            _focusedLinkIndex = links.Count > 0 ? (forward ? 0 : links.Count - 1) : -1;
        }

        base.Select(directed, forward);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab && (keyData & (Keys.Control | Keys.Alt)) == Keys.None && Focused)
        {
            var links = VisibleLinks();
            var forward = (keyData & Keys.Shift) == Keys.None;

            if (links.Count > 1)
            {
                var next = forward ? _focusedLinkIndex + 1 : _focusedLinkIndex - 1;

                if (next >= 0 && next < links.Count)
                {
                    _focusedLinkIndex = next;
                    Invalidate();

                    return true;
                }
            }
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <summary>Enter and Space activate the focused link, as clicking it does.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && !e.Alt && !e.Control)
        {
            if (FocusedLink is { Enabled: true } link)
            {
                OnLinkClicked(new LinkLabelLinkClickedEventArgs(link));
                e.Handled = true;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var bounds = TextBounds();
        var flags = TextFlags();
        var baseColor = Enabled ? ForeColor : SystemColors.GrayText;

        TextRenderer.DrawText(graphics, Text, Font, bounds, baseColor, flags);

        Font? underlineFont = null;

        try
        {
            foreach (var (link, index, runBounds) in MeasureLinkRuns(graphics))
            {
                var color = ResolveLinkColor(link);
                var underline = ShouldUnderline(link, index);
                var font = underline ? underlineFont ??= new Font(Font, Font.Style | FontStyle.Underline) : Font;

                // Only the run's horizontal span is restricted: the full text
                // height is kept so an underline (which sits below the tight
                // glyph bounds MeasureLinkRuns reports) is never clipped away.
                var clip = new Rectangle(runBounds.X, bounds.Top, runBounds.Width, bounds.Height);

                var saved = graphics.Save();
                try
                {
                    graphics.IntersectClip(clip);
                    TextRenderer.DrawText(graphics, Text, font, bounds, color, flags);
                }
                finally
                {
                    graphics.Restore(saved);
                }

                if (Focused && index == _focusedLinkIndex) ControlPaint.DrawFocusRectangle(graphics, runBounds);
            }
        }
        finally
        {
            underlineFont?.Dispose();
        }
    }

    /// <summary>The link at a point in client coordinates, or null when none covers it.</summary>
    protected Link? PointInLink(int x, int y) => FindLinkAt(new Point(x, y));

    private Link? FindLinkAt(Point location)
    {
        foreach (var (link, _, bounds) in MeasureLinkRuns(MeasurementGraphics))
        {
            if (bounds.Contains(location)) return link;
        }

        return null;
    }

    private Link? FocusedLink
    {
        get
        {
            var links = VisibleLinks();

            return _focusedLinkIndex >= 0 && _focusedLinkIndex < links.Count ? links[_focusedLinkIndex] : null;
        }
    }

    private Color ResolveLinkColor(Link link)
    {
        if (!Enabled || !link.Enabled) return DisabledLinkColor;
        if (ReferenceEquals(link, _mouseActiveLink)) return ActiveLinkColor;
        if (link.Visited) return VisitedLinkColor;

        return LinkColor;
    }

    private bool ShouldUnderline(Link link, int index)
    {
        var hoveredOrFocused = ReferenceEquals(link, _hoverLink) || (Focused && index == _focusedLinkIndex);

        return LinkBehavior switch
        {
            LinkBehavior.NeverUnderline => false,
            LinkBehavior.HoverUnderline => hoveredOrFocused,

            // AlwaysUnderline, and SystemDefault: this project paints the
            // classic Windows look, which always underlined its hyperlinks.
            _ => true,
        };
    }

    /// <summary>
    /// The links to lay out: the ones the application added, in Start order,
    /// or, as long as none has been added, the single implicit link
    /// <see cref="LinkArea"/> describes over the current <see cref="Text"/>.
    /// </summary>
    private List<Link> VisibleLinks()
    {
        if (_links.LinksAdded) return _links.Ordered;

        var text = Text ?? string.Empty;
        var start = Math.Clamp(_linkArea.Start, 0, text.Length);
        var length = _linkArea.Length < 0 ? text.Length - start : Math.Clamp(_linkArea.Length, 0, text.Length - start);

        if (length <= 0)
        {
            _autoLink = null;
            return [];
        }

        _autoLink ??= new Link(start, length);
        _autoLink.Start = start;
        _autoLink.Length = length;

        return [_autoLink];
    }

    /// <summary>Called by a Link or the LinkCollection when something that changes how they paint changes.</summary>
    internal void InvalidateLinkLayout() => Invalidate();

    private Rectangle TextBounds()
    {
        var client = ClientRectangle;

        return new Rectangle(client.X + Padding.Left, client.Y + Padding.Top, client.Width - Padding.Horizontal, client.Height - Padding.Vertical);
    }

    private TextFormatFlags TextFlags()
    {
        var flags = ContentAlignments.ToTextFormat(TextAlign) | TextFormatFlags.WordBreak;
        if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;

        return flags;
    }

    /// <summary>
    /// Where each visible link falls, measured the way <see cref="TextRenderer"/>
    /// draws: typographic metrics, the same overhang margin, one character
    /// range per link. The bounds come back in client coordinates.
    /// </summary>
    private List<(Link Link, int Index, Rectangle Bounds)> MeasureLinkRuns(Graphics graphics)
    {
        if (string.IsNullOrEmpty(Text)) return [];

        var allLinks = VisibleLinks();
        var entries = new List<(Link Link, int Index, int Start, int Length)>();

        for (var i = 0; i < allLinks.Count; i++)
        {
            var link = allLinks[i];
            var start = Math.Clamp(link.Start, 0, Text.Length);
            var length = Math.Clamp(link.Length < 0 ? Text.Length - start : link.Length, 0, Text.Length - start);

            if (length > 0) entries.Add((link, i, start, length));
        }

        if (entries.Count == 0) return [];
        if (entries.Count > MaxMeasurableLinks) entries = entries.Take(MaxMeasurableLinks).ToList();

        var inner = InnerTextBounds();
        using var format = BuildStringFormat();
        format.SetMeasurableCharacterRanges([.. entries.Select(entry => new CharacterRange(entry.Start, entry.Length))]);

        var regions = graphics.MeasureCharacterRanges(Text, Font, inner, format);
        var result = new List<(Link, int, Rectangle)>();

        for (var i = 0; i < regions.Length; i++)
        {
            var bounds = Rectangle.Round(regions[i].GetBounds(graphics));
            regions[i].Dispose();

            if (bounds.Width > 0 && bounds.Height > 0) result.Add((entries[i].Link, entries[i].Index, bounds));
        }

        return result;
    }

    private RectangleF InnerTextBounds()
    {
        var bounds = TextBounds();
        var overhang = Font.Height / 6;

        return RectangleF.FromLTRB(bounds.Left + overhang, bounds.Top, bounds.Right - overhang, bounds.Bottom);
    }

    /// <summary>The same typographic format <see cref="TextRenderer"/> draws with, so a run's measured bounds match its pixels.</summary>
    private StringFormat BuildStringFormat()
    {
        var flags = TextFlags();
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        if ((flags & TextFormatFlags.WordBreak) == 0) format.FormatFlags |= StringFormatFlags.NoWrap;

        format.Alignment = (flags & TextFormatFlags.HorizontalCenter) != 0 ? StringAlignment.Center
            : (flags & TextFormatFlags.Right) != 0 ? StringAlignment.Far
            : StringAlignment.Near;

        format.HotkeyPrefix = UseMnemonic ? HotkeyPrefix.Show : HotkeyPrefix.None;

        return format;
    }

    DialogResult IButtonControl.DialogResult
    {
        get => _dialogResult;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DialogResult));

            _dialogResult = value;
        }
    }

    /// <summary>A LinkLabel draws no default-button frame; the flag only lets a Form's AcceptButton be a LinkLabel.</summary>
    void IButtonControl.NotifyDefault(bool value)
    {
    }

    void IButtonControl.PerformClick()
    {
        var link = FocusedLink ?? VisibleLinks().FirstOrDefault();
        if (link is not { Enabled: true }) return;

        if (FindForm() is { } form && _dialogResult != DialogResult.None) form.DialogResult = _dialogResult;

        OnLinkClicked(new LinkLabelLinkClickedEventArgs(link));
    }
}
