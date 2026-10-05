using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>Limits the designer's state-image choices to indices a ListViewItem accepts.</summary>
public class ListViewItemStateImageIndexConverter : ImageIndexConverter
{
    protected override bool IncludeNoneAsStandardValue => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        var count = Math.Min(ImageListFromContext(context)?.Images.Count ?? 0, 15);
        var values = Enumerable.Range(0, count).Cast<object>().ToList();
        values.Add(-1);
        return new StandardValuesCollection(values);
    }
}
