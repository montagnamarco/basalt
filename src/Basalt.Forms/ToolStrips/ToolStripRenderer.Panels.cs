namespace System.Windows.Forms;

/// <summary>
/// Painting the two containers a <see cref="ToolStripContainer"/> holds
/// besides its strips: <see cref="ToolStripPanel"/> (the docked band a strip
/// is dragged into) and <see cref="ToolStripContentPanel"/> (the middle that
/// fills what is left). Unlike the item/strip Draw* methods, these event
/// args carry a <c>Handled</c> flag the container consults to decide whether
/// to run its own default fill (see <see cref="ToolStripPanel"/>'s and
/// ToolStripContentPanel's OnPaintBackground). So the base OnRenderX raises
/// the Render* event itself, before any override's default painting runs:
/// that gives a subscriber the chance to set Handled in time to suppress
/// both the renderer's default fill (ToolStripProfessionalRenderer checks
/// Handled right after calling base.OnRenderX) and the container's own
/// (System.Windows.Forms behaves the same way for these two containers).
/// </summary>
public partial class ToolStripRenderer
{
    public event ToolStripContentPanelRenderEventHandler? RenderToolStripContentPanelBackground;

    public event ToolStripPanelRenderEventHandler? RenderToolStripPanelBackground;

    public void DrawToolStripContentPanelBackground(ToolStripContentPanelRenderEventArgs e)
    {
        OnRenderToolStripContentPanelBackground(e);
    }

    public void DrawToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
    {
        OnRenderToolStripPanelBackground(e);
    }

    protected virtual void InitializeContentPanel(ToolStripContentPanel contentPanel)
    {
    }

    protected virtual void InitializePanel(ToolStripPanel toolStripPanel)
    {
    }

    protected virtual void OnRenderToolStripContentPanelBackground(ToolStripContentPanelRenderEventArgs e)
    {
        RenderToolStripContentPanelBackground?.Invoke(this, e);
    }

    protected virtual void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
    {
        RenderToolStripPanelBackground?.Invoke(this, e);
    }
}
