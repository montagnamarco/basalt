using System.ComponentModel;

namespace System.Windows.Forms.PropertyGridInternal;

/// <summary>What PropertyGrid's own root GridEntry implements, beyond GridEntry itself: the BrowsableAttributes a PropertyTab's GetProperties call was filtered with, and whether categories are shown - both read back by the grid's toolbar buttons and tab logic.</summary>
public interface IRootGridEntry
{
    AttributeCollection BrowsableAttributes { get; set; }

    void ResetBrowsableAttributes();

    void ShowCategories(bool fShowCat);
}
