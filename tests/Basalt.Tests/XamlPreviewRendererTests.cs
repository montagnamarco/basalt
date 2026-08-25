using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Tests;

/// <summary>
/// Verifies that the preview uses the real Avalonia engine: it is the premise
/// the whole visual designer rests on.
/// </summary>
public class XamlPreviewRendererTests
{
    [AvaloniaFact]
    public void IstanziaIControlliDichiaratiNelXaml()
    {
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <TextBlock Text="Ciao" />
                <Button Content="Premi" />
              </StackPanel>
            </UserControl>
            """;

        var result = new XamlPreviewRenderer().Render(xaml);

        Assert.True(result.Succeeded, result.Error);
        var panel = Assert.IsType<StackPanel>(Assert.IsType<UserControl>(result.Root).Content);
        Assert.Equal("Ciao", Assert.IsType<TextBlock>(panel.Children[0]).Text);
        Assert.Equal("Premi", Assert.IsType<Button>(panel.Children[1]).Content);
    }

    [AvaloniaFact]
    public void EstraeIlContenutoDiUnaFinestraPreservandoneLeDimensioni()
    {
        // A Window cannot be hosted inside the preview panel.
        const string xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="480" Height="320">
              <Grid><TextBlock Text="contenuto" /></Grid>
            </Window>
            """;

        var result = new XamlPreviewRenderer().Render(xaml);

        Assert.True(result.Succeeded, result.Error);
        var grid = Assert.IsType<Grid>(result.Root);
        Assert.Equal(480, grid.Width);
        Assert.Equal(320, grid.Height);
    }

    [AvaloniaFact]
    public void SegnalaUnErroreLeggibileSenzaSollevareEccezioniQuandoIlXamlNonEValido()
    {
        // While typing, the document is almost always incomplete.
        var result = new XamlPreviewRenderer().Render("<UserControl xmlns=\"https://github.com/avaloniaui\"><Butt");

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [AvaloniaFact]
    public void SegnalaUnErroreQuandoUnControlloNonEsiste()
    {
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"><ControlloInesistente /></UserControl>
            """;

        var result = new XamlPreviewRenderer().Render(xaml);

        Assert.False(result.Succeeded);
        Assert.Contains("ControlloInesistente", result.Error);
    }

    [AvaloniaFact]
    public void RenderizzaUnDocumentoModificatoDalDesigner()
    {
        // Full path: parse, change through an edit, render.
        var document = XamlDocument.Parse("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button Content="Originale" />
            </UserControl>
            """);

        var button = XamlDocument.ControlChildren(document.Root).Single();
        new EditHistory().Execute(new SetAttributeEdit(button, "Content", "Modificato"));

        var result = new XamlPreviewRenderer().Render(document);

        Assert.True(result.Succeeded, result.Error);
        var rendered = Assert.IsType<Button>(Assert.IsType<UserControl>(result.Root).Content);
        Assert.Equal("Modificato", rendered.Content);
    }
}
