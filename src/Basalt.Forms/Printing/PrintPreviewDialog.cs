using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;

namespace System.Windows.Forms;

/// <summary>
/// A ready-made Form hosting a <see cref="PrintPreviewControl"/> below a
/// toolbar of zoom and page-layout buttons: the common way an application
/// shows what a <see cref="PrintDocument"/> would print.
/// </summary>
/// <remarks>
/// Most of the members Form declares are re-declared here, hidden from the
/// designer, because a print preview dialog is a fixed-purpose window: see
/// PrintPreviewDialog.HiddenMembers*.cs, PrintPreviewDialog.HiddenEvents.cs
/// and PrintPreviewDialog.Overrides.cs.
/// </remarks>
[Designer("System.ComponentModel.Design.ComponentDesigner, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
[DesignTimeVisible(true)]
[DefaultProperty("Document")]
[ToolboxItemFilter("System.Windows.Forms.Control.TopLevel")]
[ToolboxItem(true)]
public partial class PrintPreviewDialog : Form
{
    private readonly ToolStrip _toolStrip = new();
    private readonly PrintPreviewControl _printPreviewControl = new();
    private readonly NumericUpDown _pageNumberBox = new();

    public PrintPreviewDialog()
    {
        Text = "Print Preview";
        ClientSize = new Size(400, 300);

        // A preview is a tool window of the application: Windows shows it with no minimize box and no taskbar button.
        MinimizeBox = false;
        ShowInTaskbar = false;

        _printPreviewControl.Dock = DockStyle.Fill;
        _printPreviewControl.UseAntiAlias = false;
        _printPreviewControl.StartPageChanged += (_, _) => SyncPageNumberBox();

        BuildToolStrip();

        Controls.Add(_printPreviewControl);
        Controls.Add(_toolStrip);
    }

    [DefaultValue(null)]
    public PrintDocument? Document
    {
        get => _printPreviewControl.Document;
        set => _printPreviewControl.Document = value;
    }

    [Browsable(false)]
    public PrintPreviewControl PrintPreviewControl => _printPreviewControl;

    [DefaultValue(false)]
    public bool UseAntiAlias
    {
        get => _printPreviewControl.UseAntiAlias;
        set => _printPreviewControl.UseAntiAlias = value;
    }

    /// <summary>
    /// One toolbar: a page-layout group (1/2/3/4/6 pages), a zoom group, the
    /// current page number, print, and close.
    /// </summary>
    private void BuildToolStrip()
    {
        _toolStrip.Dock = DockStyle.Top;

        AddPagesButton(1, 1);
        AddPagesButton(2, 1);
        AddPagesButton(3, 1);
        AddPagesButton(2, 2);
        AddPagesButton(3, 2);

        _toolStrip.Items.Add(new ToolStripSeparator());

        AddZoomButton("Auto", null);
        AddZoomButton("500%", 5.0);
        AddZoomButton("250%", 2.5);
        AddZoomButton("150%", 1.5);
        AddZoomButton("100%", 1.0);
        AddZoomButton("75%", 0.75);
        AddZoomButton("50%", 0.5);
        AddZoomButton("25%", 0.25);
        AddZoomButton("10%", 0.1);

        _toolStrip.Items.Add(new ToolStripSeparator());

        BuildPageNumberBox();

        _toolStrip.Items.Add(new ToolStripSeparator());

        var print = new ToolStripButton("Print") { Name = "printToolStripButton" };
        print.Click += (_, _) => Document?.Print();
        _toolStrip.Items.Add(print);

        var close = new ToolStripButton("Close") { Name = "closeToolStripButton" };
        close.Click += (_, _) => Close();
        _toolStrip.Items.Add(close);
    }

    private void AddPagesButton(int columns, int rows)
    {
        var button = new ToolStripButton(columns == 1 && rows == 1 ? "1 page" : $"{columns * rows} pages")
        {
            Name = $"pages{columns}x{rows}ToolStripButton",
        };

        button.Click += (_, _) =>
        {
            _printPreviewControl.Columns = columns;
            _printPreviewControl.Rows = rows;
        };

        _toolStrip.Items.Add(button);
    }

    private void AddZoomButton(string text, double? zoom)
    {
        var button = new ToolStripButton(text) { Name = $"zoom{text}ToolStripButton" };

        button.Click += (_, _) =>
        {
            if (zoom is null)
            {
                _printPreviewControl.AutoZoom = true;
            }
            else
            {
                _printPreviewControl.AutoZoom = false;
                _printPreviewControl.Zoom = zoom.Value;
            }
        };

        _toolStrip.Items.Add(button);
    }

    /// <summary>The 1-based page number box: it both shows and drives PrintPreviewControl.StartPage (0-based).</summary>
    private void BuildPageNumberBox()
    {
        _toolStrip.Items.Add(new ToolStripLabel("Page:") { Name = "pageLabelToolStripLabel" });

        // The real page count is only known once PrintPreviewControl renders the document (lazily,
        // on first paint or StartPage access); until then, a generously high ceiling keeps the box usable.
        _pageNumberBox.Name = "pageNumberNumericUpDown";
        _pageNumberBox.Minimum = 1;
        _pageNumberBox.Maximum = 9999;
        _pageNumberBox.Value = 1;
        _pageNumberBox.ValueChanged += (_, _) => ShowPageFromBox();

        _toolStrip.Items.Add(new ToolStripControlHost(_pageNumberBox) { Name = "pageNumberToolStripControlHost" });
    }

    /// <summary>
    /// Moves the preview to the typed page, then shows the page it really
    /// moved to: a number past the last page clamps there, and when that is
    /// the page already shown StartPageChanged does not fire to correct the box.
    /// </summary>
    private void ShowPageFromBox()
    {
        _printPreviewControl.StartPage = (int)_pageNumberBox.Value - 1;
        SyncPageNumberBox();
    }

    /// <summary>Reflects the control's current page back into the page number box, without re-entering StartPage.</summary>
    private void SyncPageNumberBox()
    {
        var oneBased = _printPreviewControl.StartPage + 1;

        _pageNumberBox.Maximum = Math.Max(_pageNumberBox.Maximum, oneBased);
        if (_pageNumberBox.Value != oneBased) _pageNumberBox.Value = oneBased;
    }
}
