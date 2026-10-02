using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint a <see cref="ToolStripPanel"/>'s background.</summary>
public class ToolStripPanelRenderEventArgs : EventArgs
{
    public ToolStripPanelRenderEventArgs(Graphics g, ToolStripPanel toolStripPanel)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(toolStripPanel);

        Graphics = g;
        ToolStripPanel = toolStripPanel;
    }

    public Graphics Graphics { get; }

    /// <summary>Set by a handler that painted the background itself, so the default fill is skipped.</summary>
    public bool Handled { get; set; }

    public ToolStripPanel ToolStripPanel { get; }
}
