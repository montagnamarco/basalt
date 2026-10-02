using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>Label, LinkLabel, GroupBox and Panel: controls with no interactive state of their own.</summary>
public static partial class VisualScenarios
{
    private static readonly Size LabelSize = new(100, 24);

    private static IReadOnlyList<Scenario> StaticScenarios() =>
    [
        Create<Label>("label-borderstyle-none", LabelSize, _ => { }),
        Create<Label>("label-borderstyle-fixedsingle", LabelSize, label => label.BorderStyle = BorderStyle.FixedSingle),
        Create<Label>("label-borderstyle-fixed3d", LabelSize, label => label.BorderStyle = BorderStyle.Fixed3D),
        Create<Label>("label-with-text", LabelSize, label => label.Text = "Label", FullArea(LabelSize)),

        Create<LinkLabel>("linklabel-no-text", LabelSize, _ => { }),
        Create<LinkLabel>("linklabel-with-text", LabelSize, linkLabel => linkLabel.Text = "Link", FullArea(LabelSize)),

        Create<GroupBox>("groupbox-no-text", new Size(140, 80), _ => { }),
        Create<GroupBox>("groupbox-with-text", new Size(140, 80), groupBox => groupBox.Text = "Group", FullArea(new Size(140, 80))),

        Create<Panel>("panel-borderstyle-none", new Size(100, 60), _ => { }),
        Create<Panel>("panel-borderstyle-fixedsingle", new Size(100, 60), panel => panel.BorderStyle = BorderStyle.FixedSingle),
        Create<Panel>("panel-borderstyle-fixed3d", new Size(100, 60), panel => panel.BorderStyle = BorderStyle.Fixed3D),
    ];
}
