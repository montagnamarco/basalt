using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public sealed class WinFormsToolboxGestureTests
{
    [AvaloniaFact]
    public void RealPointerMovementStartsOneDragWithOneSerializableTransferItem()
    {
        var item = new WinFormsToolboxItem(typeof(Forms.Button));
        var toolbox = new ListBox { ItemsSource = new[] { item } };
        var transfers = new List<IDataTransfer>();
        WinFormsToolboxDrag.Attach(toolbox, (_, data, effect) =>
        {
            Assert.Equal(DragDropEffects.Copy, effect);
            transfers.Add(data);
            return Task.FromResult(DragDropEffects.Copy);
        });
        var window = new Window { Content = toolbox, Width = 220, Height = 240 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var row = Assert.Single(toolbox.GetVisualDescendants().OfType<ListBoxItem>());
            var point = row.TranslatePoint(new Point(15, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(2, 2), RawInputModifiers.LeftMouseButton);
            Assert.Empty(transfers);
            window.MouseMove(point + new Vector(8, 0), RawInputModifiers.LeftMouseButton);
            var transfer = Assert.Single(transfers);
            var entry = Assert.Single(transfer.Items);
            Assert.Same(item, transfer.TryGetValue(WinFormsToolboxDrag.Format));
            Assert.Equal(item.DisplayName, transfer.TryGetText());
            Assert.Contains(entry.Formats, format => format.Kind != DataFormatKind.InProcess);
            window.MouseMove(point + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
            Assert.Single(transfers);
            window.MouseUp(point, MouseButton.Left);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ReleaseAndRightClickCannotStartAnOldToolboxDrag()
    {
        var toolbox = new ListBox
        {
            ItemsSource = new[] { new WinFormsToolboxItem(typeof(Forms.Button)) },
        };
        var starts = 0;
        WinFormsToolboxDrag.Attach(toolbox, (_, _, _) =>
        {
            starts++;
            return Task.FromResult(DragDropEffects.Copy);
        });
        var window = new Window { Content = toolbox, Width = 220, Height = 240 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var row = Assert.Single(toolbox.GetVisualDescendants().OfType<ListBoxItem>());
            var point = row.TranslatePoint(new Point(15, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseMove(point + new Vector(8, 0));
            window.MouseDown(point, MouseButton.Right);
            window.MouseMove(point + new Vector(8, 0), RawInputModifiers.RightMouseButton);
            window.MouseUp(point, MouseButton.Right);
            Assert.Equal(0, starts);
        }
        finally
        {
            window.Close();
        }
    }
}
