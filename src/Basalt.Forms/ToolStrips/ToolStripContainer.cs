using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Four <see cref="ToolStripPanel"/>s docked around a <see cref="ToolStripContentPanel"/>
/// that fills what is left: drop a form's menu, toolbars and status bar on
/// the edge panels and the form's own content on <see cref="ContentPanel"/>,
/// and the whole group resizes together.
/// </summary>
[Designer("System.Windows.Forms.Design.ToolStripContainerDesigner, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
public partial class ToolStripContainer : ContainerControl
{
    public ToolStripContainer()
    {
        ContentPanel = new ToolStripContentPanel { Dock = DockStyle.Fill };
        LeftToolStripPanel = new ToolStripPanel { Dock = DockStyle.Left };
        RightToolStripPanel = new ToolStripPanel { Dock = DockStyle.Right };
        BottomToolStripPanel = new ToolStripPanel { Dock = DockStyle.Bottom };
        TopToolStripPanel = new ToolStripPanel { Dock = DockStyle.Top };

        // Added back to front: the last one added is docked first (see DefaultLayout), so Top and
        // Bottom span the full width, Left and Right fill between them, and Content fills what remains.
        Controls.Add(ContentPanel);
        Controls.Add(LeftToolStripPanel);
        Controls.Add(RightToolStripPanel);
        Controls.Add(BottomToolStripPanel);
        Controls.Add(TopToolStripPanel);
    }

    protected override Size DefaultSize => new(150, 100);

    [Localizable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ToolStripPanel BottomToolStripPanel { get; }

    [DefaultValue(true)]
    public bool BottomToolStripPanelVisible
    {
        get => BottomToolStripPanel.Visible;
        set => BottomToolStripPanel.Visible = value;
    }

    [Localizable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ToolStripContentPanel ContentPanel { get; }

    [Localizable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ToolStripPanel LeftToolStripPanel { get; }

    [DefaultValue(true)]
    public bool LeftToolStripPanelVisible
    {
        get => LeftToolStripPanel.Visible;
        set => LeftToolStripPanel.Visible = value;
    }

    [Localizable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ToolStripPanel RightToolStripPanel { get; }

    [DefaultValue(true)]
    public bool RightToolStripPanelVisible
    {
        get => RightToolStripPanel.Visible;
        set => RightToolStripPanel.Visible = value;
    }

    [Localizable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ToolStripPanel TopToolStripPanel { get; }

    [DefaultValue(true)]
    public bool TopToolStripPanelVisible
    {
        get => TopToolStripPanel.Visible;
        set => TopToolStripPanel.Visible = value;
    }
}
