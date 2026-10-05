using System.ComponentModel;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataBinding;

public class ListBindingHelperMetadataTests
{
    [Fact]
    public void EmptyGenericListsRetainItemMetadataAndNestedMemberTypes()
    {
        var parents = new BindingList<Parent>();
        Assert.Equal(typeof(Parent), ListBindingHelper.GetListItemType(parents));
        Assert.NotNull(ListBindingHelper.GetListItemProperties(parents)[nameof(Parent.Children)]);
        Assert.Equal(typeof(Child), ListBindingHelper.GetListItemType(parents, nameof(Parent.Children)));
        var childProperties = ListBindingHelper.GetListItemProperties(parents, nameof(Parent.Children), null);
        Assert.NotNull(childProperties[nameof(Child.Number)]);
        Assert.Equal(nameof(Parent), ListBindingHelper.GetListName(parents, null));
    }

    [Fact]
    public void MemberNavigationUsesActualListAndAccessorMetadata()
    {
        var parent = new Parent();
        parent.Children.Add(new Child { Number = 42 });
        var parents = new List<Parent> { parent };
        Assert.Same(parent.Children, ListBindingHelper.GetList(parents, "children"));
        var accessor = TypeDescriptor.GetProperties(typeof(Parent))[nameof(Parent.Children)]!;
        Assert.NotNull(ListBindingHelper.GetListItemProperties(parents, [accessor])[nameof(Child.Number)]);
        Assert.Throws<ArgumentException>(() => ListBindingHelper.GetList(parents, "Missing"));
        Assert.Null(ListBindingHelper.GetList(new List<Parent>(), nameof(Parent.Children)));
        Assert.Equal(typeof(Child), ListBindingHelper.GetListItemType(typeof(Child[])));
    }

    private sealed class Parent
    {
        public List<Child> Children { get; } = [];
    }

    private sealed class Child
    {
        public int Number { get; set; }
    }
}
