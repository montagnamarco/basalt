using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>
/// The GridItem tree PropertyGrid builds with TypeDescriptor: categories,
/// BrowsableAttribute filtering, an ExpandableObjectConverter property's own
/// sub-properties, the three sort modes, and SelectedGridItemChanged.
/// </summary>
public class PropertyGridItemsTests
{
    private class Address
    {
        public string City { get; set; } = "Rome";
    }

    private class Sample
    {
        [Category("General")]
        public string Name { get; set; } = "Alpha";

        [Category("General")]
        public int Age { get; set; } = 10;

        [Browsable(false)]
        public string Hidden { get; set; } = "secret";

        [Category("Nested")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public Address Location { get; set; } = new();
    }

    private static List<GridItem> Flatten(GridItem item)
    {
        var all = new List<GridItem> { item };
        foreach (GridItem child in item.GridItems) all.AddRange(Flatten(child));

        return all;
    }

    private static List<GridItem> FlattenAll(System.Windows.Forms.PropertyGrid grid)
    {
        var all = new List<GridItem>();
        foreach (GridItem item in grid.Root.GridItems) all.AddRange(Flatten(item));

        return all;
    }

    [Fact]
    public void Properties_are_grouped_into_their_CategoryAttribute_categories()
    {
        var grid = new System.Windows.Forms.PropertyGrid { SelectedObject = new Sample(), PropertySort = PropertySort.CategorizedAlphabetical };

        var categoryLabels = new List<string?>();
        foreach (GridItem item in grid.Root.GridItems)
        {
            Assert.Equal(GridItemType.Category, item.GridItemType);
            categoryLabels.Add(item.Label);
        }

        Assert.Contains("General", categoryLabels);
        Assert.Contains("Nested", categoryLabels);

        var general = grid.Root.GridItems["General"]!;
        var propertyLabels = new List<string?>();
        foreach (GridItem property in general.GridItems) propertyLabels.Add(property.Label);

        // CategorizedAlphabetical sorts within the category: Age before Name, though Name is declared first.
        Assert.Equal(["Age", "Name"], propertyLabels);
    }

    [Fact]
    public void A_Browsable_false_property_never_appears_in_the_tree()
    {
        var grid = new System.Windows.Forms.PropertyGrid { SelectedObject = new Sample() };

        var all = FlattenAll(grid);

        Assert.DoesNotContain(all, item => item.Label == "Hidden");
    }

    [Fact]
    public void An_ExpandableObjectConverter_property_opens_its_own_sub_object_s_properties()
    {
        var grid = new System.Windows.Forms.PropertyGrid { SelectedObject = new Sample(), PropertySort = PropertySort.Alphabetical };

        var location = FlattenAll(grid).Single(item => item.Label == "Location");

        Assert.True(location.Expandable);
        Assert.False(location.Expanded);

        var city = location.GridItems["City"];
        Assert.NotNull(city);
        Assert.Equal("Rome", city!.Value);
    }

    [Fact]
    public void Alphabetical_sort_is_flat_with_no_category_rows()
    {
        var grid = new System.Windows.Forms.PropertyGrid { SelectedObject = new Sample(), PropertySort = PropertySort.Alphabetical };

        var labels = new List<string?>();
        foreach (GridItem item in grid.Root.GridItems)
        {
            Assert.Equal(GridItemType.Property, item.GridItemType);
            labels.Add(item.Label);
        }

        Assert.Equal(["Age", "Location", "Name"], labels);
    }

    [Fact]
    public void Categorized_without_Alphabetical_keeps_declaration_order_within_a_category()
    {
        var grid = new System.Windows.Forms.PropertyGrid { SelectedObject = new Sample(), PropertySort = PropertySort.Categorized };

        var general = grid.Root.GridItems["General"]!;
        var labels = new List<string?>();
        foreach (GridItem property in general.GridItems) labels.Add(property.Label);

        // Categorized (not CategorizedAlphabetical) keeps Name before Age, the order Sample declares them.
        Assert.Equal(["Name", "Age"], labels);
    }

    [Fact]
    public void Selecting_a_grid_item_raises_SelectedGridItemChanged_with_the_old_and_the_new_item()
    {
        var grid = new System.Windows.Forms.PropertyGrid { SelectedObject = new Sample(), PropertySort = PropertySort.Alphabetical };
        var target = FlattenAll(grid).Single(item => item.Label == "Name");

        GridItem? oldSeen = null;
        GridItem? newSeen = null;
        grid.SelectedGridItemChanged += (_, e) =>
        {
            oldSeen = e.OldSelection;
            newSeen = e.NewSelection;
        };

        var selected = target.Select();

        Assert.True(selected);
        Assert.Same(target, newSeen);
        Assert.Null(oldSeen);
        Assert.Same(target, grid.SelectedGridItem);
    }
}
