using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Dialogs;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Dialogs;

/// <summary>
/// FontDialog, driven the way a real application drives it: ShowDialog
/// blocks until the internal FontDialogForm closes, so a posted action finds
/// it through Application.OpenForms, clicks it, and lets it close, exactly as
/// tests/VisualBasic/InteractionTests.cs drives MsgBox and InputBox.
/// </summary>
public class FontDialogTests
{
    private static readonly Font InitialFont = new("Microsoft Sans Serif", 10f, FontStyle.Regular);

    private static DialogResult ShowAndInteract(FontDialog dialog, Action<Form> interact)
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

    private static void ClickListItem(Form form, ListBox list, int index)
    {
        var point = new Avalonia.Point(list.Left + 5, list.Top + index * list.ItemHeight + list.ItemHeight / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    private static Button FindButton(Form form, string text) => form.Controls.Cast<Control>().OfType<Button>().First(b => b.Text == text);

    [AvaloniaFact]
    public void Selecting_Bold_Italic_and_pressing_OK_sets_the_Fonts_style()
    {
        var dialog = new FontDialog { Font = InitialFont };

        var result = ShowAndInteract(dialog, form =>
        {
            var styleList = (ListBox)form.Controls["FontStyle"]!;
            ClickListItem(form, styleList, 3); // "Bold Italic"

            Click(form, FindButton(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.True(dialog.Font.Bold);
        Assert.True(dialog.Font.Italic);
    }

    [AvaloniaFact]
    public void Cancel_leaves_Font_unchanged_even_after_selecting_a_different_style()
    {
        var dialog = new FontDialog { Font = InitialFont };

        var result = ShowAndInteract(dialog, form =>
        {
            var styleList = (ListBox)form.Controls["FontStyle"]!;
            ClickListItem(form, styleList, 2); // "Bold"

            Click(form, FindButton(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal(InitialFont.Style, dialog.Font.Style);
    }

    [AvaloniaFact]
    public void Checking_Strikeout_and_Underline_sets_both_effects_on_OK()
    {
        var dialog = new FontDialog { Font = InitialFont, ShowEffects = true };

        var result = ShowAndInteract(dialog, form =>
        {
            Click(form, form.Controls.Cast<Control>().OfType<CheckBox>().First(c => c.Text == "Strikeout"));
            Click(form, form.Controls.Cast<Control>().OfType<CheckBox>().First(c => c.Text == "Underline"));

            Click(form, FindButton(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.True(dialog.Font.Strikeout);
        Assert.True(dialog.Font.Underline);
    }

    [Fact]
    public void ShowEffects_false_hides_the_effects_checkboxes()
    {
        using var form = new FontDialogForm(InitialFont, Color.Black, showEffects: false, showColor: false, showApply: false, showHelp: false, fixedPitchOnly: false, minSize: 0, maxSize: 0);

        Assert.All(form.Controls.Cast<Control>().OfType<CheckBox>(), check => Assert.False(check.Visible));
    }

    [Fact]
    public void MinSize_and_MaxSize_clamp_a_typed_size()
    {
        using var form = new FontDialogForm(InitialFont, Color.Black, showEffects: true, showColor: false, showApply: false, showHelp: false, fixedPitchOnly: false, minSize: 10, maxSize: 20);
        var sizeText = (TextBox)form.Controls["FontSize"]!;

        sizeText.Text = "999";
        Assert.Equal(20f, form.SelectedFont.Size);

        sizeText.Text = "2";
        Assert.Equal(10f, form.SelectedFont.Size);
    }

    [AvaloniaFact]
    public void ShowColor_lets_the_user_pick_a_named_colour_committed_on_OK()
    {
        var dialog = new FontDialog { Font = InitialFont, ShowColor = true, Color = Color.Black };

        var result = ShowAndInteract(dialog, form =>
        {
            var colorList = (ListBox)form.Controls["FontColor"]!;
            ClickListItem(form, colorList, 1); // "Red"

            Click(form, FindButton(form, "OK"));
        });

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(Color.Red.ToArgb(), dialog.Color.ToArgb());
    }

    [AvaloniaFact]
    public void ShowApply_raises_Apply_without_closing_and_the_change_survives_a_later_Cancel()
    {
        var dialog = new FontDialog { Font = InitialFont, ShowApply = true };
        var appliedSize = 0f;
        dialog.Apply += (_, _) => appliedSize = dialog.Font.Size;

        var result = ShowAndInteract(dialog, form =>
        {
            var sizeText = (TextBox)form.Controls["FontSize"]!;
            sizeText.Text = "18";

            Click(form, FindButton(form, "Apply"));
            Click(form, FindButton(form, "Cancel"));
        });

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal(18f, appliedSize);
        Assert.Equal(18f, dialog.Font.Size);
    }

    [AvaloniaFact]
    public void ShowHelp_shows_a_Help_button_that_raises_HelpRequest()
    {
        var dialog = new FontDialog { ShowHelp = true };
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
    public void FixedPitchOnly_narrows_the_family_list_to_monospaced_faces()
    {
        using var unfiltered = new FontDialogForm(InitialFont, Color.Black, showEffects: true, showColor: false, showApply: false, showHelp: false, fixedPitchOnly: false, minSize: 0, maxSize: 0);
        using var filtered = new FontDialogForm(InitialFont, Color.Black, showEffects: true, showColor: false, showApply: false, showHelp: false, fixedPitchOnly: true, minSize: 0, maxSize: 0);

        var unfilteredCount = ((ListBox)unfiltered.Controls["FontFamily"]!).Items.Count;
        var filteredCount = ((ListBox)filtered.Controls["FontFamily"]!).Items.Count;

        Assert.True(filteredCount > 0, "Expected at least one fixed-pitch family to be installed.");
        Assert.True(filteredCount < unfilteredCount, $"Expected filtering to narrow the list: {filteredCount} was not less than {unfilteredCount}.");
    }

    [Fact]
    public void Reset_restores_the_constructor_defaults()
    {
        var dialog = new FontDialog
        {
            Font = new Font("Microsoft Sans Serif", 24f, FontStyle.Bold),
            Color = Color.Red,
            AllowScriptChange = false,
            FixedPitchOnly = true,
            FontMustExist = true,
            MaxSize = 40,
            MinSize = 4,
            ScriptsOnly = true,
            ShowApply = true,
            ShowColor = true,
            ShowEffects = false,
            ShowHelp = true,
        };

        dialog.Reset();

        Assert.Equal(SystemFonts.DefaultFont.Name, dialog.Font.Name);
        Assert.Equal(SystemFonts.DefaultFont.Size, dialog.Font.Size);
        Assert.Equal(Color.Black.ToArgb(), dialog.Color.ToArgb());
        Assert.True(dialog.AllowScriptChange);
        Assert.False(dialog.FixedPitchOnly);
        Assert.False(dialog.FontMustExist);
        Assert.Equal(0, dialog.MaxSize);
        Assert.Equal(0, dialog.MinSize);
        Assert.False(dialog.ScriptsOnly);
        Assert.False(dialog.ShowApply);
        Assert.False(dialog.ShowColor);
        Assert.True(dialog.ShowEffects);
        Assert.False(dialog.ShowHelp);
    }

    [Fact]
    public void Options_reports_the_CHOOSEFONT_bit_flags()
    {
        var probe = new FontDialogProbe { ShowApply = true, ShowHelp = true, FixedPitchOnly = true, FontMustExist = true };

        // CF_SCREENFONTS | CF_INITTOLOGFONTSTRUCT | CF_EFFECTS | CF_APPLY | CF_SHOWHELP | CF_FIXEDPITCHONLY | CF_FORCEFONTEXIST.
        var expected = 0x00000001 | 0x00000040 | 0x00000100 | 0x00000200 | 0x00000004 | 0x00004000 | 0x00010000;
        Assert.Equal(expected, probe.OptionsValue);
    }

    [Fact]
    public void HookProc_is_not_supported_on_this_platform()
    {
        var probe = new FontDialogProbe();

        Assert.Throws<PlatformNotSupportedException>(() => probe.CallHookProc());
    }

    /// <summary>Exposes the protected Options member and the overridden HookProc for the tests above.</summary>
    private sealed class FontDialogProbe : FontDialog
    {
        public int OptionsValue => Options;

        public IntPtr CallHookProc() => HookProc(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
