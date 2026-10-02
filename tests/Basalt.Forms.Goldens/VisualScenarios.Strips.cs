using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>ToolStrip, MenuStrip and StatusStrip: the strip controls that host ToolStripItems.</summary>
public static partial class VisualScenarios
{
    private static readonly Size StripSize = new(160, 25);

    private static IReadOnlyList<Scenario> StripScenarios() =>
    [
        Create<ToolStrip>("toolstrip-buttons-separator-grip", StripSize, toolStrip =>
        {
            toolStrip.GripStyle = ToolStripGripStyle.Visible;
            toolStrip.Items.Add(new ToolStripButton("One"));
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripButton("Two"));
        }, FullArea(StripSize)),
        Create<ToolStrip>("toolstrip-no-grip", StripSize, toolStrip =>
        {
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip.Items.Add(new ToolStripButton("One"));
        }, FullArea(StripSize)),

        Create<MenuStrip>("menustrip-with-items", StripSize, menuStrip =>
        {
            menuStrip.Items.Add(new ToolStripMenuItem("File"));
            menuStrip.Items.Add(new ToolStripMenuItem("Edit"));
        }, FullArea(StripSize)),

        Create<StatusStrip>("statusstrip-with-label", StripSize, statusStrip =>
        {
            statusStrip.Items.Add(new ToolStripStatusLabel("Ready"));
        }, FullArea(StripSize)),
    ];
}
