using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Windows.Forms.Design;
using System.Windows.Forms.PropertyGridInternal;

namespace System.Windows.Forms;

/// <summary>
/// The members of the family's left-out features (the hot-commands pane,
/// the events toolbar button, flat-vs-3D toolbar rendering, the designer's
/// COM rename notification) that still need real storage and real
/// lifecycle hooks even though nothing paints them yet - see PropertyGrid.cs's
/// remarks for the full left-out list. Kept honest: every getter here
/// returns exactly what was last set (or a sensible default), nothing more.
/// </summary>
public partial class PropertyGrid
{
    private bool _commandsVisibleIfAvailable = true;
    private bool _eventsButtonVisible = true;
    private bool _drawFlatToolbar;
    private ToolStripRenderer? _toolStripRenderer;

    protected virtual Type DefaultTabType => typeof(PropertiesTab);

    protected bool DrawFlatToolbar
    {
        get => _drawFlatToolbar;
        set
        {
            if (value == _drawFlatToolbar) return;

            _drawFlatToolbar = value;
            _toolStripRenderer = null;
            Invalidate();
        }
    }

    protected ToolStripRenderer ToolStripRenderer
    {
        get => _toolStripRenderer ??= _drawFlatToolbar ? new ToolStripProfessionalRenderer() : new ToolStripSystemRenderer();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _toolStripRenderer = value;
            Invalidate();
        }
    }

    protected virtual Bitmap SortByCategoryImage => RenderGlyph(DrawCategorizedGlyph);

    protected virtual Bitmap SortByPropertyImage => RenderGlyph(DrawAlphabeticalGlyph);

    /// <summary>Microsoft's "..." property-page glyph, shown only when a component has a design-time property page; this family has no property-page feature (see the left-out list), so there is no icon to draw.</summary>
    protected virtual Bitmap ShowPropertyPageImage => null!;

    private static Bitmap RenderGlyph(Action<Graphics, Rectangle> draw)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        draw(graphics, new Rectangle(2, 2, 12, 12));
        return bitmap;
    }

    /// <summary>Whether the grid could ever show its hot-commands pane in principle; this family always can (it simply does not draw one yet).</summary>
    public virtual bool CanShowCommands => true;

    [DefaultValue(true)]
    public virtual bool CommandsVisibleIfAvailable
    {
        get => _commandsVisibleIfAvailable;
        set
        {
            if (value == _commandsVisibleIfAvailable) return;

            _commandsVisibleIfAvailable = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>Whether the pane is visible right now: available, and something is selected for it to describe.</summary>
    public virtual bool CommandsVisible => _commandsVisibleIfAvailable && SelectedGridItem is not null;

    public Color CommandsActiveLinkColor { get; set; } = SystemColors.HotTrack;

    public Color CommandsBackColor { get; set; } = SystemColors.Control;

    public Color CommandsBorderColor { get; set; } = SystemColors.ControlDark;

    public Color CommandsDisabledLinkColor { get; set; } = SystemColors.GrayText;

    public Color CommandsForeColor { get; set; } = SystemColors.ControlText;

    public Color CommandsLinkColor { get; set; } = SystemColors.HotTrack;

    /// <summary>Where a right-click context menu would appear without a mouse position to anchor it to: the middle of the grid, in screen coordinates - the same fallback Control.ContextMenuStrip callers use.</summary>
    public Point ContextMenuDefaultLocation => PointToScreen(new Point(Width / 2, Height / 2));

    /// <summary>Shows or hides the toolbar's events button; this family's toolbar does not draw one yet (see the left-out list), so this only remembers the setting for when it does.</summary>
    protected void ShowEventsButton(bool value)
    {
        if (value == _eventsButtonVisible) return;

        _eventsButtonVisible = value;
        PerformLayout();
        Invalidate();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PerformLayout();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        CancelEdit();
        base.OnHandleDestroyed(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) CancelEdit();
    }

    protected override void ScaleCore(float dx, float dy)
    {
        base.ScaleCore(dx, dy);
        RecomputeRowHeight();
        PerformLayout();
    }

    // Not yet raised from anywhere: IComPropertyBrowser.ComComponentNameChanged
    // (the event this backs) is not wired onto PropertyGrid in this family yet.
#pragma warning disable CS0649
    private EventHandler<ComponentRenameEventArgs>? _comComponentNameChanged;
#pragma warning restore CS0649

    /// <summary>Raises the rename notification IComPropertyBrowser.ComComponentNameChanged forwards (that interface is not wired up on this family's PropertyGrid yet); real behaviour once it is: whatever is listening is told the component's Site.Name changed.</summary>
    protected void OnComComponentNameChanged(ComponentRenameEventArgs e) => _comComponentNameChanged?.Invoke(this, e);

    /// <summary>IPropertyValueUIService's PropertyUIValueItemsChanged handler: the set of PropertyValueUIItems for the selected properties may have changed, so the cached item tree is dropped and the grid is asked to redraw.</summary>
    protected void OnNotifyPropertyValueUIItemsChanged(object? sender, EventArgs e)
    {
        _root = null;
        Invalidate();
    }
}
