using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Basalt.Designer;

namespace Basalt.Tests;

public class PropertyInspectorTests
{
    [AvaloniaFact]
    public void TrovaLeProprietaRealiDiUnPulsante()
    {
        var properties = PropertyInspector.Describe(typeof(Button));

        Assert.Contains(properties, p => p.Name == "Width" && p.EditorKind == PropertyEditorKind.Number);
        Assert.Contains(properties, p => p.Name == "IsEnabled" && p.EditorKind == PropertyEditorKind.Boolean);
        Assert.Contains(properties, p => p.Name == "Background" && p.EditorKind == PropertyEditorKind.Brush);
        // Four fields rather than one text box: "4,2,4,2" is easy to get
        // wrong by hand and gives no clue which number is which side.
        Assert.Contains(properties,
            p => p.Name == "Margin" && p.EditorKind == PropertyEditorKind.Thickness);
    }

    [AvaloniaFact]
    public void ElencaIValoriAmmessiPerLeEnumerazioni()
    {
        var property = Assert.Single(
            PropertyInspector.Describe(typeof(StackPanel)), p => p.Name == "Orientation");

        Assert.Equal(PropertyEditorKind.Enumeration, property.EditorKind);
        Assert.Contains(nameof(Orientation.Horizontal), property.AllowedValues);
        Assert.Contains(nameof(Orientation.Vertical), property.AllowedValues);
    }

    [AvaloniaFact]
    public void EscludeLeProprietaGestiteAltroveDalDesigner()
    {
        var names = PropertyInspector.Describe(typeof(Button)).Select(p => p.Name).ToList();

        // Content is edited on the design surface, the name in its dedicated field.
        Assert.DoesNotContain("Content", names);
        Assert.DoesNotContain("Name", names);
        Assert.DoesNotContain("Bounds", names);
    }

    [AvaloniaFact]
    public void RaggruppaLeProprietaPerCategoria()
    {
        var properties = PropertyInspector.Describe(typeof(Button));

        Assert.Contains(properties,
            p => p.Name == "Width" && p.Category == PropertyCategory.Layout);
        Assert.Contains(properties,
            p => p.Name == "Background" && p.Category == PropertyCategory.Appearance);
        Assert.Contains(properties,
            p => p.Name == "IsEnabled" && p.Category == PropertyCategory.Behavior);
    }

    [AvaloniaFact]
    public void LeggeIValoriCorrentiTramiteIlCallback()
    {
        var properties = PropertyInspector.Describe(
            typeof(Button),
            name => name == "Width" ? "120" : null);

        Assert.Equal("120", Assert.Single(properties, p => p.Name == "Width").CurrentValue);
    }
}
