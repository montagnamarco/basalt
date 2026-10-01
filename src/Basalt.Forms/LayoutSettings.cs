namespace System.Windows.Forms;

/// <summary>
/// What a container's layout needs beyond the common anchor and dock
/// settings: today, which engine arranges it. FlowLayoutPanel's (and later
/// a ToolStrip's) settings derive from this.
/// </summary>
/// <remarks>
/// A minimal, shared base: no family had created it yet, and
/// <see cref="FlowLayoutSettings"/> cannot match Microsoft's public surface
/// without it (its <c>LayoutEngine</c> must override a virtual member here).
/// </remarks>
public abstract class LayoutSettings
{
    protected LayoutSettings()
    {
    }

    public virtual Layout.LayoutEngine? LayoutEngine => null;
}
