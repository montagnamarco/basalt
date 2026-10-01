using System.Drawing.Printing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Printing;

/// <summary>PrintPreviewDialog: Document forwarding and the toolbar's page/zoom buttons.</summary>
public class PrintPreviewDialogTests
{
    private static ToolStripButton FindButton(PrintPreviewDialog dialog, string name)
    {
        foreach (Control control in dialog.Controls)
        {
            if (control is not ToolStrip toolStrip) continue;

            foreach (ToolStripItem item in toolStrip.Items)
            {
                if (item is ToolStripButton button && button.Name == name) return button;
            }
        }

        throw new InvalidOperationException($"No toolbar button named '{name}' was found.");
    }

    [Fact]
    public void The_dialogs_document_reaches_its_preview_control()
    {
        var dialog = new PrintPreviewDialog();
        var document = new PrintDocument();

        dialog.Document = document;

        Assert.Same(document, dialog.PrintPreviewControl.Document);
        Assert.Same(document, dialog.Document);
    }

    [Fact]
    public void A_pages_button_sets_the_controls_columns_and_rows()
    {
        var dialog = new PrintPreviewDialog();

        FindButton(dialog, "pages2x2ToolStripButton").PerformClick();

        Assert.Equal(2, dialog.PrintPreviewControl.Columns);
        Assert.Equal(2, dialog.PrintPreviewControl.Rows);

        FindButton(dialog, "pages1x1ToolStripButton").PerformClick();

        Assert.Equal(1, dialog.PrintPreviewControl.Columns);
        Assert.Equal(1, dialog.PrintPreviewControl.Rows);
    }

    [Fact]
    public void A_zoom_button_sets_the_controls_zoom_and_turns_off_AutoZoom()
    {
        var dialog = new PrintPreviewDialog();

        FindButton(dialog, "zoom50%ToolStripButton").PerformClick();

        Assert.False(dialog.PrintPreviewControl.AutoZoom);
        Assert.Equal(0.5, dialog.PrintPreviewControl.Zoom);

        FindButton(dialog, "zoomAutoToolStripButton").PerformClick();

        Assert.True(dialog.PrintPreviewControl.AutoZoom);
    }

    [Fact]
    public void UseAntiAlias_forwards_to_the_preview_control()
    {
        var dialog = new PrintPreviewDialog { UseAntiAlias = true };

        Assert.True(dialog.PrintPreviewControl.UseAntiAlias);
    }
}
