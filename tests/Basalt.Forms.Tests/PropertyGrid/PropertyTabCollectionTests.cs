using System.Collections;
using System.Windows.Forms.Design;
using System.Windows.Forms.PropertyGridInternal;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>PropertyGrid.PropertyTabs: a real, mutable collection - index 0 is always the default PropertiesTab, AddTabType/RemoveTabType construct and dispose real tab instances for the rest.</summary>
public class PropertyTabCollectionTests
{
    private sealed class CustomTab : PropertyTab
    {
        public static bool LastInstanceDisposed { get; private set; }

        public override string? TabName => "Custom";

        public override System.ComponentModel.PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes) =>
            System.ComponentModel.TypeDescriptor.GetProperties(component, attributes);

        protected override void Dispose(bool disposing)
        {
            if (disposing) LastInstanceDisposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void Starts_with_only_the_default_PropertiesTab()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();

        Assert.Equal(1, grid.PropertyTabs.Count);
        Assert.IsType<PropertiesTab>(grid.PropertyTabs[0]);
    }

    [Fact]
    public void AddTabType_constructs_and_lists_a_new_tab_instance()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();

        grid.PropertyTabs.AddTabType(typeof(CustomTab));

        Assert.Equal(2, grid.PropertyTabs.Count);
        Assert.IsType<CustomTab>(grid.PropertyTabs[1]);

        var names = grid.PropertyTabs.Cast<PropertyTab>().Select(t => t.TabName).ToList();
        Assert.Equal(["Properties", "Custom"], names);
    }

    [Fact]
    public void RemoveTabType_disposes_the_removed_instance()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();
        grid.PropertyTabs.AddTabType(typeof(CustomTab));

        grid.PropertyTabs.RemoveTabType(typeof(CustomTab));

        Assert.Equal(1, grid.PropertyTabs.Count);
        Assert.True(CustomTab.LastInstanceDisposed);
    }

    [Fact]
    public void AddTabType_rejects_a_type_that_is_not_a_PropertyTab()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();

        Assert.Throws<ArgumentException>(() => grid.PropertyTabs.AddTabType(typeof(string)));
    }

    [Fact]
    public void The_collection_implements_ICollection_for_CopyTo()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();
        grid.PropertyTabs.AddTabType(typeof(CustomTab));

        var array = new PropertyTab[2];
        ((ICollection)grid.PropertyTabs).CopyTo(array, 0);

        Assert.IsType<PropertiesTab>(array[0]);
        Assert.IsType<CustomTab>(array[1]);
    }
}
