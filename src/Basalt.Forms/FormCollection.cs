using System.Collections;

namespace System.Windows.Forms;

/// <summary>The forms the application has open, in the order they were opened.</summary>
public class FormCollection : ReadOnlyCollectionBase
{
    public virtual Form? this[int index] => index >= 0 && index < InnerList.Count ? (Form?)InnerList[index] : null;

    /// <summary>The first form with the name, compared without regard to case.</summary>
    public virtual Form? this[string? name]
    {
        get
        {
            if (name is null) return null;

            foreach (Form form in InnerList)
            {
                if (string.Equals(form.Name, name, StringComparison.OrdinalIgnoreCase)) return form;
            }

            return null;
        }
    }

    internal void Add(Form form)
    {
        if (!InnerList.Contains(form)) InnerList.Add(form);
    }

    internal void Remove(Form form) => InnerList.Remove(form);

    internal Form[] Snapshot() => [.. InnerList.Cast<Form>()];
}
