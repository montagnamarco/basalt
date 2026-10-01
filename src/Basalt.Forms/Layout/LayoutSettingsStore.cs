using System.Drawing;

namespace System.Windows.Forms.Layout;

/// <summary>
/// The layout settings of one element: how it is anchored or docked, how it
/// sizes itself, and what the engines remember about it between layouts.
/// </summary>
internal sealed class LayoutSettingsStore
{
    public const AnchorStyles DefaultAnchor = AnchorStyles.Top | AnchorStyles.Left;

    private AnchorStyles _anchor = DefaultAnchor;

    /// <summary>
    /// The anchor. While the element is docked the anchor reads as the
    /// default, as in WinForms: the two are exclusive, and the last one set wins.
    /// </summary>
    public AnchorStyles Anchor
    {
        get => Dock == DockStyle.None ? _anchor : DefaultAnchor;
        set => _anchor = value;
    }

    public DockStyle Dock { get; set; }

    public bool AutoSize { get; set; }

    public AutoSizeMode AutoSizeMode { get; set; } = AutoSizeMode.GrowOnly;

    public Padding Margin { get; set; }

    public Padding Padding { get; set; }

    public Size MinimumSize { get; set; }

    public Size MaximumSize { get; set; }

    /// <summary>
    /// The bounds as the user last set them, side by side: docking and
    /// layout change the real bounds, and undocking returns to these.
    /// </summary>
    public Rectangle SpecifiedBounds { get; set; }

    /// <summary>Distances to the container's edges measured when the bounds were set; null until measured.</summary>
    public AnchorDistances? AnchorInfo { get; set; }

    /// <summary>The preferred size for an unconstrained request, until something that changes it happens.</summary>
    public Size? PreferredSizeCache { get; set; }

    /// <summary>Whether the default layout should arrange this element by its anchors.</summary>
    public bool NeedsAnchorLayout(IArrangedElement element) =>
        element.ParticipatesInLayout && Dock == DockStyle.None && Anchor != DefaultAnchor;

    public bool NeedsDockLayout(IArrangedElement element) => element.ParticipatesInLayout && Dock != DockStyle.None;

    /// <summary>Settings that other engines attach to their children: flow breaks, table cells.</summary>
    public Dictionary<object, object?> Extended { get; } = [];
}

/// <summary>
/// An anchored element's edges relative to its container's display
/// rectangle. An edge anchored on the far side is kept as a (usually
/// negative) distance from that side, an edge anchored on neither side as a
/// distance from the middle; that is what lets the edge follow when the
/// container resizes.
/// </summary>
internal sealed class AnchorDistances
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Right { get; set; }

    public int Bottom { get; set; }
}
