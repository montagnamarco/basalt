using System.ComponentModel;
using System.Windows.Forms.Design;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>
/// PropertyTabCollection.AddTabType(Type, PropertyTabScope)/Clear(scope) and
/// PropertyGrid.RefreshTabs(scope): only Document and Component are valid
/// arguments to Clear/RefreshTabs - Static and Global both throw
/// ArgumentException, since neither is ever something a caller clears.
/// Document and Component tabs are cleared by their own scope and by
/// anything more specific that still reaches them.
/// </summary>
public class PropertyTabScopeTests
{
    private sealed class StaticTab : PropertyTab
    {
        public override string? TabName => "Static";

        public override System.ComponentModel.PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(component, attributes);
    }

    private sealed class DocumentTab : PropertyTab
    {
        public override string? TabName => "Document";

        public override System.ComponentModel.PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(component, attributes);
    }

    private sealed class ComponentTab : PropertyTab
    {
        public override string? TabName => "Component";

        public override System.ComponentModel.PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(component, attributes);
    }

    private static System.Windows.Forms.PropertyGrid GridWithAllThreeScopes()
    {
        var grid = new System.Windows.Forms.PropertyGrid();
        grid.PropertyTabs.AddTabType(typeof(StaticTab), PropertyTabScope.Static);
        grid.PropertyTabs.AddTabType(typeof(DocumentTab), PropertyTabScope.Document);
        grid.PropertyTabs.AddTabType(typeof(ComponentTab), PropertyTabScope.Component);
        return grid;
    }

    private static List<string?> TabNames(System.Windows.Forms.PropertyGrid grid) =>
        grid.PropertyTabs.Cast<PropertyTab>().Select(t => t.TabName).ToList();

    [Fact]
    public void AddTabType_without_a_scope_defaults_to_Global()
    {
        using var grid = new System.Windows.Forms.PropertyGrid();
        grid.PropertyTabs.AddTabType(typeof(DocumentTab));

        Assert.Equal(PropertyTabScope.Global, grid.PropertyTabs.ScopeOf(typeof(DocumentTab)));
    }

    [Theory]
    [InlineData(PropertyTabScope.Static)]
    [InlineData(PropertyTabScope.Global)]
    public void Clear_throws_for_Static_or_Global_since_neither_is_ever_cleared(PropertyTabScope scope)
    {
        using var grid = GridWithAllThreeScopes();

        Assert.Throws<ArgumentException>(() => grid.PropertyTabs.Clear(scope));

        // Nothing was removed: the exception is thrown before any tab is touched.
        Assert.Equal(["Properties", "Static", "Document", "Component"], TabNames(grid));
    }

    [Theory]
    [InlineData(PropertyTabScope.Static)]
    [InlineData(PropertyTabScope.Global)]
    public void RefreshTabs_throws_for_Static_or_Global_too(PropertyTabScope scope)
    {
        using var grid = GridWithAllThreeScopes();

        Assert.Throws<ArgumentException>(() => grid.RefreshTabs(scope));
    }

    [Fact]
    public void Clear_Component_only_removes_the_Component_scoped_tab()
    {
        using var grid = GridWithAllThreeScopes();

        grid.PropertyTabs.Clear(PropertyTabScope.Component);

        Assert.Equal(["Properties", "Static", "Document"], TabNames(grid));
    }

    [Fact]
    public void Clear_Document_removes_Document_and_Component_but_keeps_Static()
    {
        using var grid = GridWithAllThreeScopes();

        grid.PropertyTabs.Clear(PropertyTabScope.Document);

        Assert.Equal(["Properties", "Static"], TabNames(grid));
    }

    [Fact]
    public void RefreshTabs_has_the_same_clearing_semantics_as_Clear()
    {
        using var grid = GridWithAllThreeScopes();

        grid.RefreshTabs(PropertyTabScope.Document);

        Assert.Equal(["Properties", "Static"], TabNames(grid));
    }
}
