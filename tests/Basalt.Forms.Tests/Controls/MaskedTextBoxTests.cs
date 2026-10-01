using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>MaskedTextBox: the MaskedTextProvider-driven mask, typing, Backspace/Delete, selection replace, paste and validation.</summary>
public class MaskedTextBoxTests
{
    private static (Form Form, MaskedTextBox Box) Show(MaskedTextBox box)
    {
        box.Bounds = new Rectangle(10, 10, 200, box.Height);
        var form = new Form { ClientSize = new Size(300, 120) };
        form.Controls.Add(box);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        return (form, box);
    }

    private static void Type(Form form, string text)
    {
        foreach (var character in text) form.Host!.KeyTextInput(character.ToString());
    }

    private static void Press(Form form, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        form.Host!.KeyPressQwerty(key, modifiers);
        form.Host!.KeyReleaseQwerty(key, modifiers);
    }

    // --- Typing: digits land in the mask's edit positions, skipping over its literals ---

    [AvaloniaFact]
    public void Typing_digits_fills_the_mask_skipping_literals()
    {
        var (form, box) = Show(new MaskedTextBox("(000) 000-0000"));

        Type(form, "1234567890");

        Assert.Equal("(123) 456-7890", box.Text);
        Assert.True(box.MaskFull);
        Assert.True(box.MaskCompleted);

        form.Close();
    }

    [AvaloniaFact]
    public void A_partially_filled_mask_shows_the_prompt_character_in_the_remaining_positions()
    {
        // TextMaskFormat defaults to IncludeLiterals, which excludes the prompt: asking to see it explicitly.
        var (form, box) = Show(new MaskedTextBox("(000) 000-0000") { TextMaskFormat = MaskFormat.IncludePromptAndLiterals });

        Type(form, "123");

        Assert.Equal("(123) ___-____", box.Text);
        Assert.False(box.MaskFull);

        form.Close();
    }

    [Fact]
    public void A_different_PromptChar_shows_in_the_unfilled_positions()
    {
        var box = new MaskedTextBox("0000") { PromptChar = '*', TextMaskFormat = MaskFormat.IncludePromptAndLiterals };

        box.Text = "12";

        Assert.Equal("12**", box.Text);
    }

    // --- Rejected input: a character the mask does not accept raises MaskInputRejected and changes nothing ---

    [AvaloniaFact]
    public void A_letter_where_the_mask_wants_a_digit_is_rejected_and_leaves_the_text_alone()
    {
        var (form, box) = Show(new MaskedTextBox("0000") { TextMaskFormat = MaskFormat.IncludePromptAndLiterals });
        var rejections = new List<int>();
        box.MaskInputRejected += (_, e) => rejections.Add(e.Position);

        Type(form, "1a2");

        Assert.Equal("12__", box.Text);
        Assert.Equal([1], rejections);

        form.Close();
    }

    // --- Backspace and Delete: removing through the provider, not the inherited editor ---

