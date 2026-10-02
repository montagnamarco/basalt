using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>
/// Edits the grid must refuse without throwing and without raising
/// PropertyValueChanged: a read-only property cycled by double-click, text
/// for a property no converter can fill from a string, and a value one of
/// several selected objects' setters rejects.
/// </summary>
public class PropertyGridCommitSafetyTests
{
    private class Locked
    {
        [ReadOnly(true)]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>A type with no TypeConverter of its own: nothing can build one from typed text.</summary>
    private sealed class Token
    {
    }

    private class Holder
    {
        public Token Payload { get; set; } = new();
    }

    private class Picky
    {
        private int _limit = 1;

        public int Limit
        {
            get => _limit;
            set => _limit = value > MaximumAccepted ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
        }

        public int MaximumAccepted { get; init; } = 100;
    }

    private static Form ShowWith(System.Windows.Forms.PropertyGrid grid)
    {
        var form = new Form { ClientSize = new Size(260, 150) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static System.Windows.Forms.PropertyGrid GridFor(params object[] targets) => new()
    {
        SelectedObjects = targets,
        PropertySort = PropertySort.Alphabetical,
        ToolbarVisible = false,
        HelpVisible = false,
        Size = new Size(240, 120),
    };

    private static Avalonia.Point ValueCellOf(System.Windows.Forms.PropertyGrid grid, string label)
    {
        var rowIndex = grid.VisibleRowsWithDepth().FindIndex(r => r.Item.Label == label);
        var cell = grid.ValueBounds(rowIndex);

        return new Avalonia.Point(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
    }

    /// <summary>Types into the value cell and presses Enter; returns what reached Application.ThreadException, which input handlers report to.</summary>
    private static Exception? TypeAndCommit(Form form, System.Windows.Forms.PropertyGrid grid, string label, string text)
    {
        var point = ValueCellOf(grid, label);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
        grid.Editor!.Text = text;

        Exception? escaped = null;
        System.Threading.ThreadExceptionEventHandler capture = (_, e) => escaped = e.Exception;
        Application.ThreadException += capture;
        try
        {
            form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }
        finally
        {
            Application.ThreadException -= capture;
        }

        return escaped;
    }

    [AvaloniaFact]
    public void Double_clicking_a_read_only_bool_does_not_cycle_it()
    {
        var locked = new Locked();
        var grid = GridFor(locked);
        var form = ShowWith(grid);
        var raised = false;
        grid.PropertyValueChanged += (_, _) => raised = true;

        var point = ValueCellOf(grid, "Enabled");
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(locked.Enabled);
        Assert.False(raised);

        form.Close();
    }

    [AvaloniaFact]
    public void Text_for_a_property_no_converter_reads_from_a_string_keeps_the_old_value()
    {
        var holder = new Holder();
        var original = holder.Payload;
        var grid = GridFor(holder);
        var form = ShowWith(grid);
        var raised = false;
        grid.PropertyValueChanged += (_, _) => raised = true;

        var escaped = TypeAndCommit(form, grid, "Payload", "typed text");

        Assert.Null(escaped);
        Assert.Same(original, holder.Payload);
        Assert.False(raised);

        form.Close();
    }

    [AvaloniaFact]
    public void A_value_one_selected_object_rejects_is_rolled_back_on_every_object()
    {
        var lenient = new Picky { MaximumAccepted = 100 };
        var strict = new Picky { MaximumAccepted = 10 };
        var grid = GridFor(lenient, strict);
        var form = ShowWith(grid);
        var raised = false;
        grid.PropertyValueChanged += (_, _) => raised = true;

        var escaped = TypeAndCommit(form, grid, "Limit", "50");

        Assert.Null(escaped);
        Assert.Equal(1, lenient.Limit);
        Assert.Equal(1, strict.Limit);
        Assert.False(raised);

        form.Close();
    }
}
