using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class WinFormsToolboxIconTests
{
    [AvaloniaFact]
    public void TheRealWinFormsToolboxRowsContainVectorIcons()
    {
        var document = new EditorDocumentViewModel(
            "MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 960, Height = 700 };
        window.Show();

        try
        {
            var toolbox = Assert.Single(view.GetVisualDescendants().OfType<ListBox>(), list =>
                list.ItemsSource?.Cast<object>().Any(item => item is WinFormsToolboxItem) == true);
            Assert.Contains(toolbox.GetVisualDescendants().OfType<ToolboxIconView>(), icon =>
                icon.ElementName is { } name && ToolboxIcons.PathDataFor(name) is not null);
            Assert.Equal("DataGrid", WinFormsToolboxVisuals.IconName(typeof(System.Windows.Forms.DataGridView)));
            Assert.Equal("Menu", WinFormsToolboxVisuals.IconName(typeof(System.Windows.Forms.MenuStrip)));
        }
        finally
        {
            window.Close();
        }
    }
}
