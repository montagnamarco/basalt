using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class ImageConverterTests
{
    [Fact]
    public void Converters_list_images_and_keys_from_context_control()
    {
        using var imageList = new ImageList();
        using var image = new Bitmap(2, 2);
        imageList.Images.Add("open", image);
        imageList.Images.Add("save", image);
        using var tree = new TreeView { ImageList = imageList };
        var context = new ConverterContext(tree);

        Assert.Equal([0, 1, -1], new ImageIndexConverter().GetStandardValues(context)!.Cast<int>().ToArray());
        Assert.Equal(["open", "save", ""], new ImageKeyConverter().GetStandardValues(context)!.Cast<string>().ToArray());
        Assert.Equal([0, 1, -1, -2], new TreeViewImageIndexConverter().GetStandardValues(context)!.Cast<int>().ToArray());
    }

    [Fact]
    public void Converters_round_trip_special_image_choices()
    {
        var index = new ImageIndexConverter();
        var treeIndex = new TreeViewImageIndexConverter();
        var key = new ImageKeyConverter();
        var treeKey = new TreeViewImageKeyConverter();

        Assert.Equal(-1, index.ConvertFrom(null, CultureInfo.InvariantCulture, "(none)"));
        Assert.Equal("(none)", index.ConvertTo(null, CultureInfo.InvariantCulture, -1, typeof(string)));
        Assert.Equal(-1, treeIndex.ConvertFrom(null, CultureInfo.InvariantCulture, "(default)"));
        Assert.Equal(-2, treeIndex.ConvertFrom(null, CultureInfo.InvariantCulture, "(none)"));
        Assert.Equal("(default)", treeIndex.ConvertTo(null, CultureInfo.InvariantCulture, -1, typeof(string)));
        Assert.Equal(string.Empty, key.ConvertFrom(null, CultureInfo.InvariantCulture, "(none)"));
        Assert.Equal("(default)", treeKey.ConvertTo(null, CultureInfo.InvariantCulture, "", typeof(string)));
        Assert.Equal(string.Empty, treeKey.ConvertFrom(null, CultureInfo.InvariantCulture, "(default)"));
    }

    [Fact]
    public void Related_image_list_attribute_selects_named_list()
    {
        using var imageList = new ImageList();
        using var image = new Bitmap(2, 2);
        imageList.Images.Add("small", image);
        using var listView = new ListView { SmallImageList = imageList };
        var property = TypeDescriptor.CreateProperty(typeof(ListView), "ImageIndex", typeof(int),
            new RelatedImageListAttribute(nameof(ListView.SmallImageList)));
        var context = new ConverterContext(listView, property);

        Assert.Equal([0, -1], new ImageIndexConverter().GetStandardValues(context)!.Cast<int>().ToArray());
    }

    [Fact]
    public void Image_properties_expose_the_converters_to_the_designer()
    {
        Assert.IsType<TreeViewImageIndexConverter>(TypeDescriptor.GetProperties(typeof(TreeView))[nameof(TreeView.ImageIndex)]!.Converter);
        Assert.IsType<TreeViewImageKeyConverter>(TypeDescriptor.GetProperties(typeof(TreeView))[nameof(TreeView.ImageKey)]!.Converter);
        Assert.IsType<TreeViewImageIndexConverter>(TypeDescriptor.GetProperties(typeof(TreeNode))[nameof(TreeNode.ImageIndex)]!.Converter);
        Assert.IsType<TreeViewImageKeyConverter>(TypeDescriptor.GetProperties(typeof(TreeNode))[nameof(TreeNode.ImageKey)]!.Converter);
        Assert.IsType<ImageIndexConverter>(TypeDescriptor.GetProperties(typeof(ListViewItem))[nameof(ListViewItem.ImageIndex)]!.Converter);
        Assert.IsType<ImageKeyConverter>(TypeDescriptor.GetProperties(typeof(ColumnHeader))[nameof(ColumnHeader.ImageKey)]!.Converter);
    }

    [Fact]
    public void List_item_state_index_uses_the_state_images_instead_of_small_icons()
    {
        using var stateImages = new ImageList();
        using var smallImages = new ImageList();
        using var image = new Bitmap(2, 2);
        stateImages.Images.Add(image);
        stateImages.Images.Add(image);
        smallImages.Images.Add(image);
        using var list = new ListView { StateImageList = stateImages, SmallImageList = smallImages };
        var item = list.Items.Add("Alpha");
        var property = TypeDescriptor.GetProperties(item)[nameof(ListViewItem.StateImageIndex)]!;

        Assert.IsType<ListViewItemStateImageIndexConverter>(property.Converter);
        Assert.Equal([0, 1, -1], property.Converter.GetStandardValues(new ConverterContext(item, property))!.Cast<int>());
    }

    private sealed class ConverterContext(object instance, PropertyDescriptor? propertyDescriptor = null) : ITypeDescriptorContext
    {
        public IContainer? Container => null;

        public object Instance => instance;

        public PropertyDescriptor? PropertyDescriptor => propertyDescriptor;

        public object? GetService(Type serviceType) => null;

        public bool OnComponentChanging() => true;

        public void OnComponentChanged()
        {
        }
    }
}
