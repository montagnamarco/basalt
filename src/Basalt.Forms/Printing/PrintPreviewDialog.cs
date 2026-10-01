using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;

namespace System.Windows.Forms;

/// <summary>
/// A ready-made Form hosting a <see cref="PrintPreviewControl"/> below a
/// toolbar of zoom and page-layout buttons: the common way an application
/// shows what a <see cref="PrintDocument"/> would print.
/// </summary>
[Designer("System.ComponentModel.Design.ComponentDesigner, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
[DesignTimeVisible(true)]
[DefaultProperty("Document")]
[ToolboxItem(true)]
public class PrintPreviewDialog : Form
{
    private readonly ToolStrip _toolStrip = new();
    private readonly PrintPreviewControl _printPreviewControl = new();

    public PrintPreviewDialog()
    {
        Text = "Print Preview";
        ClientSize = new Size(400, 300);

        _printPreviewControl.Dock = DockStyle.Fill;
        _printPreviewControl.UseAntiAlias = false;

        BuildToolStrip();

        Controls.Add(_printPreviewControl);
        Controls.Add(_toolStrip);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new IButtonControl? AcceptButton
    {
        get => base.AcceptButton;
        set => base.AcceptButton = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new IButtonControl? CancelButton
    {
        get => base.CancelButton;
        set => base.CancelButton = value;
    }

    [DefaultValue(null)]
    public PrintDocument? Document
    {
        get => _printPreviewControl.Document;
        set => _printPreviewControl.Document = value;
    }

    [Browsable(false)]
    public PrintPreviewControl PrintPreviewControl => _printPreviewControl;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [DefaultValue(false)]
    public bool UseAntiAlias
    {
        get => _printPreviewControl.UseAntiAlias;
        set => _printPreviewControl.UseAntiAlias = value;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <summary>One toolbar: a page-layout group (1/2/3/4/6 pages), a zoom group, print, and close.</summary>
    private void BuildToolStrip()
    {
        _toolStrip.Dock = DockStyle.Top;

        AddPagesButton(1, 1);
        AddPagesButton(2, 1);
        AddPagesButton(3, 1);
        AddPagesButton(2, 2);
        AddPagesButton(3, 2);

        _toolStrip.Items.Add(new ToolStripSeparator());

        AddZoomButton("100%", 1.0);
        AddZoomButton("75%", 0.75);
        AddZoomButton("50%", 0.5);
        AddZoomButton("25%", 0.25);
        AddZoomButton("Auto", null);

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
}
