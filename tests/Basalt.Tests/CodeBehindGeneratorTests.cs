using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Tests;

public class CodeBehindGeneratorTests
{
    private const string WindowXaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="MiaApp.Views.FinestraPrincipale">
          <Button Content="Ciao" />
        </Window>
        """;

    [Fact]
    public void GeneraUnaClasseParzialeVisualBasicConNamespaceEBaseCorretti()
    {
        var code = CodeBehindGenerator.Generate(XamlDocument.Parse(WindowXaml), SourceLanguage.VisualBasic);

        Assert.Contains("Namespace MiaApp.Views", code);
        Assert.Contains("Partial Public Class FinestraPrincipale", code);
        Assert.Contains("Inherits Window", code);
        Assert.Contains("AvaloniaXamlLoader.Load(Me)", code);
        Assert.Contains("End Namespace", code);
    }

    [Fact]
    public void UsaUserControlComeBaseQuandoLaRadiceNonEUnaFinestra()
    {
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="MiaApp.Pannello" />
            """;

        var code = CodeBehindGenerator.Generate(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic);

        Assert.Contains("Inherits UserControl", code);
    }

    [Fact]
    public void GestisceUnaClasseSenzaNamespace()
    {
        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Principale" />
            """;

        var code = CodeBehindGenerator.Generate(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic);

        Assert.DoesNotContain("Namespace", code);
        Assert.Contains("Partial Public Class Principale", code);
    }

    [Fact]
    public void SegnalaLAssenzaDiXClass()
    {
        const string xaml = """<Window xmlns="https://github.com/avaloniaui" />""";

        var ex = Assert.Throws<InvalidOperationException>(
            () => CodeBehindGenerator.Generate(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic));

        Assert.Contains("x:Class", ex.Message);
    }

    [Fact]
    public void GeneraUnGestoreDiEventoVisualBasic()
    {
        var handler = CodeBehindGenerator.GenerateEventHandler(
            "PulsanteSalva_Click", "Button", "RoutedEventArgs", SourceLanguage.VisualBasic);

        Assert.Contains("Private Sub PulsanteSalva_Click(sender As Object, e As RoutedEventArgs)", handler);
        Assert.Contains("End Sub", handler);
    }
}
