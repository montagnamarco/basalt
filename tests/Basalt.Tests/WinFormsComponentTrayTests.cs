using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

public class WinFormsComponentTrayTests
{
    [AvaloniaFact]
    public void Tray_selects_and_deletes_by_field_name_without_a_control_bounds_model()
    {
        var tray = new WinFormsComponentTray();
        var window = new Window { Content = tray };
        window.Show();
        try
        {
            tray.Show([
                new WinFormsDesignerComponent("Timer1", "System.Windows.Forms.Timer"),
                new WinFormsDesignerComponent("ImageList1", "System.Windows.Forms.ImageList"),
            ]);
            tray.Select("Timer1");
            Assert.Equal("Timer1", tray.SelectedName);

            string? selected = null;
            tray.SelectionChanged += name => selected = name;
            var list = Assert.Single(tray.GetVisualDescendants().OfType<ListBox>());
            list.SelectedIndex = 1;
            Assert.Equal("ImageList1", selected);
            tray.Select("Timer1");

            string? deleted = null;
            tray.DeleteRequested += name => deleted = name;
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            Assert.Equal("Timer1", deleted);

            tray.Show([new WinFormsDesignerComponent("ImageList1", "System.Windows.Forms.ImageList")]);
            Assert.Null(tray.SelectedName);
            tray.Select("ImageList1");
            Assert.Equal("ImageList1", tray.SelectedName);
        }
        finally { window.Close(); }
    }
}
