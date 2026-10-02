using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>
/// Editing a value cell end to end through real mouse and keyboard input:
/// a click opens the in-place TextBox, Enter commits it through the
/// property's TypeConverter and raises PropertyValueChanged, an invalid
/// string is rejected silently, and Escape cancels without touching the
/// object at all.
/// </summary>
public class PropertyGridEditingTests
{
    private class Widget
    {
        public string Name { get; set; } = "Alpha";

        public int Count { get; set; } = 1;
    }

    private static Form ShowWith(System.Windows.Forms.PropertyGrid grid)
    {
        var form = new Form { ClientSize = new Size(260, 150) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static System.Windows.Forms.PropertyGrid GridFor(object target) => new()
    {
        SelectedObject = target,
        PropertySort = PropertySort.Alphabetical,
        ToolbarVisible = false,
        HelpVisible = false,
        Size = new Size(240, 120),
    };

    /// <summary>Alphabetical and flat: the row index among VisibleRowsWithDepth for a property with this label.</summary>
    private static int RowIndexOf(System.Windows.Forms.PropertyGrid grid, string label) =>
        grid.VisibleRowsWithDepth().FindIndex(r => r.Item.Label == label);

    private static Avalonia.Point CenterOf(Rectangle rectangle) => new(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);

    private static void ClickValueCell(Form form, System.Windows.Forms.PropertyGrid grid, string label)
    {
        var rowIndex = RowIndexOf(grid, label);
        var point = CenterOf(grid.ValueBounds(rowIndex));

        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Clicking_a_value_cell_opens_an_in_place_TextBox_with_the_current_value()
    {
        var widget = new Widget();
        var grid = GridFor(widget);
        var form = ShowWith(grid);

        ClickValueCell(form, grid, "Name");

        Assert.NotNull(grid.Editor);
        Assert.Equal("Alpha", grid.Editor!.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void Enter_commits_the_typed_text_through_the_TypeConverter_and_raises_PropertyValueChanged()
    {
        var widget = new Widget();
        var grid = GridFor(widget);
        var form = ShowWith(grid);

        ClickValueCell(form, grid, "Name");
        grid.Editor!.Text = "Beta";

        PropertyValueChangedEventArgs? raised = null;
        grid.PropertyValueChanged += (_, e) => raised = e;

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal("Beta", widget.Name);
        Assert.NotNull(raised);
        Assert.Equal("Alpha", raised!.OldValue);
        Assert.Equal("Name", raised.ChangedItem?.Label);
        Assert.Null(grid.Editor);

        form.Close();
    }

    [AvaloniaFact]
    public void An_invalid_string_keeps_the_old_value_and_never_raises_PropertyValueChanged()
    {
        var widget = new Widget();
        var grid = GridFor(widget);
        var form = ShowWith(grid);

        ClickValueCell(form, grid, "Count");
        grid.Editor!.Text = "not a number";

        var raised = false;
        grid.PropertyValueChanged += (_, _) => raised = true;

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(1, widget.Count);
        Assert.False(raised);
        Assert.Null(grid.Editor);

        form.Close();
    }

    [AvaloniaFact]
    public void Escape_cancels_the_edit_without_changing_the_object()
    {
        var widget = new Widget();
        var grid = GridFor(widget);
        var form = ShowWith(grid);

        ClickValueCell(form, grid, "Name");
        grid.Editor!.Text = "Changed but abandoned";

        var raised = false;
        grid.PropertyValueChanged += (_, _) => raised = true;

        form.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal("Alpha", widget.Name);
        Assert.False(raised);
        Assert.Null(grid.Editor);

        form.Close();
    }
}
