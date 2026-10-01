using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Dialogs;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Dialogs;

/// <summary>
/// ColorDialog, driven the way a real application drives it: ShowDialog
/// blocks until the internal ColorDialogForm closes, so a posted action finds
/// it through Application.OpenForms, clicks it, and lets it close, exactly as
/// tests/VisualBasic/InteractionTests.cs drives MsgBox and InputBox.
/// </summary>
public class ColorDialogTests
{
    /// <summary>Runs an action against the dialog once it is on screen, then returns what ShowDialog returned.</summary>
    private static DialogResult ShowAndInteract(ColorDialog dialog, Action<Form> interact)
    {
        Dispatcher.UIThread.Post(() => interact(Application.OpenForms.Cast<Form>().Last()));

        return dialog.ShowDialog();
    }

    private static void Click(Form form, Control control, int offsetX = 5, int offsetY = 5)
    {
        var point = new Avalonia.Point(control.Left + offsetX, control.Top + offsetY);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    private static Button FindButton(Form form, string text) => form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == text);

    /// <summary>The centre of the swatch at this index in an 8-column grid of 18px cells with a 2px gap.</summary>
    private static Avalonia.Point SwatchCentre(Control grid, int index)
    {
        const int cellSize = 18;
        const int gap = 2;
        var column = index % 8;
        var row = index / 8;

        return new Avalonia.Point(grid.Left + column * (cellSize + gap) + cellSize / 2, grid.Top + row * (cellSize + gap) + cellSize / 2);
    }

