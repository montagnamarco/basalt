using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

public class PropertyGridDefaultValueTests
{
    [AvaloniaFact]
    public void UnsetPropertiesShowTheirDefaultsWithoutWritingAssignments()
    {
        var grid = new PropertyGrid
        {
            Properties =
            [
                Property("Enabled", PropertyEditorKind.Boolean, "True"),
                Property("Width", PropertyEditorKind.Number, "120"),
                Property("Text", PropertyEditorKind.Text, "Button"),
                Property("Mode", PropertyEditorKind.Enumeration, "Second", ["First", "Second"]),
            ],
        };
        var edits = new List<string>();
        grid.PropertyEdited += (name, _) => edits.Add(name);
        var window = new Window { Content = grid, Width = 320, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(Assert.Single(grid.GetVisualDescendants().OfType<CheckBox>()).IsChecked);
        Assert.Equal(120m, Assert.Single(grid.GetVisualDescendants().OfType<NumericUpDown>()).Value);
        Assert.Contains(grid.GetVisualDescendants().OfType<TextBox>(), box => box.Text == "Button");
        Assert.Equal("Second", Assert.Single(grid.GetVisualDescendants().OfType<ComboBox>()).SelectedItem);
        Assert.All(grid.Properties, property => Assert.False(property.IsSet));
        Assert.Empty(edits);
        window.Close();
    }

    private static DesignableProperty Property(string name, PropertyEditorKind kind,
        string defaultValue, IReadOnlyList<string>? allowed = null) =>
        new(name, typeof(string), kind, PropertyCategory.Common, allowed ?? [], null)
        { DefaultValue = defaultValue };
}
