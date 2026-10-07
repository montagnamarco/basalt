using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The window region: the shape of the control, outside of which it is
/// neither drawn nor hit, as SetWindowRgn makes it on Windows. Coordinates
/// are the window's, frame included, with the origin at its top-left corner.
/// </summary>
/// <remarks>
/// A top-level form's native window stays rectangular: the canvas clips its
/// drawing, but the platform window around it is not shaped.
/// </remarks>
public partial class Control
{
    private static readonly object s_regionChangedEvent = new();

    private Region? _region;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Region? Region
    {
        get => _region;
        set
        {
            if (ReferenceEquals(_region, value)) return;

            _region = value;
            OnRegionChanged(EventArgs.Empty);

            // What the old shape hid in the parent must be painted again, and the new shape drawn.
            _parent?.Invalidate(Bounds, invalidateChildren: true);
            Invalidate(invalidateChildren: true);
        }
    }

    [Category("Property Changed")]
    public event EventHandler? RegionChanged
    {
        add => Events.AddHandler(s_regionChangedEvent, value);
        remove => Events.RemoveHandler(s_regionChangedEvent, value);
    }

    protected virtual void OnRegionChanged(EventArgs e) => ((EventHandler?)Events[s_regionChangedEvent])?.Invoke(this, e);

    /// <summary>Whether a point in the parent's client coordinates lies inside this control's shape.</summary>
    private bool RegionContains(Point inParent)
    {
        if (_region is null) return true;

        return _region.IsVisible(inParent.X - Left, inParent.Y - Top);
    }
}
