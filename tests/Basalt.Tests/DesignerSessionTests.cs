using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Designer.Toolbox;

namespace Basalt.Tests;

public class DesignerSessionTests
{
    private static DesignerSession NewSession(string? xaml = null) =>
        new(XamlDocument.Parse(xaml ?? """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.Finestra">
              <StackPanel>
                <Button Content="Uno" />
              </StackPanel>
            </Window>
            """), SourceLanguage.VisualBasic);

    private static ToolboxItem Bottone => ToolboxCatalog.Items.First(i => i.ElementName == "Button");

    [Fact]
    public void InserisceUnControlloDentroIlContenitoreSelezionato()
    {
        var session = NewSession();
        var panel = XamlDocument.ControlChildren(session.Document.Root).Single();
        session.Select(panel);

        var inserted = session.InsertFromToolbox(Bottone);

        Assert.Equal(2, XamlDocument.ControlChildren(panel).Count());
        Assert.Same(inserted, session.Selection);
    }

    [Fact]
    public void InserisceAccantoAUnControlloNonContenitore()
    {
        // With a Button selected, the new control goes into the panel that holds it.
        var session = NewSession();
        var panel = XamlDocument.ControlChildren(session.Document.Root).Single();
        session.Select(XamlDocument.ControlChildren(panel).Single());

        session.InsertFromToolbox(Bottone);

        Assert.Equal(2, XamlDocument.ControlChildren(panel).Count());
    }

    [Fact]
    public void ScriveLeProprietaComeAttributiXaml()
    {
        var session = NewSession();
        session.Select(XamlDocument.ControlChildren(session.Document.Root).Single());

        session.SetProperty("Orientation", "Horizontal");

        Assert.Contains("""Orientation="Horizontal" """.TrimEnd(), session.Document.ToXaml());
    }

    [Fact]
    public void RimuoveLaProprietaQuandoIlValoreEVuoto()
    {
        var session = NewSession();
        session.Select(XamlDocument.ControlChildren(session.Document.Root).Single());
        session.SetProperty("Orientation", "Horizontal");

        session.SetProperty("Orientation", "");

        Assert.DoesNotContain("Orientation", session.Document.ToXaml());
    }

    [Fact]
    public void AnnullaERipristinaUnInserimento()
    {
        var session = NewSession();
        var panel = XamlDocument.ControlChildren(session.Document.Root).Single();
        session.Select(panel);
        session.InsertFromToolbox(Bottone);

        session.Undo();
        Assert.Single(XamlDocument.ControlChildren(panel));

        session.Redo();
        Assert.Equal(2, XamlDocument.ControlChildren(panel).Count());
    }

    [Fact]
    public void AnnullaUnaEliminazioneRipristinandoLaPosizione()
    {
        var session = NewSession("""
            <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="A.B">
              <StackPanel>
                <Button Content="Uno" /><Button Content="Due" /><Button Content="Tre" />
              </StackPanel>
            </Window>
            """);
        var panel = XamlDocument.ControlChildren(session.Document.Root).Single();
        session.Select(XamlDocument.ControlChildren(panel).ElementAt(1));

        session.RemoveSelected();
        session.Undo();

        var contenuti = XamlDocument.ControlChildren(panel)
            .Select(e => e.Attribute("Content")!.Value).ToList();
        Assert.Equal(["Uno", "Due", "Tre"], contenuti);
    }

    [Fact]
    public void NonEliminaLaRadiceDelDocumento()
    {
        var session = NewSession();
        session.Select(session.Document.Root);

        session.RemoveSelected();

        Assert.NotNull(session.Document.Xml.Root);
    }

    [Fact]
    public void ImpostaIlNomeUsandoXName()
    {
        var session = NewSession();
        var panel = XamlDocument.ControlChildren(session.Document.Root).Single();
        session.Select(XamlDocument.ControlChildren(panel).Single());

        session.SetName("PulsanteSalva");

        Assert.Equal("PulsanteSalva", XamlDocument.GetName(session.Selection!));
    }

    [AvaloniaFact]
    public void RenderizzaIlDocumentoDopoLeModifiche()
    {
        var session = NewSession();
        var panel = XamlDocument.ControlChildren(session.Document.Root).Single();
        session.Select(panel);
        session.InsertFromToolbox(Bottone);

        var result = session.Render();

        Assert.True(result.Succeeded, result.Error);
        var rendered = Assert.IsType<StackPanel>(result.Root);
        Assert.Equal(2, rendered.Children.Count);
    }

    [Fact]
    public void ProduceCodeBehindVisualBasicCoerenteConIlDocumento()
    {
        var code = NewSession().GenerateCodeBehind();

        Assert.Contains("Namespace App", code);
        Assert.Contains("Partial Public Class Finestra", code);
        Assert.Contains("Inherits Window", code);
    }

    [Fact]
    public void PreservaICommentiScrittiAMano()
    {
        // The XAML file is the source of truth: the designer must not rewrite it from scratch.
        var session = NewSession("""
            <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="A.B">
              <!-- commento importante -->
              <StackPanel />
            </Window>
            """);
        session.Select(XamlDocument.ControlChildren(session.Document.Root).Single());
        session.SetProperty("Orientation", "Horizontal");

        Assert.Contains("<!-- commento importante -->", session.Document.ToXaml());
    }
}
