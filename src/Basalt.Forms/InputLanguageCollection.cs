using System.Collections;

namespace System.Windows.Forms;

/// <summary>The installed keyboard layouts: on this platform, always the current one alone.</summary>
public class InputLanguageCollection : ReadOnlyCollectionBase
{
    internal InputLanguageCollection(InputLanguage[] value) => InnerList.AddRange(value);

    public InputLanguage this[int index] => (InputLanguage)InnerList[index]!;

    public bool Contains(InputLanguage value) => InnerList.Contains(value);

    public void CopyTo(InputLanguage[] array, int index) => InnerList.CopyTo(array, index);

    public int IndexOf(InputLanguage value) => InnerList.IndexOf(value);
}