    [AvaloniaFact]
    public void Backspace_clears_the_character_before_the_caret_back_to_its_prompt()
    {
        var (form, box) = Show(new MaskedTextBox("0000") { TextMaskFormat = MaskFormat.IncludePromptAndLiterals });
        Type(form, "123");

        Press(form, PhysicalKey.Backspace);

        Assert.Equal("12__", box.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void Delete_clears_the_character_at_the_caret()
    {
        var (form, box) = Show(new MaskedTextBox("0000") { TextMaskFormat = MaskFormat.IncludePromptAndLiterals });
        Type(form, "1234");
        Press(form, PhysicalKey.Home);

        Press(form, PhysicalKey.Delete);

        Assert.Equal("234_", box.Text);

        form.Close();
    }

    // --- Selection replacement: typing over a selected range replaces exactly that range ---

    [AvaloniaFact]
    public void Typing_over_a_selection_replaces_only_the_selected_positions()
    {
        var (form, box) = Show(new MaskedTextBox("0000") { TextMaskFormat = MaskFormat.IncludePromptAndLiterals });
        Type(form, "1234");
        box.Select(0, 2);

        Type(form, "9");

        // Removing the two-position selection shifts "34" left before the "9" lands at the caret.
        Assert.Equal("94__", box.Text);

        form.Close();
    }

    // --- Paste: Ctrl+V goes through the mask too, one character at a time ---

    [AvaloniaFact]
    public void Pasting_fills_the_mask_from_the_caret()
    {
        var (form, box) = Show(new MaskedTextBox("0000") { TextMaskFormat = MaskFormat.IncludePromptAndLiterals });
        Clipboard.SetText("56");

        Type(form, "1");
        Press(form, PhysicalKey.V, RawInputModifiers.Control);

        Assert.Equal("156_", box.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void RejectInputOnFirstFailure_stops_a_paste_at_the_first_rejected_character()
    {
        var (form, box) = Show(new MaskedTextBox("0000") { RejectInputOnFirstFailure = true, TextMaskFormat = MaskFormat.IncludePromptAndLiterals });
        Clipboard.SetText("1a23");

        Press(form, PhysicalKey.V, RawInputModifiers.Control);

        Assert.Equal("1___", box.Text); // Stops at the 'a': the "23" after it never lands.

        form.Close();
    }

    [AvaloniaFact]
    public void Without_RejectInputOnFirstFailure_a_paste_skips_the_bad_character_and_keeps_going()
    {
        var (form, box) = Show(new MaskedTextBox("0000"));
        Clipboard.SetText("1a23");

        Press(form, PhysicalKey.V, RawInputModifiers.Control);

        // The 'a' is skipped; '2' and '3' land right after the '1', leaving the fourth position unfilled.
        Assert.Equal("123", box.Text);

        form.Close();
    }

    // --- InsertKeyMode / IsOverwriteMode: the default overwrites; Insert toggles it ---

    [Fact]
    public void A_fresh_box_starts_in_overwrite_mode()
    {
        var box = new MaskedTextBox("0000");

        Assert.True(box.IsOverwriteMode);
    }

    [AvaloniaFact]
    public void The_Insert_key_toggles_IsOverwriteMode()
    {
        var (form, box) = Show(new MaskedTextBox("0000"));
        var changed = 0;
        box.IsOverwriteModeChanged += (_, _) => changed++;

        Press(form, PhysicalKey.Insert);

        Assert.False(box.IsOverwriteMode);
        Assert.Equal(1, changed);

        form.Close();
    }

    // --- TextMaskFormat: what the Text property returns, independently of what is displayed ---

    [Fact]
    public void ExcludePromptAndLiterals_returns_only_what_the_user_typed()
    {
        var box = new MaskedTextBox("(000) 000-0000") { TextMaskFormat = MaskFormat.ExcludePromptAndLiterals };

        box.Text = "123";

        Assert.Equal("123", box.Text);
    }

    // --- ValidatingType / TypeValidationCompleted / ValidateText ---

    [Fact]
    public void ValidateText_converts_to_ValidatingType_and_raises_TypeValidationCompleted()
    {
        var box = new MaskedTextBox("0000") { ValidatingType = typeof(int) };
        box.Text = "1234";
        TypeValidationEventArgs? seen = null;
        box.TypeValidationCompleted += (_, e) => seen = e;

        var result = box.ValidateText();

        Assert.Equal(1234, result);
        Assert.NotNull(seen);
        Assert.True(seen!.IsValidInput);
    }

    [AvaloniaFact]
    public void Leaving_an_incomplete_box_with_a_ValidatingType_cancels_the_validation()
    {
        var (form, box) = Show(new MaskedTextBox("0000") { ValidatingType = typeof(int) });
        var other = new TextBox();
        form.Controls.Add(other);
        Type(form, "12"); // Two of the four prompt positions are still unfilled: not a valid int.

        var cancelled = false;
        box.Validating += (_, e) => cancelled = e.Cancel;
        other.Focus();

        Assert.True(cancelled);

        form.Close();
    }

    // --- HidePromptOnLeave: the prompt characters disappear once focus leaves, and come back ---

    [AvaloniaFact]
    public void HidePromptOnLeave_hides_the_prompts_once_focus_leaves_and_restores_them_on_focus()
    {
        // HidePromptOnLeave changes only what the inherited editor displays, not the formatted Text
        // property: TextLength (which reads the editor directly) is what actually shows the prompts
        // appearing and disappearing.
        var (form, box) = Show(new MaskedTextBox("0000") { HidePromptOnLeave = true });
        var other = new TextBox();
        form.Controls.Add(other);
        Type(form, "12");
        Assert.Equal(4, box.TextLength); // Focused: "12__".

        other.Focus();
        Assert.Equal(2, box.TextLength); // Unfocused: the two unfilled prompts are gone, leaving "12".

        box.Focus();
        Assert.Equal(4, box.TextLength); // Focused again: the prompts are back.

        form.Close();
    }

    // --- The mask provider itself, and Mask being reset ---

    [Fact]
    public void MaskedTextProvider_exposes_the_same_mask()
    {
        var box = new MaskedTextBox("0000");

        Assert.NotNull(box.MaskedTextProvider);
        Assert.Equal("0000", box.MaskedTextProvider!.Mask);
    }

    [Fact]
    public void An_empty_Mask_behaves_like_a_plain_text_box()
    {
        var box = new MaskedTextBox("0000");
        box.Text = "12";

        box.Mask = string.Empty;
        box.Text = "hello";

        Assert.Null(box.MaskedTextProvider);
        Assert.Equal("hello", box.Text);
    }

    [AvaloniaFact]
    public void Paste_and_AppendText_from_code_go_through_the_mask()
    {
        var box = new MaskedTextBox("000-000");
        Clipboard.SetText("12a3");
        box.Select(0, 0);

        box.Paste();
        Assert.Equal("123-", box.Text.TrimEnd('_').TrimEnd());

        box.AppendText("456");
        Assert.Equal("123-456", box.Text);
        Assert.True(box.MaskCompleted);
    }

    [AvaloniaFact]
    public void Cut_from_code_removes_through_the_mask_and_Undo_does_nothing()
    {
        var box = new MaskedTextBox("000-000") { Text = "123456" };
        box.Select(0, 3);

        box.Cut();
        Assert.Equal("123", Clipboard.GetText());
        Assert.False(box.MaskCompleted);

        var afterCut = box.Text;
        ((TextBoxBase)box).Undo();
        Assert.Equal(afterCut, box.Text);
        Assert.False(((TextBoxBase)box).CanUndo);
    }

    [AvaloniaFact]
    public void A_password_box_never_copies_or_cuts_its_characters()
    {
        var box = new MaskedTextBox("000") { PasswordChar = '*', Text = "123" };
        Clipboard.SetText("before");
        box.SelectAll();

        box.Copy();
        box.Cut();

        Assert.Equal("before", Clipboard.GetText());
        Assert.Equal("123", box.Text);
    }

    [AvaloniaFact]
    public void Shift_Delete_with_no_selection_deletes_nothing_like_Cut()
    {
        var box = new MaskedTextBox("000") { Text = "123" };
        var form = new Form();
        form.Controls.Add(box);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();
        box.Select(0, 0);

        form.Host!.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.Shift);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.Shift);

        Assert.Equal("123", box.Text);
        form.Close();
    }
}
