using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

public sealed class WinFormsDesignerDropTests
{
    [AvaloniaFact]
    public void RoutedDragOverAndDropUseNestedContainerClientCoordinates()
    {
        var form = new Forms.Form { ClientSize = new Drawing.Size(300, 200) };
        var panel = new Forms.Panel
        {
            Bounds = new Drawing.Rectangle(35, 30, 100, 90),
            BorderStyle = Forms.BorderStyle.FixedSingle,
        };
        var button = new Forms.Button { Bounds = new Drawing.Rectangle(10, 10, 50, 30) };
        panel.Controls.Add(button);
        form.Controls.Add(panel);
        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Content = surface, Width = 380, Height = 280 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var received = new List<WinFormsControlDrop>();
            surface.ControlDropRequested += received.Add;
            var transfer = Transfer(typeof(Forms.Label));
            var position = surface.TranslatePoint(new Point(12 + 35 + 1 + 15, 12 + 30 + 1 + 16), window)!.Value;

            var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, window, position, KeyModifiers.None);
            surface.RaiseEvent(over);
            Assert.Equal(DragDropEffects.Copy, over.DragEffects);
            var host = Assert.IsType<Canvas>(surface.Content);
            var preview = Assert.Single(host.Children.OfType<Canvas>(), canvas =>
                canvas.Children.Count == 2);
            Assert.Equal(2, preview.Children.Count);
            var ghost = Assert.IsType<Border>(preview.Children[1]);
            Dispatcher.UIThread.RunJobs();
            var sample = surface.TranslatePoint(new Point(12 + 35 + 1 + 15 + 70,
                12 + 30 + 1 + 16 + 5), window)!.Value;
            var highlighted = ReadPixel(window, sample);
            Assert.True(highlighted.Blue > highlighted.Red + 30,
                $"the drop preview should paint blue over the actual Forms panel; " +
                $"RGB={highlighted.Red},{highlighted.Green},{highlighted.Blue}, " +
                $"ghost=({Canvas.GetLeft(ghost)},{Canvas.GetTop(ghost)}), sample={sample}");

            var drop = new DragEventArgs(DragDrop.DropEvent, transfer, window, position, KeyModifiers.None);
            surface.RaiseEvent(drop);
            Dispatcher.UIThread.RunJobs();
            var cleared = ReadPixel(window, sample);
            Assert.True(cleared.Blue < highlighted.Blue || cleared.Red > highlighted.Red,
                "the drop preview should clear after the routed drop");

            var placement = Assert.Single(received);
            Assert.Equal(typeof(Forms.Label), placement.ControlType);
            Assert.Same(panel, placement.Parent);
            Assert.Equal(new Drawing.Point(15, 16), placement.Location);
        }
        finally { window.Close(); form.Dispose(); }
    }

    [AvaloniaFact]
    public async Task DroppingFromToolboxPersistsNestedLocationAndUndoAsOneEdit()
    {
        var source = WinFormsDesignerDocumentTests.Source;
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            // DragEventArgs is resolved against the surface's host canvas. The
            // property pane can shift that canvas within its surface viewport.
            var host = Assert.IsType<Canvas>(surface.Content);
            var position = host.TranslatePoint(new Point(12 + 140, 12 + 80), window)!.Value;
            var drop = new DragEventArgs(DragDrop.DropEvent, Transfer(typeof(Forms.Button)),
                window, position, KeyModifiers.None);
            surface.RaiseEvent(drop);
            await WaitUntilAsync(() => document.Text.Contains("Friend WithEvents Button2", StringComparison.Ordinal));

            var added = Assert.Single(new WinFormsDesignerDocument(document.Text).Controls,
                control => control.Name == "Button2");
            Assert.Equal("Panel1", added.ParentName);
            Assert.Equal("New System.Drawing.Point(140, 80)", added.Properties["Location"]);
            Assert.Equal("Button2", surface.SelectedControl?.Name);

            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Basalt.Core.Localization.Localizer.Get(
                    Basalt.Core.Localization.StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == source);
        }
        finally { window.Close(); }
    }

    private static DataTransfer Transfer(Type controlType)
    {
        var item = new DataTransferItem();
        item.Set(WinFormsToolboxDrag.Format, new WinFormsToolboxItem(controlType));
        item.SetText(controlType.Name);
        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    private static (byte Red, byte Green, byte Blue) ReadPixel(Window window, Point point)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(380, 280));
        bitmap.Render(window);
        var pixels = new byte[380 * 280 * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 380, 280),
                handle.AddrOfPinnedObject(), pixels.Length, 380 * 4);
        }
        finally { handle.Free(); }
        var index = ((int)point.Y * 380 + (int)point.X) * 4;
        return (pixels[index], pixels[index + 1], pixels[index + 2]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not complete the drop transaction.");
    }
}
