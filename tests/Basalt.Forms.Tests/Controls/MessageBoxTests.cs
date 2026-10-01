using System.ComponentModel;
using System.Globalization;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>MessageBox: its buttons, the keys that answer it, and its validation.</summary>
public class MessageBoxTests
{
    /// <summary>Presses a key in the message box once it is on screen, then returns what Show returned.</summary>
    private static DialogResult ShowAndPress(PhysicalKey key, Func<DialogResult> show, List<string>? captions = null, string? typed = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var box = Application.OpenForms.Cast<Form>().Last();
            captions?.AddRange(box.Controls.OfType<Button>().Select(button => button.Text));
            box.Host!.KeyPressQwerty(key, RawInputModifiers.None);

            // The character a key types arrives separately, as WM_CHAR after WM_KEYDOWN.
            if (typed is not null) box.Host.KeyTextInput(typed);
        });

        return show();
    }

    [AvaloniaFact]
    public void Enter_answers_with_the_default_button()
    {
        var result = ShowAndPress(PhysicalKey.Enter, () => MessageBox.Show("Save?", "Title", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2));

        Assert.Equal(DialogResult.No, result);
    }

    [AvaloniaFact]
    public void Escape_cancels_when_there_is_a_cancel_button_and_answers_OK_on_an_OK_box()
    {
        Assert.Equal(DialogResult.Cancel, ShowAndPress(PhysicalKey.Escape, () => MessageBox.Show("Go?", "Title", MessageBoxButtons.OKCancel)));
        Assert.Equal(DialogResult.OK, ShowAndPress(PhysicalKey.Escape, () => MessageBox.Show("Done")));
    }

    [AvaloniaFact]
    public void The_buttons_are_the_ones_asked_for_in_the_users_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("it-IT");

        try
        {
            var captions = new List<string>();
            ShowAndPress(PhysicalKey.Enter, () => MessageBox.Show("Continue?", "Title", MessageBoxButtons.YesNo), captions);

            Assert.Equal(["&Sì", "&No"], captions);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [AvaloniaFact]
    public void A_mnemonic_answers_with_its_button()
    {
        Assert.Equal(DialogResult.Retry, ShowAndPress(PhysicalKey.R, () => MessageBox.Show("Failed", "Title", MessageBoxButtons.AbortRetryIgnore), typed: "r"));
    }

    [Fact]
    public void Invalid_arguments_are_refused_before_anything_is_shown()
    {
        Assert.Throws<InvalidEnumArgumentException>(() => MessageBox.Show("x", "y", (MessageBoxButtons)99));
        Assert.Throws<ArgumentException>(() => MessageBox.Show(new Form(), "x", "y", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, MessageBoxOptions.ServiceNotification));
    }
}
