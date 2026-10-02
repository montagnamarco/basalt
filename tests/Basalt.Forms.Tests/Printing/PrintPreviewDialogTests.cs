using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Printing;

/// <summary>
/// PrintPreviewDialog: the hidden Form members it re-declares still behave
/// like Form's, and its own toolbar drives the PrintPreviewControl it hosts.
/// </summary>
public class PrintPreviewDialogTests
{
    [Fact]
    public void A_hidden_member_set_on_the_dialog_reads_back_through_a_Form_reference()
    {
        var dialog = new PrintPreviewDialog();
        Form asForm = dialog;

        dialog.ControlBox = false;
        dialog.MinimizeBox = false;
        dialog.MaximizeBox = false;
        dialog.ShowInTaskbar = false;
        dialog.TopMost = true;
        dialog.Opacity = 0.5;
        dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
        dialog.StartPosition = FormStartPosition.CenterScreen;
        dialog.WindowState = FormWindowState.Maximized;

        Assert.False(asForm.ControlBox);
        Assert.False(asForm.MinimizeBox);
        Assert.False(asForm.MaximizeBox);
        Assert.False(asForm.ShowInTaskbar);
        Assert.True(asForm.TopMost);
        Assert.Equal(0.5, asForm.Opacity);
        Assert.Equal(FormBorderStyle.FixedDialog, asForm.FormBorderStyle);
        Assert.Equal(FormStartPosition.CenterScreen, asForm.StartPosition);
        Assert.Equal(FormWindowState.Maximized, asForm.WindowState);
    }

    [Fact]
    public void A_hidden_member_set_through_the_Form_reference_reads_back_on_the_dialog()
    {
        var dialog = new PrintPreviewDialog();
        Form asForm = dialog;

        asForm.Text = "Custom title";
        asForm.Size = new Size(500, 400);
        asForm.Location = new Point(10, 20);
        asForm.Tag = "tag-value";

        Assert.Equal("Custom title", dialog.Text);
        Assert.Equal(new Size(500, 400), dialog.Size);
        Assert.Equal(new Point(10, 20), dialog.Location);
        Assert.Equal("tag-value", dialog.Tag);
    }

    [Fact]
    public void A_hidden_changed_event_set_on_the_dialog_fires_from_the_Form_side_too()
    {
        var dialog = new PrintPreviewDialog();
        Form asForm = dialog;
        var raised = 0;

        dialog.BackColorChanged += (_, _) => raised++;

        asForm.BackColor = Color.Red;

        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_handler_added_through_the_Form_reference_fires_from_a_hidden_event_too()
    {
        var dialog = new PrintPreviewDialog();
        Form asForm = dialog;
        var raised = 0;

        asForm.EnabledChanged += (_, _) => raised++;
        dialog.Enabled = false;

        Assert.Equal(1, raised);
    }

    [Fact]
    public void The_page_layout_buttons_set_Columns_and_Rows_on_the_preview_control()
    {
        var dialog = new PrintPreviewDialog();
        var toolStrip = FindToolStrip(dialog);

        Click(toolStrip, "pages2x2ToolStripButton");

        Assert.Equal(2, dialog.PrintPreviewControl.Columns);
        Assert.Equal(2, dialog.PrintPreviewControl.Rows);

        Click(toolStrip, "pages3x2ToolStripButton");

        Assert.Equal(3, dialog.PrintPreviewControl.Columns);
        Assert.Equal(2, dialog.PrintPreviewControl.Rows);
    }

    [Fact]
    public void A_zoom_button_sets_an_explicit_Zoom_and_turns_off_AutoZoom()
    {
        var dialog = new PrintPreviewDialog();
        var toolStrip = FindToolStrip(dialog);

        Click(toolStrip, "zoom25%ToolStripButton");

        Assert.False(dialog.PrintPreviewControl.AutoZoom);
        Assert.Equal(0.25, dialog.PrintPreviewControl.Zoom);
    }

    [Fact]
    public void The_Auto_zoom_button_turns_AutoZoom_back_on()
    {
        var dialog = new PrintPreviewDialog();
        var toolStrip = FindToolStrip(dialog);
        dialog.PrintPreviewControl.Zoom = 0.5;

        Click(toolStrip, "zoomAutoToolStripButton");

        Assert.True(dialog.PrintPreviewControl.AutoZoom);
    }

    [Fact]
    public void The_page_number_box_moves_StartPage_and_raises_StartPageChanged()
    {
        var dialog = new PrintPreviewDialog { Document = TwoPageDocument() };
        var toolStrip = FindToolStrip(dialog);
        var pageBox = FindPageNumberBox(toolStrip);
        var raised = 0;
        dialog.PrintPreviewControl.StartPageChanged += (_, _) => raised++;

        pageBox.Value = 2;

        Assert.Equal(1, dialog.PrintPreviewControl.StartPage);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_page_number_past_the_last_page_shows_the_last_page_in_the_box()
    {
        var dialog = new PrintPreviewDialog { Document = TwoPageDocument() };
        var pageBox = FindPageNumberBox(FindToolStrip(dialog));
        pageBox.Value = 2;

        pageBox.Value = 99;

        Assert.Equal(1, dialog.PrintPreviewControl.StartPage);
        Assert.Equal(2, pageBox.Value);
    }

    [Fact]
    public void A_new_dialog_has_no_minimize_box_and_no_taskbar_button_as_its_defaults_declare()
    {
        var dialog = new PrintPreviewDialog();

        Assert.False(dialog.MinimizeBox);
        Assert.False(dialog.ShowInTaskbar);
    }

    [Fact]
    public void Moving_StartPage_on_the_control_syncs_the_page_number_box_back()
    {
        var dialog = new PrintPreviewDialog { Document = TwoPageDocument() };
        var toolStrip = FindToolStrip(dialog);
        var pageBox = FindPageNumberBox(toolStrip);

        dialog.PrintPreviewControl.StartPage = 1;

        Assert.Equal(2, pageBox.Value);
    }

    [AvaloniaFact]
    public void The_Close_button_closes_the_dialog()
    {
        var dialog = new PrintPreviewDialog();
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var toolStrip = FindToolStrip(dialog);

        Click(toolStrip, "closeToolStripButton");
        Dispatcher.UIThread.RunJobs();

        Assert.True(dialog.IsDisposed);
    }

    [AvaloniaFact]
    public void Escape_closes_the_dialog_through_ProcessDialogKey()
    {
        var dialog = new PrintPreviewDialog();
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        dialog.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(dialog.IsDisposed);
    }

    private static PrintDocument TwoPageDocument()
    {
        var document = new PrintDocument();
        var pages = 0;

        document.PrintPage += (_, e) =>
        {
            pages++;
            e.HasMorePages = pages < 2;
        };

        return document;
    }

    private static ToolStrip FindToolStrip(PrintPreviewDialog dialog)
    {
        foreach (Control control in dialog.Controls)
        {
            if (control is ToolStrip toolStrip) return toolStrip;
        }

        throw new InvalidOperationException("The dialog has no ToolStrip among its controls.");
    }

    private static NumericUpDown FindPageNumberBox(ToolStrip toolStrip)
    {
        var host = (ToolStripControlHost)toolStrip.Items.Find("pageNumberToolStripControlHost", false)[0];

        return (NumericUpDown)host.Control;
    }

    private static void Click(ToolStrip toolStrip, string name) => toolStrip.Items.Find(name, false)[0].PerformClick();
}
