namespace System.Windows.Forms;

/// <summary>
/// Painting the two containers a <see cref="ToolStripContainer"/> holds
/// besides its strips: <see cref="ToolStripPanel"/> (the docked band a strip
/// is dragged into) and <see cref="ToolStripContentPanel"/> (the middle that
/// fills what is left). The base class only raises the matching event, same
/// as every other Render* member here; the event args' Handled flag lets a
/// subscriber skip the default fill below.
/// </summary>
public partial class ToolStripRenderer
{
    public event ToolStripContentPanelRenderEventHandler? RenderToolStripContentPanelBackground;

    public event ToolStripPanelRenderEventHandler? RenderToolStripPanelBackground;

    public void DrawToolStripContentPanelBackground(ToolStripContentPanelRenderEventArgs e) => OnRenderToolStripContentPanelBackground(e);

    public void DrawToolStripPanelBackground(ToolStripPanelRenderEventArgs e) => OnRenderToolStripPanelBackground(e);

    protected virtual void InitializeContentPanel(ToolStripContentPanel contentPanel)
    {
    }

    protected virtual void InitializePanel(ToolStripPanel toolStripPanel)
    {
    }

    protected virtual void OnRenderToolStripContentPanelBackground(ToolStripContentPanelRenderEventArgs e) =>
        RenderToolStripContentPanelBackground?.Invoke(this, e);

    protected virtual void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e) =>
        RenderToolStripPanelBackground?.Invoke(this, e);
}
