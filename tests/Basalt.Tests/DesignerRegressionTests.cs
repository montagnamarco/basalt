using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Tests;

/// <summary>Defects that already showed up, covered so they do not come back.</summary>
public class RegressioniDesignerTests
{
    [Fact]
    public void LUndoRimuoveIlNodoRealmenteInseritoNonIlFrammentoClonato()
    {
        // LINQ-to-XML clones an XElement that already belongs to a document:
        // the edit must undo the node present in the tree, not the original.
        var doc = XamlDocument.Parse("""
            <StackPanel xmlns="https://github.com/avaloniaui"><Button Content="Uno" /></StackPanel>
            """);
        var frammento = XElement.Parse(
            """<Root xmlns="https://github.com/avaloniaui"><Button Content="Nuovo" /></Root>""")
            .Elements().First();

        var edit = new InsertElementEdit(doc.Root, frammento);
        var history = new EditHistory();

        history.Execute(edit);
        Assert.Equal(2, XamlDocument.ControlChildren(doc.Root).Count());
        Assert.NotNull(edit.Inserted);

        history.Undo();
        Assert.Single(XamlDocument.ControlChildren(doc.Root));
    }

    [AvaloniaFact]
    public void RenderizzaUnaFinestraConXClassAncheSeIlTipoNonEAncoraCompilato()
    {
        // This is the normal condition while designing: the code-behind has not
        // been compiled, so the type named by x:Class does not exist.
        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="AppInesistente.FinestraMai Compilata">
              <TextBlock Text="visibile" />
            </Window>
            """;

        var result = new XamlPreviewRenderer().Render(xaml);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("visibile", Assert.IsType<TextBlock>(result.Root).Text);
    }

    [AvaloniaFact]
    public void ConservaXClassNelDocumentoSalvatoAncheSeLAnteprimaLoIgnora()
    {
        // The removal only affects the copy handed to the loader: the file on
        // disk must keep its link to the code-behind.
        var doc = XamlDocument.Parse("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.Finestra"><TextBlock Text="x" /></Window>
            """);

        new XamlPreviewRenderer().Render(doc);

        Assert.Equal("App.Finestra", doc.ClassName);
        Assert.Contains("x:Class=\"App.Finestra\"", doc.ToXaml());
    }
}
