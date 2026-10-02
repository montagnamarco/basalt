using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint a <see cref="ToolStripContentPanel"/>'s background.</summary>
public class ToolStripContentPanelRenderEventArgs : EventArgs
{
    public ToolStripContentPanelRenderEventArgs(Graphics g, ToolStripContentPanel contentPanel)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(contentPanel);

        Graphics = g;
        ToolStripContentPanel = contentPanel;
    }

    public Graphics Graphics { get; }

    /// <summary>Set by a handler that painted the background itself, so the default fill is skipped.</summary>
    public bool Handled { get; set; }

    public ToolStripContentPanel ToolStripContentPanel { get; }
}
