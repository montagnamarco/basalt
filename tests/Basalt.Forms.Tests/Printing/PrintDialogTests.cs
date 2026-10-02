using System.Drawing.Printing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Printing;

/// <summary>
/// PrintDialog, driven the way a real application drives it: ShowDialog
/// blocks until the internal PrintDialogForm closes, so a posted action finds
/// it through Application.OpenForms, sets its controls, and lets it close.
/// Mirrors ColorDialogTests' own ShowAndInteract pattern.
/// </summary>
public class PrintDialogTests
{
    private static DialogResult ShowAndInteract(PrintDialog dialog, Action<Form> interact)
    {
        Dispatcher.UIThread.Post(() => interact(Application.OpenForms.Cast<Form>().Last()));

        return dialog.ShowDialog();
    }

    /// <summary>
    /// Clicks the centre of a control that may be nested inside a group box:
    /// unlike ColorDialogTests' own swatches, these controls are not direct
    /// children of the Form, so Left/Top alone is not the Form's own
    /// coordinate space.
    /// </summary>
    private static void Click(Form form, Control control)
    {
        var screenPoint = control.PointToScreen(new System.Drawing.Point(5, 5));
        var formPoint = form.PointToClient(screenPoint);
        var point = new Avalonia.Point(formPoint.X, formPoint.Y);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    private static Control Named(Form form, string name) => form.Controls.Find(name, searchAllChildren: true)[0];

    [AvaloniaFact]
    public void Checking_collate_and_setting_copies_writes_back_only_on_OK()
    {
        var dialog = new PrintDialog();
        dialog.PrinterSettings.Copies = 1;
        dialog.PrinterSettings.Collate = false;

        var result = ShowAndInteract(dialog, form =>
        {
            ((NumericUpDown)Named(form, "CopiesUpDown")).Value = 4;
            Click(form, Named(form, "CollateCheckBox"));

            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal((short)4, dialog.PrinterSettings.Copies);
        Assert.True(dialog.PrinterSettings.Collate);
    }

    [AvaloniaFact]
    public void Cancel_leaves_PrinterSettings_unchanged()
    {
        var dialog = new PrintDialog();
        dialog.PrinterSettings.Copies = 2;

        var result = ShowAndInteract(dialog, form =>
        {
            ((NumericUpDown)Named(form, "CopiesUpDown")).Value = 7;
            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal((short)2, dialog.PrinterSettings.Copies);
    }

    [AvaloniaFact]
    public void Picking_the_Pages_range_writes_back_From_and_To()
    {
        var dialog = new PrintDialog { AllowSomePages = true };
        dialog.PrinterSettings.MinimumPage = 1;
        dialog.PrinterSettings.MaximumPage = 20;

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, Named(form, "PagesRadioButton"));
            ((NumericUpDown)Named(form, "FromPageUpDown")).Value = 3;
            ((NumericUpDown)Named(form, "ToPageUpDown")).Value = 9;

            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(PrintRange.SomePages, dialog.PrinterSettings.PrintRange);
        Assert.Equal(3, dialog.PrinterSettings.FromPage);
        Assert.Equal(9, dialog.PrinterSettings.ToPage);
    }

    [AvaloniaFact]
    public void A_page_range_never_runs_backwards()
    {
        var dialog = new PrintDialog { AllowSomePages = true };
        dialog.PrinterSettings.MinimumPage = 1;
        dialog.PrinterSettings.MaximumPage = 20;

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, Named(form, "PagesRadioButton"));
            var from = (NumericUpDown)Named(form, "FromPageUpDown");
            var to = (NumericUpDown)Named(form, "ToPageUpDown");
            to.Value = 3;
            from.Value = 9;

            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.True(dialog.PrinterSettings.FromPage <= dialog.PrinterSettings.ToPage,
            $"from {dialog.PrinterSettings.FromPage} is after to {dialog.PrinterSettings.ToPage}");
        Assert.Equal(9, dialog.PrinterSettings.FromPage);
    }

    [AvaloniaFact]
    public void AllowSomePages_false_disables_the_Pages_radio_button_and_its_fields()
    {
        var dialog = new PrintDialog { AllowSomePages = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.False(Named(form, "PagesRadioButton").Enabled);
            Assert.False(Named(form, "FromPageUpDown").Enabled);
            Assert.False(Named(form, "ToPageUpDown").Enabled);

            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void AllowSelection_and_AllowCurrentPage_false_disable_their_radio_buttons()
    {
        var dialog = new PrintDialog { AllowSelection = false, AllowCurrentPage = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.False(Named(form, "SelectionRadioButton").Enabled);
            Assert.False(Named(form, "CurrentPageRadioButton").Enabled);
            Assert.True(Named(form, "AllRadioButton").Enabled);

            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void PrintToFile_checkbox_is_disabled_when_AllowPrintToFile_is_false()
    {
        var dialog = new PrintDialog { AllowPrintToFile = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.False(Named(form, "PrintToFileCheckBox").Enabled);
            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void Typing_a_printer_name_and_pressing_OK_writes_it_back()
    {
        var dialog = new PrintDialog();

        var result = ShowAndInteract(dialog, form =>
        {
            ((ComboBox)Named(form, "PrinterComboBox")).Text = "My Printer";
            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal("My Printer", dialog.PrinterSettings.PrinterName);
    }

    [AvaloniaFact]
    public void ShowHelp_shows_a_Help_button_that_raises_HelpRequest()
    {
        var dialog = new PrintDialog { ShowHelp = true };
        var raised = false;
        dialog.HelpRequest += (_, _) => raised = true;

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Help"));
            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.True(raised);
    }

    [AvaloniaFact]
    public void ShowHelp_false_shows_no_Help_button()
    {
        var dialog = new PrintDialog { ShowHelp = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.DoesNotContain(form.Controls.Cast<Control>().OfType<Button>(), b => b.Text == "Help");
            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void ShowNetwork_false_hides_the_network_button()
    {
        var dialog = new PrintDialog { ShowNetwork = false };

        var result = ShowAndInteract(dialog, form =>
        {
            Assert.DoesNotContain(form.Controls.Cast<Control>().OfType<Button>(), b => b.Name == "NetworkButton");
            Click(form, form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [Fact]
    public void PrinterSettings_mirrors_the_attached_Document()
    {
        var dialog = new PrintDialog();
        var document = new PrintDocument();
        document.PrinterSettings.PrinterName = "DocPrinter";

        dialog.Document = document;

        Assert.Same(document.PrinterSettings, dialog.PrinterSettings);
        Assert.Equal("DocPrinter", dialog.PrinterSettings.PrinterName);
    }

    [Fact]
    public void Reset_restores_the_constructor_defaults()
    {
        var dialog = new PrintDialog
        {
            AllowCurrentPage = true,
            AllowPrintToFile = false,
            AllowSelection = true,
            AllowSomePages = true,
            PrintToFile = true,
            ShowHelp = true,
            ShowNetwork = false,
            UseEXDialog = true,
            Document = new PrintDocument(),
        };

        dialog.Reset();

        Assert.False(dialog.AllowCurrentPage);
        Assert.True(dialog.AllowPrintToFile);
        Assert.False(dialog.AllowSelection);
        Assert.False(dialog.AllowSomePages);
        Assert.False(dialog.PrintToFile);
        Assert.False(dialog.ShowHelp);
        Assert.True(dialog.ShowNetwork);
        Assert.False(dialog.UseEXDialog);
        Assert.Null(dialog.Document);
    }
}
