using System.Drawing.Printing;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Printing;

/// <summary>
/// The Allow* flags, Show* flags and the Document/PageSettings aliasing
/// PageSetupDialogTests.cs leaves out to stay under the file-size guideline.
/// </summary>
public partial class PageSetupDialogTests
{
    [AvaloniaFact]
    public void AllowMargins_false_disables_the_margin_boxes()
    {
        var dialog = new PageSetupDialog { AllowMargins = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.False(Named(form, "LeftMarginUpDown").Enabled);
            Assert.False(Named(form, "RightMarginUpDown").Enabled);
            Assert.False(Named(form, "TopMarginUpDown").Enabled);
            Assert.False(Named(form, "BottomMarginUpDown").Enabled);

            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void AllowOrientation_false_disables_the_orientation_radio_buttons()
    {
        var dialog = new PageSetupDialog { AllowOrientation = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.False(Named(form, "PortraitRadioButton").Enabled);
            Assert.False(Named(form, "LandscapeRadioButton").Enabled);

            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void AllowPaper_false_disables_the_paper_size_combo_box()
    {
        var dialog = new PageSetupDialog { AllowPaper = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.False(Named(form, "PaperSizeComboBox").Enabled);
            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void AllowPrinter_false_disables_the_printer_button()
    {
        var dialog = new PageSetupDialog { AllowPrinter = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.False(Named(form, "PrinterButton").Enabled);
            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void ShowHelp_shows_a_Help_button_that_raises_HelpRequest()
    {
        var dialog = new PageSetupDialog { ShowHelp = true };
        var raised = false;
        dialog.HelpRequest += (_, _) => raised = true;

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, ButtonNamed(form, "Help"));
            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.True(raised);
    }

    [AvaloniaFact]
    public void ShowNetwork_false_hides_the_network_button()
    {
        var dialog = new PageSetupDialog { ShowNetwork = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.DoesNotContain(AllButtons(form), b => b.Name == "NetworkButton");
            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [Fact]
    public void PageSettings_mirrors_the_attached_Document()
    {
        var dialog = new PageSetupDialog();
        var document = new PrintDocument();
        document.DefaultPageSettings.Landscape = true;

        dialog.Document = document;

        Assert.Same(document.DefaultPageSettings, dialog.PageSettings);
        Assert.True(dialog.PageSettings!.Landscape);
    }

    [Fact]
    public void Reset_restores_the_constructor_defaults()
    {
        var dialog = new PageSetupDialog
        {
            AllowMargins = false,
            AllowOrientation = false,
            AllowPaper = false,
            AllowPrinter = false,
            EnableMetric = true,
            MinMargins = new Margins(50, 50, 50, 50),
            ShowHelp = true,
            ShowNetwork = false,
            Document = new PrintDocument(),
        };

        dialog.Reset();

        Assert.True(dialog.AllowMargins);
        Assert.True(dialog.AllowOrientation);
        Assert.True(dialog.AllowPaper);
        Assert.True(dialog.AllowPrinter);
        Assert.False(dialog.EnableMetric);
        Assert.Null(dialog.MinMargins);
        Assert.False(dialog.ShowHelp);
        Assert.True(dialog.ShowNetwork);
        Assert.Null(dialog.Document);
    }
}
