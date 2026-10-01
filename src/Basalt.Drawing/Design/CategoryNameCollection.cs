using System.Collections;

namespace System.Drawing.Design;

/// <summary>A read-only list of the category names a designer groups properties under.</summary>
public sealed class CategoryNameCollection : ReadOnlyCollectionBase
{
    public CategoryNameCollection(CategoryNameCollection value)
    {
        ArgumentNullException.ThrowIfNull(value);

        InnerList.AddRange(value);
    }

    public CategoryNameCollection(string[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        InnerList.AddRange(value);
    }

    public string this[int index] => (string)InnerList[index]!;

    public bool Contains(string value) => InnerList.Contains(value);

    public void CopyTo(string[] array, int index) => InnerList.CopyTo(array, index);

    public int IndexOf(string value) => InnerList.IndexOf(value);
}
