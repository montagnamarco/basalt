using System.Text.RegularExpressions;
using Avalonia;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The spacings the interface uses.
///
/// A survey found fifty distinct paddings and eight gap sizes, including 3,
/// 5, 10 and 14 — close enough to their neighbours that nobody chose them,
/// and far enough that panels side by side did not line up. These keep the
/// values on one scale, by reading the source rather than by trusting that
/// everyone remembers.
/// </summary>
public sealed class SpacingTests
{
    /// <summary>The shell's source files.</summary>
    private static IEnumerable<string> ShellSources()
    {
        var here = Directory.GetCurrentDirectory();

        // Up from bin/Debug/net10.0 to the repository.
        var root = here;

        while (root is not null && !Directory.Exists(Path.Combine(root, "src", "Basalt.Shell")))
            root = Path.GetDirectoryName(root);

        Assert.NotNull(root);

        return Directory.EnumerateFiles(
            Path.Combine(root, "src", "Basalt.Shell"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));
    }

    [Fact]
    public void EveryPaddingIsOnTheScale()
    {
        // BorderThickness is not spacing: a 1-pixel hairline is a line, not a
        // gap, and it is allowed on the scale for that reason.
        var offScale = new List<string>();

        foreach (var file in ShellSources())
        {
            var text = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(text, @"new Thickness\(([0-9,\s]+)\)"))
            {
                var numbers = match.Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(double.Parse)
                    .ToList();

                if (numbers.All(Spacing.IsOnScale)) continue;

                offScale.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(offScale.Count == 0,
            "These are not on the spacing scale:\n" + string.Join("\n", offScale));
    }

    [Fact]
    public void EveryGapIsOnTheScale()
    {
        var offScale = new List<string>();

        foreach (var file in ShellSources())
        {
            var text = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(text, @"Spacing = ([0-9]+)"))
            {
                var value = double.Parse(match.Groups[1].Value);

                if (Spacing.IsOnScale(value)) continue;

                offScale.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(offScale.Count == 0,
            "These gaps are not on the spacing scale:\n" + string.Join("\n", offScale));
    }

    [Fact]
    public void KnowsWhatIsOnTheScaleAndWhatIsNot()
    {
        Assert.True(Spacing.IsOnScale(8));
        Assert.True(Spacing.IsOnScale(0));

        // The ones the survey found, which is why the scale exists.
        Assert.False(Spacing.IsOnScale(3));
        Assert.False(Spacing.IsOnScale(5));
        Assert.False(Spacing.IsOnScale(10));
        Assert.False(Spacing.IsOnScale(14));
    }

    [Fact]
    public void ChecksEverySideOfAThickness()
    {
        Assert.True(Spacing.IsOnScale(new Thickness(8, 4, 8, 4)));

        // One bad side is enough: it is the one that fails to line up.
        Assert.False(Spacing.IsOnScale(new Thickness(8, 4, 8, 5)));
    }

    [Fact]
    public void OffersTheSpacingsThePanelsActuallyNeed()
    {
        Assert.True(Spacing.IsOnScale(Spacing.RowPadding));
        Assert.True(Spacing.IsOnScale(Spacing.PanelPadding));
        Assert.True(Spacing.IsOnScale(Spacing.DialogPadding));
        Assert.True(Spacing.IsOnScale(Spacing.CellPadding));

        Assert.True(Spacing.IsOnScale(Spacing.Tight));
        Assert.True(Spacing.IsOnScale(Spacing.Normal));
        Assert.True(Spacing.IsOnScale(Spacing.Loose));
    }

    [Fact]
    public void ReadsTheShellSourcesAtAll()
    {
        // Otherwise the two tests above would pass by finding nothing.
        Assert.NotEmpty(ShellSources());
        Assert.Contains(ShellSources(), f => Path.GetFileName(f) == "MainWindow.axaml.cs");
    }
}
