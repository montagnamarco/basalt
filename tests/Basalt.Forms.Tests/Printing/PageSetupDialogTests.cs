using System.Drawing.Printing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Printing;

/// <summary>
/// PageSetupDialog, driven the way a real application drives it: ShowDialog
/// blocks until the internal PageSetupDialogForm closes, so a posted action
/// finds it through Application.OpenForms, sets its controls, and lets it
/// close. Mirrors PrintDialogTests' own ShowAndInteract pattern.
/// </summary>
public partial class PageSetupDialogTests
{
    private static DialogResult ShowAndInteract(PageSetupDialog dialog, Action<Form> interact)
    {
        Dispatcher.UIThread.Post(() => interact(Application.OpenForms.Cast<Form>().Last()));

        return dialog.ShowDialog();
    }

    private static void Click(Form form, Control control)
    {
        var screenPoint = control.PointToScreen(new System.Drawing.Point(5, 5));
        var formPoint = form.PointToClient(screenPoint);
        var point = new Avalonia.Point(formPoint.X, formPoint.Y);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    private static Control Named(Form form, string name) => form.Controls.Find(name, searchAllChildren: true)[0];

    /// <summary>Every control under the form, however deeply nested in a group box: Controls.Find needs a non-empty key, so this walks the tree itself.</summary>
    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in AllControls(child)) yield return descendant;
        }
    }

    private static IEnumerable<Button> AllButtons(Form form) => AllControls(form).OfType<Button>();

    private static Button ButtonNamed(Form form, string text) => AllButtons(form).First(b => b.Text == text);

    [AvaloniaFact]
    public void Checking_Landscape_and_pressing_OK_writes_back_the_orientation()
    {
        var dialog = new PageSetupDialog();
        dialog.PageSettings!.Landscape = false;

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, Named(form, "LandscapeRadioButton"));
            Click(form, ButtonNamed(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.True(dialog.PageSettings!.Landscape);
    }

    [AvaloniaFact]
    public void Cancel_leaves_PageSettings_unchanged()
    {
        var dialog = new PageSetupDialog();
        dialog.PageSettings!.Landscape = false;

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, Named(form, "LandscapeRadioButton"));
            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.False(dialog.PageSettings!.Landscape);
    }

    [AvaloniaFact]
    public void Selecting_a_different_paper_size_writes_it_back_on_OK()
    {
        var dialog = new PageSetupDialog();
        dialog.PageSettings!.PaperSize = dialog.PrinterSettings!.PaperSizes[0];

        var result = ShowAndInteract(dialog, form =>
        {
            ((ComboBox)Named(form, "PaperSizeComboBox")).SelectedIndex = 1;
            Click(form, ButtonNamed(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(dialog.PrinterSettings!.PaperSizes[1].PaperName, dialog.PageSettings!.PaperSize.PaperName);
    }

    [AvaloniaFact]
    public void Margins_are_clamped_to_MinMargins_in_hundredths_of_an_inch()
    {
        var dialog = new PageSetupDialog { MinMargins = new Margins(200, 200, 200, 200) };
        dialog.PageSettings!.Margins = new Margins(50, 50, 50, 50);

        var result = ShowAndInteract(dialog, form =>
        {
            var leftMargin = (NumericUpDown)Named(form, "LeftMarginUpDown");

            // The spin box must have raised the margin to the floor by itself; 50/100" is below the 200/100" minimum.
            Assert.Equal(2.00m, leftMargin.Value);
            Assert.Equal(2.00m, leftMargin.Minimum);

            Click(form, ButtonNamed(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(200, dialog.PageSettings!.Margins.Left);
    }

    [AvaloniaFact]
    public void EnableMetric_shows_the_margins_converted_to_millimeters()
    {
        var dialog = new PageSetupDialog { EnableMetric = true };
        dialog.PageSettings!.Margins = new Margins(100, 100, 100, 100);

        var result = ShowAndInteract(dialog, form =>
        {
            var leftMargin = (NumericUpDown)Named(form, "LeftMarginUpDown");

            // 100/100" = 1 inch = 25.4mm, rounded to one decimal.
            Assert.Equal(25.4m, leftMargin.Value);

            Click(form, ButtonNamed(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

}
