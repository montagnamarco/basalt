using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Avalonia.VisualTree;

namespace Basalt.Tests;

/// <summary>
/// The pictures beside the toolbox entries.
/// </summary>
public class ToolboxIconTests
{
    [Fact]
    public void EveryControlInTheCatalogueHasAPicture()
    {
        // A list where some rows have an icon and some do not reads as broken
        // rather than as sparse, and the gap is invisible until the panel is
        // on screen.
        var missing = ToolboxCatalog.Items
            .Where(i => ToolboxIcons.PathDataFor(i.ElementName) is null)
            .Select(i => i.ElementName)
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void ControlsThatLookAlikeAreDrawnDifferently()
    {
        // The point of a picture is telling one row from another, and two
        // rows drawn the same are worse than two rows drawn as nothing: they
        // say the controls are the same when they are not.
        var shapes = ToolboxCatalog.Items
            .Select(i => i.ElementName)
            .Distinct()
            .Select(name => ToolboxIcons.PathDataFor(name))
            .ToList();

        Assert.Equal(shapes.Count, shapes.Distinct().Count());
    }

    [AvaloniaFact]
    public void TheHeadingsSplitTheListIntoItsCategories()
    {
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 240, Height = 600 };

        window.Show();
        window.UpdateLayout();

        var list = panel.GetVisualDescendants().OfType<ListBox>().Single();
        var rows = list.ItemsSource!.Cast<object>().ToList();

        // Every control is still there, and a heading stands before each
        // group of them.
        Assert.Equal(ToolboxCatalog.Items.Count, rows.OfType<ToolboxItem>().Count());
        Assert.Equal(
            ToolboxCatalog.ByCategory().Count(),
            rows.OfType<string>().Count());

        // The first row is a heading, or the first group has none.
        Assert.IsType<string>(rows[0]);

        window.Close();
    }

    [AvaloniaFact]
    public void AHeadingCannotBeInsertedIntoTheForm()
    {
        // A heading is a label, not a control: selecting one and pressing
        // Enter would ask the designer to insert the word "Layout".
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 240, Height = 600 };

        window.Show();
        window.UpdateLayout();

        var list = panel.GetVisualDescendants().OfType<ListBox>().Single();

        var headings = list.GetRealizedContainers()
            .OfType<ListBoxItem>()
            .Where(c => c.DataContext is string)
            .ToList();

        Assert.NotEmpty(headings);
        Assert.All(headings, h => Assert.False(h.Focusable));

        window.Close();
    }

    [AvaloniaFact]
    public void ThePanelDrawsItsIcons()
    {
        // Rendered and looked at. Every assertion above passes on a panel
        // that throws on each draw or paints nothing at all: the model is
        // right and only the screen is wrong, which is the failure a model
        // assertion cannot see.
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 240, Height = 600 };

        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var target = new RenderTargetBitmap(new Avalonia.PixelSize(240, 600));

        target.Render(window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-toolbox.png");

        using (var file = File.Create(path))
            target.Save(file, new PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 2000, "the render is empty");

        window.Close();
    }

    [Fact]
    public void NoIconTriesToDrawACircleWithOneArc()
    {
        // An elliptical arc that ends where it began draws nothing: there is
        // no unique circle through a single point, so the renderer answers
        // with a sliver or with an empty path.
        //
        // Both the radio button and the toggle's knob were written that way
        // and passed every assertion here — the shapes were distinct, present
        // and rendered without throwing. What the panel showed was a letter
        // "e" where a radio button belonged, found by looking at it. A circle
        // takes two half arcs.
        var offenders = ToolboxCatalog.Items
            .Select(i => i.ElementName)
            .Distinct()
            .Where(name => ToolboxIcons.PathDataFor(name) is { } data
                        && HasArcEndingWhereItBegan(data))
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Whether any arc in the path ends within a hair of its own start.
    /// </summary>
    /// <remarks>
    /// Walked rather than matched with an expression: path data has enough
    /// shapes that a regular expression for this either misses cases or
    /// claims ones it should not, and both are hard to see in the pattern.
    /// </remarks>
    private static bool HasArcEndingWhereItBegan(string data)
    {
        var commands = System.Text.RegularExpressions.Regex
            .Matches(data, @"[MLHVAZ][^MLHVAZ]*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        double x = 0, y = 0;

        foreach (System.Text.RegularExpressions.Match command in commands)
        {
            var numbers = System.Text.RegularExpressions.Regex
                .Matches(command.Value, @"-?\d*\.?\d+")
                .Select(m => double.Parse(m.Value,
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            switch (char.ToUpperInvariant(command.Value[0]))
            {
                case 'M' or 'L' when numbers.Length >= 2:
                    (x, y) = (numbers[0], numbers[1]);
                    break;

                case 'H' when numbers.Length >= 1:
                    x = numbers[0];
                    break;

                case 'V' when numbers.Length >= 1:
                    y = numbers[0];
                    break;

                // rx ry rotation large-arc sweep x y
                case 'A' when numbers.Length >= 7:
                    var (endX, endY) = (numbers[5], numbers[6]);

                    if (Math.Abs(endX - x) < 0.5 && Math.Abs(endY - y) < 0.5)
                        return true;

                    (x, y) = (endX, endY);
                    break;
            }
        }

        return false;
    }
}
