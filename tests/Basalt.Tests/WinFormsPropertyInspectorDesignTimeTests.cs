using System.Collections;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Standard component design-time metadata honoured for any component type, not only
/// built-in Basalt.Forms controls: Browsable, Category, Description, DefaultValue,
/// DesignerSerializationVisibility, a TypeConverter's standard values and expandable
/// properties, and an [Editor(..., typeof(UITypeEditor))] attribute. Every fixture below
/// is a plain class declared in this test assembly, not Basalt.Forms, so a passing test
/// proves the inspector works from reflection alone rather than a hardcoded type name.
/// </summary>
public class WinFormsPropertyInspectorDesignTimeTests
{
    [Fact]
    public void BrowsableFalseHidesAndCategoryAndDescriptionAreCarried()
    {
        var properties = WinFormsPropertyInspector.DescribeDesigner(new SampleComponent());

        Assert.DoesNotContain(properties, p => p.Name == nameof(SampleComponent.Hidden));

        var choice = Assert.Single(properties, p => p.Name == nameof(SampleComponent.Choice));
        Assert.Equal("Custom", choice.CategoryName);
        Assert.Equal("A choice from a closed set.", choice.Description);
    }

    [Fact]
    public void DesignerSerializationVisibilityHiddenIsNeverShown()
    {
        var properties = WinFormsPropertyInspector.DescribeDesigner(new SampleComponent());

        Assert.DoesNotContain(properties, p => p.Name == nameof(SampleComponent.NeverSerialized));
    }

    [Fact]
    public void AnExclusiveTypeConverterStandardValueSetBecomesADropDown()
    {
        var properties = WinFormsPropertyInspector.DescribeDesigner(new SampleComponent());
        var choice = Assert.Single(properties, p => p.Name == nameof(SampleComponent.Choice));

        Assert.Equal(PropertyEditorKind.Enumeration, choice.EditorKind);
        Assert.Equal(new[] { "Low", "Medium", "High" }, choice.AllowedValues);
        Assert.False(choice.IsReadOnly);
    }

    [Fact]
    public void ANonExclusiveTypeConverterStandardValueSetStaysFreeText()
    {
        // GetStandardValuesExclusive false means the values are suggestions over free
        // text: this grid's drop-down editor cannot offer both, so the property keeps
        // its ordinary text row rather than becoming a closed choice list.
        var properties = WinFormsPropertyInspector.DescribeDesigner(new SampleComponent());
        var suggestion = Assert.Single(properties, p => p.Name == nameof(SampleComponent.Suggestion));

        Assert.Equal(PropertyEditorKind.Text, suggestion.EditorKind);
    }

    [Fact]
    public void ATypeConverterThatSupportsGetPropertiesMakesTheRowExpandable()
    {
        var properties = WinFormsPropertyInspector.DescribeDesigner(new SampleComponent());
        var anchor = Assert.Single(properties, p => p.Name == nameof(SampleComponent.Anchor));

        Assert.True(anchor.IsExpandable);
        // SamplePointConverter never claimed it could parse a one-line string back into
        // a SamplePoint, so the top row - unlike Font or Point, whose real converters do -
        // is shown but not edited directly; only the expanded X and Y rows are.
        Assert.True(anchor.IsReadOnly);
    }

    [Fact]
    public void ExpandableChildrenAreListedAndRebuiltGenericallyThroughTheConverter()
    {
        var component = new SampleComponent();

        var children = WinFormsExpandableProperties.Children(component, nameof(SampleComponent.Anchor), isExplicit: true);
        Assert.Equal(new[] { "1", "2" }, children.Select(c => c.DefaultValue));
        Assert.All(children, c => Assert.False(c.IsReadOnly));

        var rebuilt = WinFormsExpandableProperties.Rebuild(component, nameof(SampleComponent.Anchor), nameof(SamplePoint.Y), "9");
        var point = Assert.IsType<SamplePoint>(rebuilt);
        Assert.Equal(1, point.X);
        Assert.Equal(9, point.Y);
    }

    [Fact]
    public void AnEditorAttributeGivesTheRowACustomEditorButton()
    {
        var properties = WinFormsPropertyInspector.DescribeDesigner(new SampleComponent());
        var edited = Assert.Single(properties, p => p.Name == nameof(SampleComponent.Edited));

        Assert.True(edited.HasCustomEditor);
    }

    [Fact]
    public void TheDesignerViewFindsAndInvokesTheSameEditorAttribute()
    {
        // WinFormsDesignerView.PropertyEditors has a compile-time reference to the
        // UITypeEditor base type, where WinFormsPropertyInspector (above) only has the
        // one it resolves by reflection; both must agree on the same attribute.
        var component = new SampleComponent();
        var descriptor = TypeDescriptor.GetProperties(component)[nameof(SampleComponent.Edited)]!;

        var editor = WinFormsDesignerView.TryGetDesignTimeEditor(descriptor);
        Assert.NotNull(editor);
        Assert.Equal(UITypeEditorEditStyle.Modal, editor!.GetEditStyle());

        using var owner = new Forms.Form();
        var changed = WinFormsDesignerView.TryInvokeDesignTimeEditor(
            editor, component, descriptor, currentValue: 1, owner, out var value);

        Assert.True(changed);
        Assert.Equal(42, value);
    }

    private sealed class SampleComponent
    {
        [Browsable(false)]
        public int Hidden { get; set; }

        [Category("Custom")]
        [Description("A choice from a closed set.")]
        [TypeConverter(typeof(ChoiceConverter))]
        public string Choice { get; set; } = "Medium";

        [TypeConverter(typeof(SuggestionConverter))]
        public string Suggestion { get; set; } = "";

        [TypeConverter(typeof(SamplePointConverter))]
        public SamplePoint Anchor { get; set; } = new(1, 2);

        [Editor(typeof(SampleEditor), typeof(UITypeEditor))]
        public int Edited { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int NeverSerialized { get; set; }
    }

    private readonly record struct SamplePoint(int X, int Y);

    private sealed class ChoiceConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(new[] { "Low", "Medium", "High" });
    }

    private sealed class SuggestionConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(new[] { "a", "b" });
    }

    private sealed class SamplePointConverter : TypeConverter
    {
        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection GetProperties(
            ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(SamplePoint), attributes);

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues) =>
            new SamplePoint((int)propertyValues[nameof(SamplePoint.X)]!, (int)propertyValues[nameof(SamplePoint.Y)]!);
    }

    private sealed class SampleEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
            UITypeEditorEditStyle.Modal;

        // Real editors call provider.GetService(typeof(IWindowsFormsEditorService)) and
        // show a dialog or drop-down through it; this one returns a fixed new value
        // directly so the test proves EditValue is reached with a working context and
        // service, without needing a displayed window in a headless test run.
        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            Assert.NotNull(provider);
            Assert.NotNull(provider!.GetService(typeof(System.Windows.Forms.Design.IWindowsFormsEditorService)));
            return 42;
        }
    }
}
