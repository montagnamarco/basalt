using System.ComponentModel;
using Basalt.Designer;

namespace Basalt.Tests;

public class WinFormsPropertyInspectorTests
{
    [Fact]
    public void DescribesAndEditsAnActualBasaltFormsControl()
    {
        using var control = new System.Windows.Forms.Button { Text = "Before" };
        var properties = WinFormsPropertyInspector.Describe(control);
        Assert.Contains(properties, property => property.Name == "Text");
        Assert.Contains(properties, property => property.Name == "Enabled" && property.EditorKind == PropertyEditorKind.Boolean);
        WinFormsPropertyInspector.SetValue(control, "Text", "After");
        WinFormsPropertyInspector.SetValue(control, "Width", "180");
        Assert.Equal("After", control.Text);
        Assert.Equal(180, control.Width);
    }

    [Fact]
    public void UsesComponentMetadataAndExcludesInfrastructure()
    {
        var properties = WinFormsPropertyInspector.Describe(typeof(TestControl));
        var width = Assert.Single(properties, property => property.Name == nameof(TestControl.Width));

        Assert.Equal(PropertyEditorKind.Number, width.EditorKind);
        Assert.Equal(PropertyCategory.Layout, width.Category);
        Assert.Equal("120", width.DefaultValue);
        Assert.Equal("The client width.", width.Description);
        Assert.DoesNotContain(properties, property => property.Name == nameof(TestControl.Hidden));
        Assert.DoesNotContain(properties, property => property.Name == nameof(TestControl.Infrastructure));
        Assert.DoesNotContain(properties, property => property.Name == nameof(TestControl.ReadOnly));
        Assert.DoesNotContain(properties, property => property.Name == nameof(TestControl.Children));
    }

    [Fact]
    public void RetainsExplicitDocumentValuesAndAllowsCombinedFlags()
    {
        var properties = WinFormsPropertyInspector.Describe(typeof(TestControl),
            name => name == nameof(TestControl.Width) ? "180" : null);
        Assert.Equal("180", Assert.Single(properties, property => property.Name == nameof(TestControl.Width)).CurrentValue);

        var flags = Assert.Single(properties, property => property.Name == nameof(TestControl.Anchor));
        Assert.Equal(PropertyEditorKind.Text, flags.EditorKind);
        Assert.Empty(flags.AllowedValues);
        var mode = Assert.Single(properties, property => property.Name == nameof(TestControl.Mode));
        Assert.Equal(PropertyEditorKind.Enumeration, mode.EditorKind);
        Assert.Equal(["First", "Second"], mode.AllowedValues);
    }

    [Fact]
    public void ConvertsEditsAndResetsThroughTheComponentDescriptor()
    {
        var control = new TestControl();
        WinFormsPropertyInspector.SetValue(control, nameof(TestControl.Anchor), "Top, Left");
        Assert.Equal(TestAnchor.Top | TestAnchor.Left, control.Anchor);
        WinFormsPropertyInspector.SetValue(control, nameof(TestControl.Width), "180");
        Assert.Equal("180", WinFormsPropertyInspector.ReadValue(control, nameof(TestControl.Width)));
        Assert.True(WinFormsPropertyInspector.ResetValue(control, nameof(TestControl.Width)));
        Assert.Equal(120, control.Width);
        Assert.False(WinFormsPropertyInspector.ResetValue(control, nameof(TestControl.Width)));
    }

    [Fact]
    public void InvalidInputDoesNotChangeTheControl()
    {
        var control = new TestControl();
        Assert.ThrowsAny<Exception>(() => WinFormsPropertyInspector.SetValue(control, nameof(TestControl.Width), "invalid"));
        Assert.Equal(120, control.Width);
        Assert.Throws<InvalidOperationException>(() => WinFormsPropertyInspector.SetValue(control, nameof(TestControl.ReadOnly), "2"));
    }

    [Fact]
    public void UsesInvariantTextForPersistedNumericValues()
    {
        var control = new TestControl();
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            WinFormsPropertyInspector.SetValue(control, nameof(TestControl.Scale), "1.25");
            Assert.Equal(1.25m, control.Scale);
            Assert.Equal("1.25", WinFormsPropertyInspector.ReadValue(control, nameof(TestControl.Scale)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    public sealed class TestControl
    {
        [Category("Layout"), DefaultValue(120), Description("The client width.")]
        public int Width { get; set; } = 120;
        public decimal Scale { get; set; } = 1m;
        public TestAnchor Anchor { get; set; }
        public TestMode Mode { get; set; }
        [Browsable(false)]
        public string Hidden { get; set; } = "";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Infrastructure { get; set; } = "";
        public int ReadOnly => 1;
        public List<object> Children { get; set; } = [];
    }

    [Flags]
    public enum TestAnchor { None = 0, Top = 1, Left = 2 }
    public enum TestMode { First, Second }
}