    [AvaloniaFact]
    public void Clicking_a_basic_swatch_and_pressing_OK_commits_that_colour()
    {
        var dialog = new ColorDialog { Color = Color.Black };
        var expected = ColorDialogForm.BasicColors()[5];

        var result = ShowAndInteract(dialog, form =>
        {
            var grid = form.Controls["BasicColors"]!;
            var point = SwatchCentre(grid, 5);
            form.Host!.MouseDown(point, MouseButton.Left);
            form.Host!.MouseUp(point, MouseButton.Left);

            Click(form, FindButton(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(expected.ToArgb(), dialog.Color.ToArgb());
    }

    [AvaloniaFact]
    public void Cancel_leaves_Color_unchanged_even_after_selecting_a_different_swatch()
    {
        var dialog = new ColorDialog { Color = Color.Black };

        var result = ShowAndInteract(dialog, form =>
        {
            var grid = form.Controls["BasicColors"]!;
            var point = SwatchCentre(grid, 5);
            form.Host!.MouseDown(point, MouseButton.Left);
            form.Host!.MouseUp(point, MouseButton.Left);

            Click(form, FindButton(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal(Color.Black.ToArgb(), dialog.Color.ToArgb());
    }

    [AvaloniaFact]
    public void Adding_the_current_colour_to_a_custom_slot_and_pressing_OK_updates_CustomColors()
    {
        var dialog = new ColorDialog { Color = Color.Black, AllowFullOpen = true, FullOpen = true };
        var expectedRgb = ColorDialogForm.BasicColors()[5];

        var result = ShowAndInteract(dialog, form =>
        {
            var grid = form.Controls["BasicColors"]!;
            var point = SwatchCentre(grid, 5);
            form.Host!.MouseDown(point, MouseButton.Left);
            form.Host!.MouseUp(point, MouseButton.Left);

            Click(form, form.Controls["AddToCustomColors"]!);
            Click(form, FindButton(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        var expectedBgr = expectedRgb.R | (expectedRgb.G << 8) | (expectedRgb.B << 16);
        Assert.Equal(expectedBgr, dialog.CustomColors[0]);
    }

    [AvaloniaFact]
    public void AllowFullOpen_false_greys_out_the_define_custom_colors_button()
    {
        var dialog = new ColorDialog { AllowFullOpen = false };

        var result = ShowAndInteract(dialog, form =>
        {
            var define = form.Controls["DefineCustomColors"]!;
            Assert.True(define.Visible);
            Assert.False(define.Enabled);
            Click(form, FindButton(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
    }

    [AvaloniaFact]
    public void ShowHelp_shows_a_Help_button_that_raises_HelpRequest()
    {
        var dialog = new ColorDialog { ShowHelp = true };
        var raised = false;
        dialog.HelpRequest += (_, _) => raised = true;

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, FindButton(form, "Help"));
            Click(form, FindButton(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.True(raised);
    }

    [Fact]
    public void ShowHelp_false_shows_no_Help_button()
    {
        using var form = new ColorDialogForm(Color.Black, new int[16], allowFullOpen: true, fullOpen: false, showHelp: false);

        Assert.DoesNotContain(form.Controls.Cast<Control>().OfType<Button>(), button => button.Text == "Help");
    }

    [Fact]
    public void CustomColors_getter_returns_a_copy_that_does_not_mutate_the_dialog()
    {
        var dialog = new ColorDialog();

        var colors = dialog.CustomColors;
        colors[0] = 0x00112233;

        Assert.NotEqual(0x00112233, dialog.CustomColors[0]);
    }

    [Fact]
    public void CustomColors_defaults_to_sixteen_white_entries()
    {
        var dialog = new ColorDialog();

        Assert.Equal(16, dialog.CustomColors.Length);
        Assert.All(dialog.CustomColors, value => Assert.Equal(0x00FFFFFF, value));
    }

    [Fact]
    public void CustomColors_setter_pads_a_short_array_with_white_and_truncates_a_long_one()
    {
        var dialog = new ColorDialog { CustomColors = [0x00010203] };
        Assert.Equal(0x00010203, dialog.CustomColors[0]);
        Assert.Equal(0x00FFFFFF, dialog.CustomColors[1]);

        dialog.CustomColors = Enumerable.Range(0, 20).ToArray();
        Assert.Equal(16, dialog.CustomColors.Length);
        Assert.Equal(15, dialog.CustomColors[15]);
    }

    [Fact]
    public void Reset_restores_the_constructor_defaults()
    {
        var dialog = new ColorDialog
        {
            Color = Color.Red,
            AllowFullOpen = false,
            AnyColor = true,
            FullOpen = true,
            ShowHelp = true,
            SolidColorOnly = true,
            CustomColors = [0x00010203],
        };

        dialog.Reset();

        Assert.Equal(Color.Black.ToArgb(), dialog.Color.ToArgb());
        Assert.True(dialog.AllowFullOpen);
        Assert.False(dialog.AnyColor);
        Assert.False(dialog.FullOpen);
        Assert.False(dialog.ShowHelp);
        Assert.False(dialog.SolidColorOnly);
        Assert.All(dialog.CustomColors, value => Assert.Equal(0x00FFFFFF, value));
    }

    [Fact]
    public void Options_reports_the_CHOOSECOLOR_bit_flags()
    {
        var probe = new ColorDialogProbe { FullOpen = true, ShowHelp = true, SolidColorOnly = true, AnyColor = true };

        // CC_RGBINIT | CC_FULLOPEN | CC_SHOWHELP | CC_SOLIDCOLOR | CC_ANYCOLOR, Win32's own bit numbers.
        Assert.Equal(0x00000001 | 0x00000002 | 0x00000008 | 0x00000080 | 0x00000100, probe.OptionsValue);
        Assert.Equal(IntPtr.Zero, probe.InstanceValue);
    }

    [Fact]
    public void Options_sets_CC_PREVENTFULLOPEN_when_AllowFullOpen_is_false()
    {
        var probe = new ColorDialogProbe { AllowFullOpen = false };

        Assert.Equal(0x00000001 | 0x00000004, probe.OptionsValue);
    }

    [Fact]
    public void The_basic_palette_renders_each_cell_as_the_colour_it_stands_for()
    {
        using var form = new ColorDialogForm(Color.Black, new int[16], allowFullOpen: true, fullOpen: false, showHelp: false);
        var grid = form.Controls["BasicColors"]!;

        using var bitmap = new Bitmap(grid.Width, grid.Height, PixelFormat.Format32bppArgb);
        grid.DrawToBitmap(bitmap, new Rectangle(0, 0, grid.Width, grid.Height));

        var colors = ColorDialogForm.BasicColors();

        // Index 40 is the first cell of the bottom, grayscale row: black. Index 47 is the last: white.
        AssertCellColor(bitmap, colors[40], 40);
        AssertCellColor(bitmap, colors[47], 47);
        AssertCellColor(bitmap, colors[0], 0);
    }

    private static void AssertCellColor(Bitmap bitmap, Color expected, int index)
    {
        const int cellSize = 18;
        const int gap = 2;
        var column = index % 8;
        var row = index / 8;
        var x = column * (cellSize + gap) + cellSize / 2;
        var y = row * (cellSize + gap) + cellSize / 2;

        Assert.Equal(expected.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
    }

    /// <summary>Exposes the protected Options and Instance members for the Options test above.</summary>
    private sealed class ColorDialogProbe : ColorDialog
    {
        public int OptionsValue => Options;
        public IntPtr InstanceValue => Instance;
    }
}
