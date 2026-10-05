using System.ComponentModel;
using System.Drawing;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsExpandablePropertiesTests
{
    [Fact]
    public void DesignerMetadataRetainsReadOnlyRowsAndOriginalCategoriesWithoutRunningCustomConverters()
    {
        var properties = WinFormsPropertyInspector.DescribeDesigner(new MetadataControl());
        var width = Assert.Single(properties, property => property.Name == nameof(MetadataControl.Width));
        Assert.Equal("Layout", width.CategoryName);
        Assert.Equal("The client width.", width.Description);
        Assert.False(width.IsReadOnly);

        var readOnly = Assert.Single(properties, property => property.Name == nameof(MetadataControl.ReadOnly));
        Assert.True(readOnly.IsReadOnly);
        Assert.Equal("9", WinFormsPropertyInspector.ReadDesignerValue(new MetadataControl(), nameof(MetadataControl.ReadOnly)));

        var custom = Assert.Single(properties, property => property.Name == nameof(MetadataControl.Custom));
        Assert.True(custom.IsReadOnly);
        Assert.DoesNotContain(properties, property => property.Name == nameof(MetadataControl.Hidden));
    }

    [Fact]
    public void ReadOnlyCollectionGettersCanStillOpenTheBuiltInItemsAndColumnsEditors()
    {
        using var listBox = new Forms.ListBox();
        using var grid = new Forms.DataGridView();
        var items = Assert.Single(WinFormsPropertyInspector.DescribeDesigner(listBox), property => property.Name == "Items");
        var columns = Assert.Single(WinFormsPropertyInspector.DescribeDesigner(grid), property => property.Name == "Columns");
        Assert.True(items.IsCollection);
        Assert.True(items.HasCustomEditor);
        Assert.False(items.IsReadOnly);
        Assert.True(columns.IsCollection);
        Assert.True(columns.HasCustomEditor);
        Assert.False(columns.IsReadOnly);
    }

    [Fact]
    public void ButtonBackColorRemainsBrowsableForTheDesignerColorEditor()
    {
        using var button = new Forms.Button();
        var backColor = Assert.Single(WinFormsPropertyInspector.DescribeDesigner(button),
            property => property.Name == nameof(button.BackColor));

        Assert.False(backColor.IsReadOnly);
        Assert.True(backColor.IsExpandable);
        Assert.True(backColor.HasCustomEditor);
    }

    [Fact]
    public void PointAndPaddingChildEditsRebuildOneParentValueWithoutMutatingThePreview()
    {
        using var button = new Forms.Button
        {
            Location = new Point(4, 8),
            Padding = new Forms.Padding(1, 2, 3, 4),
        };
        var children = WinFormsExpandableProperties.Children(button, nameof(button.Location), isExplicit: true);
        var x = Assert.Single(children, child => child.Name == "X");
        Assert.Equal(nameof(button.Location), x.ParentName);
        Assert.Equal(1, x.Depth);
        Assert.Equal("4", x.CurrentValue);

        var moved = Assert.IsType<Point>(WinFormsExpandableProperties.Rebuild(button, nameof(button.Location), "Y", "23"));
        Assert.Equal(new Point(4, 23), moved);
        Assert.Equal(new Point(4, 8), button.Location);

        var padding = Assert.IsType<Forms.Padding>(WinFormsExpandableProperties.Rebuild(button, nameof(button.Padding), "Top", "12"));
        Assert.Equal(new Forms.Padding(1, 12, 3, 4), padding);
        Assert.Equal(new Forms.Padding(1, 2, 3, 4), button.Padding);
        Assert.Throws<FormatException>(() => WinFormsExpandableProperties.Rebuild(button, nameof(button.Location), "X", "invalid"));
    }

    [Fact]
    public void ColorAndFontChildEditsPreserveOtherChannelsAndFontSettings()
    {
        using var button = new Forms.Button
        {
            BackColor = Color.FromArgb(160, 10, 20, 30),
        };
        var color = Assert.IsType<Color>(WinFormsExpandableProperties.Rebuild(button, nameof(button.BackColor), "R", "44"));
        Assert.Equal(Color.FromArgb(160, 44, 20, 30), color);
        Assert.Equal(Color.FromArgb(160, 10, 20, 30), button.BackColor);

        var originalFont = button.Font;
        using var resized = Assert.IsType<Font>(WinFormsExpandableProperties.Rebuild(button, nameof(button.Font), "Size", "13.5"));
        Assert.Equal(13.5f, resized.Size);
        Assert.Equal(originalFont.Name, resized.Name);
        Assert.Equal(originalFont.Style, resized.Style);
        Assert.Equal(originalFont.Unit, resized.Unit);
        Assert.Equal(originalFont.Name, button.Font.Name);
        Assert.Equal(originalFont.Size, button.Font.Size);
        Assert.Equal(originalFont.Style, button.Font.Style);
        using var bold = Assert.IsType<Font>(WinFormsExpandableProperties.Rebuild(button, nameof(button.Font), "Bold", "True"));
        Assert.True(bold.Bold);
        Assert.Equal(originalFont.Size, bold.Size);
        Assert.Throws<OverflowException>(() => WinFormsExpandableProperties.Rebuild(button, nameof(button.BackColor), "A", "300"));
    }

    [Fact]
    public void ParentDisplayTextRoundTripsThroughBuiltInInvariantConverters()
    {
        using var button = new Forms.Button
        {
            Location = new Point(-5, 20),
            Size = new Size(90, 30),
            Padding = new Forms.Padding(1, 2, 3, 4),
        };
        foreach (var name in new[] { nameof(button.Location), nameof(button.Size), nameof(button.Padding), nameof(button.Font) })
        {
            var property = TypeDescriptor.GetProperties(button)[name]!;
            var text = WinFormsPropertyInspector.ReadDesignerValue(button, name);
            Assert.NotNull(text);
            var parsed = property.Converter.ConvertFromInvariantString(text);
            if (parsed is Font font)
            {
                var expected = button.Font;
                Assert.Equal(expected.Name, font.Name);
                Assert.Equal(expected.Size, font.Size);
                Assert.Equal(expected.Style, font.Style);
            }
            else Assert.Equal(property.GetValue(button), parsed);
        }
    }

    private sealed class MetadataControl
    {
        [Category("Layout"), Description("The client width.")]
        public int Width { get; set; } = 120;

        public int ReadOnly => 9;

        [TypeConverter(typeof(ExplodingConverter))]
        public object Custom { get; set; } = new();

        [Browsable(false)]
        public string Hidden { get; set; } = "";
    }

    private sealed class ExplodingConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            throw new InvalidOperationException("Application converters must not run in the designer.");
    }
}
