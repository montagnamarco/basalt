using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// How closely the rows of a settings page sit.
///
/// Every row used to be 16 apart whatever it was, and Fluent gives a check box
/// a minimum height of 32 for a control about 20 high: two switches about the
/// same thing ended up 48 apart, with almost an empty line between them, and
/// the page read as a list of unrelated things.
/// </summary>
public sealed class SettingsCompactnessTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-compact-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private SettingsWindow Open()
    {
        var window = new SettingsWindow(
            new SettingsStore(Path.Combine(_root, "settings.json")), null);

        window.Show();

        for (var i = 0; i < 5; i++) window.UpdateLayout();

        return window;
    }

    [AvaloniaFact]
    public void ACheckBoxIsNoTallerThanItNeedsToBe()
    {
        var window = Open();

        var boxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();

        Assert.NotEmpty(boxes);

        // Fluent's own minimum is 32, which is half again what the control
        // draws.
        Assert.All(boxes, b => Assert.True(b.Bounds.Height <= 24,
            $"a check box is {b.Bounds.Height} high"));

        window.Close();
    }

    [AvaloniaFact]
    public void TwoSwitchesInARowSitClose()
    {
        var window = Open();

        var boxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();

        // The gap the page puts between two rows that belong together.
        var consecutive = boxes.Where(b => b.Margin.Top == Spacing.Small).ToList();

        Assert.NotEmpty(consecutive);

        window.Close();
    }

    [AvaloniaFact]
    public void ARowThatIsNotASwitchGetsRoom()
    {
        // A labelled field needs its label to read as belonging to the field
        // below it rather than to the row above.
        var window = Open();

        var spaced = window.GetVisualDescendants().OfType<Control>()
            .Where(c => c.Margin.Top == Spacing.Large)
            .ToList();

        Assert.NotEmpty(spaced);

        window.Close();
    }

    [AvaloniaFact]
    public void TwoSwitchesAreNoFurtherApartThanTheirOwnHeight()
    {
        // The measure that matters and does not depend on which page is
        // showing: two switches about the same thing used to sit 48 apart —
        // a 32-high control with 16 of air — which reads as a gap rather than
        // as a group. Their height plus the small gap is 28.
        var window = Open();

        var boxes = window.GetVisualDescendants().OfType<CheckBox>()
            .Where(b => b.Margin.Top == Spacing.Small)
            .ToList();

        Assert.NotEmpty(boxes);

        Assert.All(boxes, b => Assert.True(b.Bounds.Height + b.Margin.Top <= 30,
            $"a switch takes {b.Bounds.Height + b.Margin.Top} including its gap"));

        window.Close();
    }
}
