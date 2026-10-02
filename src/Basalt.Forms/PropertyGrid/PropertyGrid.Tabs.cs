using System.Collections;
using System.Windows.Forms.Design;

namespace System.Windows.Forms;

/// <summary>
/// PropertyTabs: the set of PropertyTab instances a PropertyGrid knows
/// about, beyond the always-present default PropertiesTab at index 0.
/// </summary>
/// <remarks>
/// AddTabType/RemoveTabType and the collection itself are real: adding a tab
/// type constructs and keeps a real instance, removing one disposes it. What
/// stays out, with the rest of the family's gaps: switching the grid's
/// *displayed* tab to one of these (SelectedTab is always the default tab -
/// see PropertyGrid.cs's remarks), and the three PropertyTabScope overloads
/// (AddTabType(Type, PropertyTabScope), Clear(PropertyTabScope),
/// RefreshTabs(PropertyTabScope)): System.ComponentModel.PropertyTabScope has
/// no entry at all in this repository's Microsoft reference dump, so adding
/// it ourselves would be public surface Microsoft's reference assembly does
/// not have - the coordinator owns reconciling that gap.
/// </remarks>
public partial class PropertyGrid
{
    private PropertyTabCollection? _propertyTabCollection;

    /// <summary>The non-default tabs AddTabType/RemoveTabType maintain; PropertyTabCollection always reports the default PropertiesTab as well, at index 0.</summary>
    private readonly List<PropertyTab> _extraTabs = [];

    public PropertyTabCollection PropertyTabs => _propertyTabCollection ??= new PropertyTabCollection(this);

    /// <summary>Constructs one instance of tabType (which must derive from PropertyTab and have a public parameterless constructor, as Microsoft's does).</summary>
    protected virtual PropertyTab CreatePropertyTab(Type tabType)
    {
        ArgumentNullException.ThrowIfNull(tabType);

        if (!typeof(PropertyTab).IsAssignableFrom(tabType) || tabType.IsAbstract)
        {
            throw new ArgumentException($"'{tabType}' does not derive from a concrete System.Windows.Forms.Design.PropertyTab.", nameof(tabType));
        }

        return (PropertyTab)Activator.CreateInstance(tabType)!;
    }

    /// <summary>A real, mutable collection: PropertyGrid.PropertyTabs()[0] is always the default PropertiesTab; AddTabType/RemoveTabType act on the rest.</summary>
    public class PropertyTabCollection : ICollection, IEnumerable
    {
        private readonly PropertyGrid _owner;

        internal PropertyTabCollection(PropertyGrid owner) => _owner = owner;

        public int Count => _owner._extraTabs.Count + 1;

        public PropertyTab this[int index] => index == 0 ? _owner._defaultTab : _owner._extraTabs[index - 1];

        public void AddTabType(Type tabType)
        {
            var tab = _owner.CreatePropertyTab(tabType);
            _owner._extraTabs.Add(tab);
        }

        public void RemoveTabType(Type tabType)
        {
            ArgumentNullException.ThrowIfNull(tabType);

            var index = _owner._extraTabs.FindIndex(tab => tab.GetType() == tabType);
            if (index < 0) return;

            var removed = _owner._extraTabs[index];
            _owner._extraTabs.RemoveAt(index);
            removed.Dispose();
        }

        public IEnumerator GetEnumerator()
        {
            yield return _owner._defaultTab;
            foreach (var tab in _owner._extraTabs) yield return tab;
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        void ICollection.CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);

            var i = index;
            foreach (var tab in this) array.SetValue(tab, i++);
        }
    }
}
