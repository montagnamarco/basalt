using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The property grid, on its own rather than through the designer.
/// </summary>
/// <remarks>
/// Tested apart from the designer on purpose: the grid takes a list of
/// properties and reports changes, so a Visual Basic form's properties will go
/// through it one day without any of this having to be rewritten.
/// </remarks>
public sealed class PropertyGridTests
{
    private static DesignableProperty Property(
        string name,
        PropertyEditorKind kind = PropertyEditorKind.Text,
        PropertyCategory category = PropertyCategory.Common,
        string? current = null,
        IReadOnlyList<string>? allowed = null) =>
        new(name, typeof(string), kind, category, allowed ?? [], current);

    private static PropertyGrid Grid(params DesignableProperty[] properties)
    {
        var grid = new PropertyGrid { Properties = properties };

        // Inside a window and rendered: measuring alone leaves the visual
        // tree unbuilt, so every descendant search comes back empty and the
        // test fails for a reason that has nothing to do with the grid.
        var window = new Window { Width = 320, Height = 640, Content = grid };
        window.Show();

        Dispatcher.UIThread.RunJobs();

        return grid;
    }

    /// <summary>Lays the window out again after something changed.</summary>
    private static void Settle(PropertyGrid grid)
    {
        grid.InvalidateMeasure();

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void PropertiesAreGroupedUnderTheirCategory()
    {
        // The categories were in the model all along and the old panel threw
        // them away, which is what made a control with forty properties an
        // undifferentiated list.
        var grid = Grid(
            Property("Text"),
            Property("Width", category: PropertyCategory.Layout),
            Property("Background", category: PropertyCategory.Appearance));

        Assert.Equal(PropertyGrid.SortOrder.ByCategory, grid.Order);

        // Three headings and three rows.
        Assert.Equal(6, grid.Rows.Count);
    }

    [AvaloniaFact]
    public void ACategoryCanBeCollapsed()
    {
        var grid = Grid(
            Property("Text"),
            Property("Width", category: PropertyCategory.Layout));

        var heading = grid.Rows.OfType<Button>().First();

        var before = grid.Rows.Count;
        heading.Command?.Execute(null);
        heading.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.True(grid.Rows.Count < before, "collapsing hid nothing");
    }

    [AvaloniaFact]
    public void EachKindGetsItsOwnEditor()
    {
        // The old panel had three kinds and sent everything else to a text
        // box, so a colour and a margin were both typed by hand.
        var grid = Grid(
            Property("Enabled", PropertyEditorKind.Boolean),
            Property("Width", PropertyEditorKind.Number),
            Property("Background", PropertyEditorKind.Brush),
            Property("Margin", PropertyEditorKind.Thickness),
            Property("Align", PropertyEditorKind.Enumeration, allowed: ["Left", "Right"]));

        var editors = grid.GetVisualDescendants().ToList();

        Assert.Contains(editors, c => c is CheckBox);
        Assert.Contains(editors, c => c is NumericUpDown);
        Assert.Contains(editors, c => c is ComboBox);

        // Four boxes for the four sides of a thickness.
        Assert.True(editors.OfType<TextBox>().Count() >= 4, "the thickness has no four fields");
    }

    [AvaloniaFact]
    public void ASetValueIsToldApartFromADefault()
    {
        var grid = Grid(
            Property("Text", current: "hello"),
            Property("Tag"));

        var labels = grid.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text is "Text" or "Tag")
            .ToDictionary(t => t.Text!, t => t.FontWeight);

        Assert.NotEqual(labels["Text"], labels["Tag"]);
    }

    [AvaloniaFact]
    public void SearchFiltersAsYouType()
    {
        var grid = Grid(Property("Width"), Property("Height"), Property("Text"));

        var search = grid.GetVisualDescendants().OfType<TextBox>().First();
        search.Text = "wid";

        Settle(grid);

        var names = grid.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text)
            .ToList();

        Assert.Contains("Width", names);
        Assert.DoesNotContain("Height", names);
    }

    [AvaloniaFact]
    public void ChangingAValueReportsIt()
    {
        var grid = Grid(Property("Enabled", PropertyEditorKind.Boolean));

        string? changed = null;
        string? value = null;

        grid.PropertyEdited += (name, written) => { changed = name; value = written; };

        var box = grid.GetVisualDescendants().OfType<CheckBox>().Single();
        box.IsChecked = true;

        Assert.Equal("Enabled", changed);
        Assert.Equal("True", value);
    }

    [AvaloniaFact]
    public void OnlyASetPropertyCanBeReset()
    {
        // Clearing a text box is not resetting: an empty string is a value,
        // and some properties mean something different when set to one.
        var withValue = Grid(Property("Text", current: "hello"));
        var without = Grid(Property("Text"));

        Assert.True(
            withValue.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Reset"),
            "a set property offers no reset");

        Assert.DoesNotContain(
            without.GetVisualDescendants().OfType<Button>(),
            b => b.Name == "PART_Reset");
    }

    [AvaloniaFact]
    public void WithNothingSelectedItSaysSo()
    {
        var grid = Grid();

        var texts = grid.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "")
            .ToList();

        Assert.Contains(texts, t => t.Length > 0);
    }

    [AvaloniaFact]
    public void ItDrawsSomething()
    {
        // Rendered and looked at: the arrangement of a grid is the kind of
        // thing that passes every numeric assertion and still comes out
        // unreadable.
        var grid = Grid(
            Property("Text", current: "hello"),
            Property("Width", PropertyEditorKind.Number, PropertyCategory.Layout, "120"),
            Property("Background", PropertyEditorKind.Brush, PropertyCategory.Appearance, "#FF3355"));

        var target = new RenderTargetBitmap(new PixelSize(360, 260));
        target.Render(grid);

        var path = Path.Combine(Path.GetTempPath(), "basalt-propertygrid.png");

        using (var file = File.Create(path))
            target.Save(file, new PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 1000, "the render is empty");
    }
}
