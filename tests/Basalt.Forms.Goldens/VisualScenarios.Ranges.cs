using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>HScrollBar, VScrollBar, TrackBar and ProgressBar: controls that show a value along a track.</summary>
public static partial class VisualScenarios
{
    private static readonly Size HScrollSize = new(100, 17);
    private static readonly Size VScrollSize = new(17, 100);
    private static readonly Size HTrackSize = new(120, 45);
    private static readonly Size VTrackSize = new(45, 120);
    private static readonly Size ProgressSize = new(120, 23);

    private static IReadOnlyList<Scenario> RangeScenarios() =>
    [
        Create<HScrollBar>("hscrollbar-middle-value", HScrollSize, scrollBar => scrollBar.Value = 50),
        Create<HScrollBar>("hscrollbar-disabled", HScrollSize, scrollBar =>
        {
            scrollBar.Value = 50;
            scrollBar.Enabled = false;
        }),
        Create<VScrollBar>("vscrollbar-middle-value", VScrollSize, scrollBar => scrollBar.Value = 50),
        Create<VScrollBar>("vscrollbar-disabled", VScrollSize, scrollBar =>
        {
            scrollBar.Value = 50;
            scrollBar.Enabled = false;
        }),

        Create<TrackBar>("trackbar-horizontal-ticks-bottomright", HTrackSize, trackBar =>
        {
            trackBar.Minimum = 0;
            trackBar.Maximum = 10;
            trackBar.Value = 4;
            trackBar.TickStyle = TickStyle.BottomRight;
        }),
        Create<TrackBar>("trackbar-horizontal-ticks-topleft", HTrackSize, trackBar =>
        {
            trackBar.Minimum = 0;
            trackBar.Maximum = 10;
            trackBar.Value = 4;
            trackBar.TickStyle = TickStyle.TopLeft;
        }),
        Create<TrackBar>("trackbar-horizontal-ticks-none", HTrackSize, trackBar =>
        {
            trackBar.Minimum = 0;
            trackBar.Maximum = 10;
            trackBar.Value = 4;
            trackBar.TickStyle = TickStyle.None;
        }),
        Create<TrackBar>("trackbar-vertical-ticks-both", VTrackSize, trackBar =>
        {
            trackBar.Orientation = Orientation.Vertical;
            trackBar.Minimum = 0;
            trackBar.Maximum = 10;
            trackBar.Value = 4;
            trackBar.TickStyle = TickStyle.Both;
        }),
        Create<TrackBar>("trackbar-disabled", HTrackSize, trackBar =>
        {
            trackBar.Value = 4;
            trackBar.Enabled = false;
        }),

        Create<ProgressBar>("progressbar-value-0", ProgressSize, progressBar => progressBar.Value = 0),
        Create<ProgressBar>("progressbar-value-50", ProgressSize, progressBar => progressBar.Value = 50),
        Create<ProgressBar>("progressbar-value-100", ProgressSize, progressBar => progressBar.Value = 100),
    ];
}
