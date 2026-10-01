using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.VisualBasic;

namespace Basalt.Forms.Tests.VisualBasic;

/// <summary>
/// MsgBox and InputBox, called the way a compiled VB application calls them:
/// through Microsoft.VisualBasic.Interaction (the real Microsoft.VisualBasic.Core
/// assembly), which finds Microsoft.VisualBasic._Interaction in our
/// Microsoft.VisualBasic.Forms by reflection. Proves the assembly answers to
/// the name and signature the VB runtime looks up, not just that our own code
/// calls itself directly.
/// </summary>
public class InteractionTests
{
    /// <summary>Runs an action against the dialog once it is on screen, then returns what the VB call returned.</summary>
    private static T ShowAndInteract<T>(Action<Form> interact, Func<T> call)
    {
        Dispatcher.UIThread.Post(() => interact(Application.OpenForms.Cast<Form>().Last()));

        return call();
    }

    [AvaloniaFact]
    public void MsgBox_through_Interaction_returns_the_pressed_button()
    {
        var result = ShowAndInteract(
            box => box.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None),
            () => Interaction.MsgBox("Save?", MsgBoxStyle.YesNo, "Title"));

        Assert.Equal(MsgBoxResult.Yes, result);
    }

    [AvaloniaFact]
    public void MsgBox_maps_the_icon_default_button_and_cancel_group()
    {
        var result = ShowAndInteract(
            box => box.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None),
            () => Interaction.MsgBox("Go?", MsgBoxStyle.OkCancel | MsgBoxStyle.Critical | MsgBoxStyle.DefaultButton2, "Title"));

        Assert.Equal(MsgBoxResult.Cancel, result);
    }

    [AvaloniaFact]
    public void InputBox_through_Interaction_returns_what_was_typed()
    {
        // The text box starts with the default response selected: typing replaces it.
        var result = ShowAndInteract(box =>
        {
            box.Host!.KeyTextInput("42");
            box.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }, () => Interaction.InputBox("Age?", "Title", "0"));

        Assert.Equal("42", result);
    }

    [AvaloniaFact]
    public void InputBox_returns_empty_when_cancelled()
    {
        var result = ShowAndInteract(
            box => box.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None),
            () => Interaction.InputBox("Age?", "Title", "7"));

        Assert.Equal(string.Empty, result);
    }

    [AvaloniaFact]
    public void A_title_that_is_not_a_string_is_shown_as_text()
    {
        string? title = null;

        ShowAndInteract(box =>
        {
            title = box.Text;
            box.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }, () => Interaction.MsgBox("Hello", MsgBoxStyle.OkOnly, 42));

        Assert.Equal("42", title);
    }

    [AvaloniaFact]
    public void An_omitted_title_is_the_applications()
    {
        string? title = null;

        ShowAndInteract(box =>
        {
            title = box.Text;
            box.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }, () => Interaction.MsgBox("Hello"));

        Assert.False(string.IsNullOrEmpty(title));
        Assert.Equal(Microsoft.VisualBasic._Interaction.DefaultTitle(), title);
    }

    [AvaloniaFact]
    public void InputBox_places_the_box_at_the_position_given_in_twips()
    {
        Point location = default;

        ShowAndInteract(box =>
        {
            location = box.Location;
            box.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        }, () => Interaction.InputBox("Name?", "Title", "", 1500, 3000));

        // 15 twips to a pixel at 96 dpi.
        Assert.Equal(new Point(100, 200), location);
    }
}
