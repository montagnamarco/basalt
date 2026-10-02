using System.Globalization;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.TaskDialogs;

/// <summary>
/// TaskDialog: shown modally like MessageBox, built from a TaskDialogPage's
/// buttons, radio buttons, verification check box, expander, footnote and
/// progress bar.
/// </summary>
public class TaskDialogTests
{
    /// <summary>Runs an action against the dialog once it is on screen, then returns the clicked button.</summary>
    private static TaskDialogButton ShowAndInteract(Action<Form> interact, TaskDialogPage page)
    {
        Dispatcher.UIThread.Post(() => interact(Application.OpenForms.Cast<Form>().Last()));

        // A dialog the interaction failed to close is closed after a while (TaskDialog.Close works
        // whatever AllowCancel says), so a regression fails with a wrong result instead of hanging.
        var safetyNet = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        safetyNet.Tick += (_, _) =>
        {
            safetyNet.Stop();
            page.BoundDialog?.Close();
        };
        safetyNet.Start();

        try
        {
            return TaskDialog.ShowDialog(page);
        }
        finally
        {
            safetyNet.Stop();
        }
    }

    private static Button FindButton(Form form, string text) =>
        form.Controls.OfType<Button>().First(button => button.Text == text);

    [AvaloniaFact]
    public void A_standard_button_is_returned_when_clicked()
    {
        var page = new TaskDialogPage { Caption = "Title", Text = "Save?" };
        page.Buttons.Add(TaskDialogButton.OK);
        page.Buttons.Add(TaskDialogButton.Cancel);

        var result = ShowAndInteract(form => FindButton(form, "OK").PerformClick(), page);

        Assert.Equal(TaskDialogButton.OK, result);
    }

    [AvaloniaFact]
    public void A_custom_button_instance_is_returned_when_clicked()
    {
        var page = new TaskDialogPage { Text = "Continue?" };
        var custom = page.Buttons.Add("Do it");
        page.Buttons.Add("Don't");

        var result = ShowAndInteract(form => FindButton(form, "Do it").PerformClick(), page);

        Assert.Same(custom, result);
    }

    [AvaloniaFact]
    public void A_command_link_button_raises_Click_and_is_returned()
    {
        var page = new TaskDialogPage { Text = "Pick one" };
        var clicked = false;
        var link = new TaskDialogCommandLinkButton("First option", "More about it");
        link.Click += (_, _) => clicked = true;
        page.Buttons.Add(link);

        var result = ShowAndInteract(form => FindButton(form, "First option\nMore about it").PerformClick(), page);

        Assert.Same(link, result);
        Assert.True(clicked);
    }

    [AvaloniaFact]
    public void AllowCloseDialog_false_keeps_the_dialog_open_until_another_button_closes_it()
    {
        var page = new TaskDialogPage { Text = "Apply?" };
        var apply = page.Buttons.Add("Apply", allowCloseDialog: false);
        page.Buttons.Add("Done");

        var applyStillOpen = false;

        var result = ShowAndInteract(form =>
        {
            FindButton(form, "Apply").PerformClick();
            applyStillOpen = form.Visible;
            FindButton(form, "Done").PerformClick();
        }, page);

        Assert.True(applyStillOpen);
        Assert.Equal("Done", result.Text);
    }

    [AvaloniaFact]
    public void DefaultButton_is_invoked_by_Enter()
    {
        var page = new TaskDialogPage { Text = "Go?" };
        page.Buttons.Add("First");
        var second = page.Buttons.Add("Second");
        page.DefaultButton = second;

        var result = ShowAndInteract(
            form => form.Host!.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None),
            page);

