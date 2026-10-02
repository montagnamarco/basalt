using System.ComponentModel;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>AutoCompleteStringCollection as a full list: add, insert, remove, index, and the CollectionChanged it raises.</summary>
public class AutoCompleteStringCollectionTests
{
    [Fact]
    public void Add_insert_remove_and_indexer_behave_like_an_ordinary_list()
    {
        var collection = new AutoCompleteStringCollection();

        collection.Add("one");
        collection.AddRange(["two", "three"]);
        collection.Insert(1, "inserted");

        Assert.Equal(4, collection.Count);
        Assert.Equal("inserted", collection[1]);
        Assert.True(collection.Contains("two"));
        Assert.Equal(3, collection.IndexOf("three"));

        collection.RemoveAt(0);
        Assert.Equal(["inserted", "two", "three"], collection.Cast<string>());

        collection.Remove("two");
        Assert.DoesNotContain("two", collection.Cast<string>());

        collection[0] = "replaced";
        Assert.Equal("replaced", collection[0]);

        collection.Clear();
        Assert.Empty(collection);
    }

    [Fact]
    public void CollectionChanged_fires_for_every_edit_so_an_open_suggestion_list_can_refresh()
    {
        var collection = new AutoCompleteStringCollection();
        var actions = new List<CollectionChangeAction>();
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);

        collection.Add("a");
        collection.Insert(0, "b");
        collection[0] = "c";
        collection.Remove("a");
        collection.Clear();

        // The indexer's replace is a Remove of the old value followed by an Add of the new one.
        Assert.Equal(
            [
                CollectionChangeAction.Add,
                CollectionChangeAction.Add,
                CollectionChangeAction.Remove,
                CollectionChangeAction.Add,
                CollectionChangeAction.Remove,
                CollectionChangeAction.Refresh,
            ],
            actions);
    }

    [Fact]
    public void Implements_the_untyped_collection_interfaces_WinForms_declares()
    {
        var collection = new AutoCompleteStringCollection();
        collection.Add("x");

        var list = (System.Collections.IList)collection;
        Assert.True(list.Contains("x"));
        Assert.Equal(0, list.IndexOf("x"));

        var enumerable = (System.Collections.IEnumerable)collection;
        Assert.NotNull(enumerable.GetEnumerator());
    }
}
