using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>AllowColumnReorder + ColumnReordered, and ColumnWidthChanging raised while a column divider is being dragged.</summary>
public class ListViewColumnReorderTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form Show(ListView list)
    {
        list.Font = Font;
        list.Bounds = new Rectangle(0, 0, 300, 150);
        var form = new Form { ClientSize = new Size(300, 150) };
        form.Controls.Add(list);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void Dragging_a_header_past_another_column_reorders_them_when_AllowColumnReorder_is_on()
    {
        var list = new ListView { View = View.Details, AllowColumnReorder = true };
        var first = list.Columns.Add("First", 60);
        var second = list.Columns.Add("Second", 60);
        var form = Show(list);
        ColumnHeader? reordered = null;
        list.ColumnReordered += (_, e) => reordered = e.Header;

        form.Host!.MouseDown(new Avalonia.Point(10, 5), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(90, 5));
        form.Host!.MouseUp(new Avalonia.Point(90, 5), MouseButton.Left);

        Assert.Same(first, reordered);
        Assert.Equal(1, list.Columns.IndexOf(first));
        Assert.Equal(0, list.Columns.IndexOf(second));
        form.Close();
    }

    [AvaloniaFact]
    public void A_plain_click_without_dragging_still_fires_ColumnClick_not_a_reorder()
    {
        var list = new ListView { View = View.Details, AllowColumnReorder = true };
        list.Columns.Add("Only", 60);
        var form = Show(list);
        var clicks = 0;
        var reorders = 0;
        list.ColumnClick += (_, _) => clicks++;
        list.ColumnReordered += (_, _) => reorders++;

        form.Host!.MouseDown(new Avalonia.Point(10, 5), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(10, 5), MouseButton.Left);

        Assert.Equal(1, clicks);
        Assert.Equal(0, reorders);
        form.Close();
    }

    [AvaloniaFact]
    public void Dragging_a_column_divider_raises_ColumnWidthChanging_before_applying_the_new_width()
    {
        var list = new ListView { View = View.Details };
        var column = list.Columns.Add("Only", 60);
        var form = Show(list);
        var seenNewWidth = -1;
        list.ColumnWidthChanging += (_, e) => seenNewWidth = e.NewWidth;

        form.Host!.MouseDown(new Avalonia.Point(60, 5), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(80, 5));
        form.Host!.MouseUp(new Avalonia.Point(80, 5), MouseButton.Left);

        Assert.Equal(80, seenNewWidth);
        Assert.Equal(80, column.Width);
        form.Close();
    }

    [AvaloniaFact]
    public void Cancelling_ColumnWidthChanging_keeps_the_original_width()
    {
        var list = new ListView { View = View.Details };
        var column = list.Columns.Add("Only", 60);
        var form = Show(list);
        list.ColumnWidthChanging += (_, e) => e.Cancel = true;

        form.Host!.MouseDown(new Avalonia.Point(60, 5), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(80, 5));
        form.Host!.MouseUp(new Avalonia.Point(80, 5), MouseButton.Left);

        Assert.Equal(60, column.Width);
        form.Close();
    }
}