        Assert.Same(second, result);
    }

    [AvaloniaFact]
    public void AllowCancel_without_a_cancel_button_answers_Cancel_when_the_dialog_is_closed()
    {
        var page = new TaskDialogPage { Text = "Only OK", AllowCancel = true };
        page.Buttons.Add(TaskDialogButton.OK);

        var result = ShowAndInteract(form => form.Close(), page);

        Assert.Equal(TaskDialogButton.Cancel, result);
    }

    [AvaloniaFact]
    public void Escape_answers_Cancel_when_AllowCancel_is_set_even_without_a_Cancel_button()
    {
        var page = new TaskDialogPage { Text = "Only OK", AllowCancel = true };
        page.Buttons.Add(TaskDialogButton.OK);

        var result = ShowAndInteract(
            form => form.Host!.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None),
            page);

        Assert.Equal(TaskDialogButton.Cancel, result);
    }

    [AvaloniaFact]
    public void Escape_does_nothing_when_AllowCancel_is_not_set_and_there_is_no_Cancel_button()
    {
        var page = new TaskDialogPage { Text = "Only OK", AllowCancel = false };
        page.Buttons.Add(TaskDialogButton.OK);

        var result = ShowAndInteract(form =>
        {
            form.Host!.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.Equal(TaskDialogButton.OK, result);
    }

    [AvaloniaFact]
    public void Closing_is_blocked_when_the_page_does_not_allow_cancelling()
    {
        var page = new TaskDialogPage { Text = "Only OK", AllowCancel = false };
        page.Buttons.Add(TaskDialogButton.OK);

        var closingAttempts = new List<bool>();

        var result = ShowAndInteract(form =>
        {
            // The dialog has no Cancel button and does not allow cancelling: the close
            // attempt must come back with Cancel = true instead of ending the dialog.
            form.FormClosing += (_, e) => closingAttempts.Add(e.Cancel);
            form.Close();
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.True(closingAttempts[0]);
        Assert.Equal(TaskDialogButton.OK, result);
    }

    [AvaloniaFact]
    public void Radio_buttons_check_exclusively_and_raise_CheckedChanged()
    {
        var page = new TaskDialogPage { Text = "Pick a size" };
        var small = page.RadioButtons.Add("Small");
        var large = page.RadioButtons.Add("Large");
        small.Checked = true;

        var raisedOnLarge = false;
        large.CheckedChanged += (_, _) => raisedOnLarge = true;
        page.Buttons.Add(TaskDialogButton.OK);

        var result = ShowAndInteract(form =>
        {
            form.Controls.OfType<RadioButton>().First(r => r.Text == "Large").Checked = true;
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.Equal(TaskDialogButton.OK, result);
        Assert.False(small.Checked);
        Assert.True(large.Checked);
        Assert.True(raisedOnLarge);
    }

    [AvaloniaFact]
    public void The_verification_check_box_toggles_and_raises_CheckedChanged()
    {
        var page = new TaskDialogPage { Text = "Remember?" };
        var verification = new TaskDialogVerificationCheckBox("Don't ask again");
        page.Verification = verification;
        page.Buttons.Add(TaskDialogButton.OK);

        var raised = false;
        verification.CheckedChanged += (_, _) => raised = true;

        ShowAndInteract(form =>
        {
            form.Controls.OfType<CheckBox>().First().Checked = true;
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.True(verification.Checked);
        Assert.True(raised);
    }

    [AvaloniaFact]
    public void The_expander_toggles_and_raises_ExpandedChanged()
    {
        var page = new TaskDialogPage { Text = "Details" };
        var expander = new TaskDialogExpander("The extra text") { CollapsedButtonText = "Show details" };
        page.Expander = expander;
        page.Buttons.Add(TaskDialogButton.OK);

        var raised = false;
        expander.ExpandedChanged += (_, _) => raised = true;
        var hadExtraLabelWhenExpanded = false;

        ShowAndInteract(form =>
        {
            FindButton(form, "Show details").PerformClick();
            hadExtraLabelWhenExpanded = form.Controls.OfType<Label>().Any(l => l.Text == "The extra text");
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.True(expander.Expanded);
        Assert.True(raised);
        Assert.True(hadExtraLabelWhenExpanded);
    }

    [AvaloniaFact]
    public void The_progress_bar_value_and_marquee_state_reach_the_rendered_control()
    {
        var page = new TaskDialogPage { Text = "Working" };
        page.ProgressBar = new TaskDialogProgressBar { Value = 40, State = TaskDialogProgressBarState.Marquee };
        page.Buttons.Add(TaskDialogButton.OK);

        var value = 0;
        var style = ProgressBarStyle.Blocks;

        ShowAndInteract(form =>
        {
            var bar = form.Controls.OfType<ProgressBar>().First();
            value = bar.Value;
            style = bar.Style;
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.Equal(40, value);
        Assert.Equal(ProgressBarStyle.Marquee, style);
    }

    [AvaloniaFact]
    public void The_footnote_text_is_shown()
    {
        var page = new TaskDialogPage { Text = "Done" };
        page.Footnote = new TaskDialogFootnote("A footnote");
        page.Buttons.Add(TaskDialogButton.OK);

        var hasFootnoteLabel = false;

        ShowAndInteract(form =>
        {
            hasFootnoteLabel = form.Controls.OfType<Label>().Any(l => l.Text == "A footnote");
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.True(hasFootnoteLabel);
    }

    [AvaloniaFact]
    public void Created_and_Destroyed_fire_once_around_the_dialog_being_shown()
    {
        var page = new TaskDialogPage { Text = "Go" };
        page.Buttons.Add(TaskDialogButton.OK);

        var order = new List<string>();
        page.Created += (_, _) => order.Add("created");
        page.Destroyed += (_, _) => order.Add("destroyed");

        ShowAndInteract(form => FindButton(form, "OK").PerformClick(), page);

        Assert.Equal(["created", "destroyed"], order);
    }

    [AvaloniaFact]
    public void Navigate_replaces_the_shown_content_while_the_dialog_stays_open()
    {
        var firstPage = new TaskDialogPage { Text = "First page" };
        firstPage.Buttons.Add(TaskDialogButton.OK);

        var secondPage = new TaskDialogPage { Text = "Second page" };
        var secondCreated = false;
        secondPage.Created += (_, _) => secondCreated = true;
        var closeButton = secondPage.Buttons.Add("Close this");

        var sawSecondText = false;

        var result = ShowAndInteract(form =>
        {
            firstPage.Navigate(secondPage);
            sawSecondText = form.Controls.OfType<Label>().Any(l => l.Text == "Second page");
            FindButton(form, "Close this").PerformClick();
        }, firstPage);

        Assert.True(sawSecondText);
        Assert.True(secondCreated);
        Assert.Same(closeButton, result);
    }

    [AvaloniaFact]
    public void TaskDialog_Close_closes_the_dialog_from_code_through_BoundDialog()
    {
        var page = new TaskDialogPage { Text = "Waiting", AllowCancel = true };
        page.Buttons.Add(TaskDialogButton.OK);

        var result = ShowAndInteract(_ => page.BoundDialog!.Close(), page);

        Assert.Equal(TaskDialogButton.Cancel, result);
    }

    [Fact]
    public void Standard_buttons_compare_equal_across_separate_instances_but_custom_buttons_do_not()
    {
        Assert.Equal(TaskDialogButton.OK, TaskDialogButton.OK);
        Assert.True(TaskDialogButton.OK == TaskDialogButton.OK);
        Assert.NotEqual(new TaskDialogButton("A"), new TaskDialogButton("A"));
    }

    [AvaloniaFact]
    public void DefaultButton_matches_a_standard_button_by_kind_not_by_instance()
    {
        // TaskDialogButton.OK returns a new instance every time it is read, so the one
        // assigned to DefaultButton is never the same instance as the one in Buttons.
        var page = new TaskDialogPage { Text = "Go?" };
        page.Buttons.Add(TaskDialogButton.Cancel);
        page.Buttons.Add(TaskDialogButton.OK);
        page.DefaultButton = TaskDialogButton.OK;

        var result = ShowAndInteract(
            form => form.Host!.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None),
            page);

        Assert.Equal(TaskDialogButton.OK, result);
    }

    [AvaloniaFact]
    public void A_Click_handler_that_closes_the_dialog_itself_is_not_overridden_by_the_button_that_was_clicked()
    {
        var page = new TaskDialogPage { Text = "Go?", AllowCancel = true };
        var apply = page.Buttons.Add("Apply");
        apply.Click += (_, _) => page.BoundDialog!.Close();

        var result = ShowAndInteract(form => FindButton(form, "Apply").PerformClick(), page);

        // The handler's own Close() already answered Cancel; the Apply click must not overwrite it.
        Assert.Equal(TaskDialogButton.Cancel, result);
    }

    [AvaloniaFact]
    public void A_Click_handler_that_navigates_is_not_followed_by_closing_the_dialog_with_the_old_button()
    {
        var firstPage = new TaskDialogPage { Text = "First" };
        var secondPage = new TaskDialogPage { Text = "Second" };
        var finish = secondPage.Buttons.Add("Finish");

        var next = firstPage.Buttons.Add("Next");
        next.Click += (_, _) => firstPage.Navigate(secondPage);

        var sawSecondPage = false;

        var result = ShowAndInteract(form =>
        {
            FindButton(form, "Next").PerformClick();
            sawSecondPage = form.Controls.OfType<Label>().Any(l => l.Text == "Second");
            FindButton(form, "Finish").PerformClick();
        }, firstPage);

        Assert.True(sawSecondPage);
        Assert.Same(finish, result);
    }

    [Fact]
    public void A_button_already_owned_by_another_page_cannot_be_added_to_a_second_page()
    {
        var firstPage = new TaskDialogPage();
        var button = firstPage.Buttons.Add("Shared");

        var secondPage = new TaskDialogPage();

        Assert.Throws<InvalidOperationException>(() => secondPage.Buttons.Add(button));
    }

    [Fact]
    public void A_radio_button_already_owned_by_another_page_cannot_be_added_to_a_second_page()
    {
        var firstPage = new TaskDialogPage();
        var radioButton = firstPage.RadioButtons.Add("Shared");

        var secondPage = new TaskDialogPage();

        Assert.Throws<InvalidOperationException>(() => secondPage.RadioButtons.Add(radioButton));
    }

    [AvaloniaFact]
    public void Buttons_cannot_be_added_removed_or_replaced_while_their_page_is_shown()
    {
        var page = new TaskDialogPage { Text = "Go" };
        page.Buttons.Add(TaskDialogButton.OK);

        Exception? addFailure = null;
        Exception? removeFailure = null;
        Exception? setFailure = null;

        var result = ShowAndInteract(form =>
        {
            addFailure = Record.Exception(() => page.Buttons.Add("Late"));
            removeFailure = Record.Exception(() => page.Buttons.RemoveAt(0));
            setFailure = Record.Exception(() => page.Buttons[0] = new TaskDialogButton("Replacement"));
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.IsType<InvalidOperationException>(addFailure);
        Assert.IsType<InvalidOperationException>(removeFailure);
        Assert.IsType<InvalidOperationException>(setFailure);
        Assert.Equal(TaskDialogButton.OK, result);
    }

    [AvaloniaFact]
    public void Radio_buttons_cannot_be_added_removed_or_replaced_while_their_page_is_shown()
    {
        var page = new TaskDialogPage { Text = "Go" };
        page.RadioButtons.Add("Small");
        page.Buttons.Add(TaskDialogButton.OK);

        Exception? addFailure = null;

        var result = ShowAndInteract(form =>
        {
            addFailure = Record.Exception(() => page.RadioButtons.Add("Large"));
            FindButton(form, "OK").PerformClick();
        }, page);

        Assert.IsType<InvalidOperationException>(addFailure);
        Assert.Equal(TaskDialogButton.OK, result);
    }

    [AvaloniaFact]
    public void Destroyed_sees_BoundDialog_still_set_both_when_closing_and_when_navigating_away()
    {
        TaskDialog? boundDialogDuringClose = null;
        var closePage = new TaskDialogPage { Text = "Go" };
        closePage.Destroyed += (_, _) => boundDialogDuringClose = closePage.BoundDialog;
        closePage.Buttons.Add(TaskDialogButton.OK);

        ShowAndInteract(form => FindButton(form, "OK").PerformClick(), closePage);

        Assert.NotNull(boundDialogDuringClose);

        TaskDialog? boundDialogDuringNavigateAway = null;
        var firstPage = new TaskDialogPage { Text = "First" };
        firstPage.Destroyed += (_, _) => boundDialogDuringNavigateAway = firstPage.BoundDialog;
        var secondPage = new TaskDialogPage { Text = "Second" };
        secondPage.Buttons.Add(TaskDialogButton.OK);

        ShowAndInteract(form =>
        {
            firstPage.Navigate(secondPage);
            FindButton(form, "OK").PerformClick();
        }, firstPage);

        Assert.NotNull(boundDialogDuringNavigateAway);
    }

    [AvaloniaFact]
    public void Standard_button_captions_follow_the_current_UI_culture_like_MessageBox_does()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("it-IT");

        try
        {
            Assert.Equal("Chiudi", TaskDialogButton.Close.Text);
            Assert.Equal("&Sì", TaskDialogButton.Yes.Text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
