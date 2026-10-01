using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Components;

/// <summary>
/// HelpProvider's extender properties and what F1 does with them.
/// </summary>
/// <remarks>
/// Most tests raise <see cref="Control.HelpRequested"/> directly through the
/// internal RaiseHelpRequested; one presses a real F1 to prove the key path.
/// </remarks>
public class HelpProviderTests
{
    [Fact]
    public void CanExtend_accepts_only_controls()
    {
        using var provider = new HelpProvider();

        Assert.True(provider.CanExtend(new Control()));
        Assert.False(provider.CanExtend("not a control"));
    }

    [Fact]
    public void HelpString_and_HelpKeyword_read_back_what_was_set()
    {
        var control = new Control();
        using var provider = new HelpProvider();

        Assert.Equal(string.Empty, provider.GetHelpString(control));
        Assert.Equal(string.Empty, provider.GetHelpKeyword(control));
        Assert.Equal(HelpNavigator.AssociateIndex, provider.GetHelpNavigator(control));

        provider.SetHelpString(control, "Enter your name here.");
        provider.SetHelpKeyword(control, "Name field");
        provider.SetHelpNavigator(control, HelpNavigator.Topic);

        Assert.Equal("Enter your name here.", provider.GetHelpString(control));
        Assert.Equal("Name field", provider.GetHelpKeyword(control));
        Assert.Equal(HelpNavigator.Topic, provider.GetHelpNavigator(control));
    }

    [Fact]
    public void ShowHelp_defaults_to_true_and_ResetShowHelp_restores_it()
    {
        var control = new Control();
        using var provider = new HelpProvider();

        Assert.True(provider.GetShowHelp(control));

        provider.SetShowHelp(control, false);
        Assert.False(provider.GetShowHelp(control));

        provider.ResetShowHelp(control);
        Assert.True(provider.GetShowHelp(control));
    }

    /// <summary>F1 (simulated through RaiseHelpRequested) with a help string shows it in a pop-up, with no help file needed.</summary>
    [AvaloniaFact]
    public void F1_with_a_help_string_shows_it_in_a_popup_without_a_help_file()
    {
        var control = new Control();
        using var provider = new HelpProvider();
        provider.SetHelpString(control, "Enter your name here.");

        var openBefore = Popups.All.Count;

        control.RaiseHelpRequested(new HelpEventArgs(new Point(10, 10)));

        Assert.Equal(openBefore + 1, Popups.All.Count);

        foreach (var popup in Popups.All.Skip(openBefore).ToArray()) Popups.Close(popup);
    }

    [AvaloniaFact]
    public void A_real_F1_on_the_focused_control_shows_its_help_string()
    {
        var box = new TextBox();
        var form = new Form();
        form.Controls.Add(box);
        using var provider = new HelpProvider();
        provider.SetHelpString(box, "Enter your name here.");
        form.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        box.Focus();
        var openBefore = Popups.All.Count;

        form.Host!.KeyPressQwerty(Avalonia.Input.PhysicalKey.F1, Avalonia.Input.RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.F1, Avalonia.Input.RawInputModifiers.None);

        Assert.Equal(openBefore + 1, Popups.All.Count);

        foreach (var popup in Popups.All.Skip(openBefore).ToArray()) Popups.Close(popup);
        form.Close();
    }

    /// <summary>
    /// Without a help string, F1 hands the namespace to the system (a browser
    /// for a web address); a help file that does not exist opens nothing,
    /// and neither ever throws out of the key press.
    /// </summary>
    [Fact]
    public void F1_with_only_a_help_namespace_opens_it_through_the_system()
    {
        var opened = new List<string>();
        var original = Help.Opener;
        Help.Opener = opened.Add;
        try
        {
            var control = new Control();
            using var webHelp = new HelpProvider { HelpNamespace = "https://example.org/help" };
            webHelp.SetHelpKeyword(control, "Overview");

            control.RaiseHelpRequested(new HelpEventArgs(new Point(10, 10)));

            var other = new Control();
            using var missingFile = new HelpProvider { HelpNamespace = "no-such-file.chm" };
            missingFile.SetHelpKeyword(other, "Overview");

            other.RaiseHelpRequested(new HelpEventArgs(new Point(10, 10)));

            Assert.Equal(["https://example.org/help"], opened);
        }
        finally
        {
            Help.Opener = original;
        }
    }

    [Fact]
    public void ShowHelp_false_suppresses_F1_entirely()
    {
        var opened = new List<string>();
        var original = Help.Opener;
        Help.Opener = opened.Add;
        try
        {
            var control = new Control();
            using var provider = new HelpProvider { HelpNamespace = "https://example.org/help" };
            provider.SetHelpKeyword(control, "Overview");
            provider.SetShowHelp(control, false);

            control.RaiseHelpRequested(new HelpEventArgs(new Point(10, 10)));

            Assert.Empty(opened);
        }
        finally
        {
            Help.Opener = original;
        }
    }
}
