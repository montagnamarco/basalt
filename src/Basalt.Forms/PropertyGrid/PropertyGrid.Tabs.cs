using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms.Design;

namespace System.Windows.Forms;

/// <summary>
/// PropertyTabs: the set of PropertyTab instances a PropertyGrid knows
/// about, beyond the always-present default PropertiesTab at index 0.
/// </summary>
/// <remarks>
/// AddTabType/RemoveTabType and the collection itself are real: adding a tab
/// type constructs and keeps a real instance, removing one disposes it. Each
/// tab remembers the PropertyTabScope it was added with (Global when none is
/// given, matching Microsoft's default). Clear(scope) and RefreshTabs(scope)
/// only accept PropertyTabScope.Document or PropertyTabScope.Component -
/// Static and Global tabs are never something a caller clears, so passing
/// either throws ArgumentException, the same validation Microsoft's grid
/// has - and remove every tab whose own scope is scope or more specific
/// (Component is the most specific, Document less so): Clear(Component)
/// drops only Component tabs, Clear(Document) drops Document and Component
/// tabs, leaving every Static or Global tab untouched either way.
/// PropertyTabScope itself lives in System.ComponentModel.TypeConverter, not
/// in this assembly, so using it here adds no public surface of our own.
/// What stays out, with the rest of the family's gaps: switching the grid's
/// *displayed* tab to one of these (SelectedTab is always the default tab -
/// see PropertyGrid.cs's remarks) and automatically discovering tabs from a
/// selected component's PropertyTabAttribute (nothing populates Document- or
/// Component-scoped tabs on its own; RefreshTabs only ever clears them).
/// </remarks>
public partial class PropertyGrid
{
    private PropertyTabCollection? _propertyTabCollection;

    /// <summary>The non-default tabs AddTabType/RemoveTabType maintain, each with the scope it was added at; PropertyTabCollection always reports the default PropertiesTab as well, at index 0.</summary>
    private readonly List<(PropertyTab Tab, PropertyTabScope Scope)> _extraTabs = [];

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

    /// <summary>What Clear(scope) does, since this family has no automatic per-component tab discovery to rebuild from: scope must be Document or Component, and only tabs of that scope or more specific are dropped.</summary>
    public void RefreshTabs(PropertyTabScope scope) => PropertyTabs.Clear(scope);

    /// <summary>A real, mutable collection: PropertyGrid.PropertyTabs()[0] is always the default PropertiesTab; AddTabType/RemoveTabType act on the rest.</summary>
    public class PropertyTabCollection : ICollection, IEnumerable
    {
        private readonly PropertyGrid _owner;

        internal PropertyTabCollection(PropertyGrid owner) => _owner = owner;

        public int Count => _owner._extraTabs.Count + 1;

        public PropertyTab this[int index] => index == 0 ? _owner._defaultTab : _owner._extraTabs[index - 1].Tab;

        /// <summary>Adds tabType at PropertyTabScope.Global, the scope Microsoft's own parameterless overload uses.</summary>
        public void AddTabType(Type tabType) => AddTabType(tabType, PropertyTabScope.Global);

        public void AddTabType(Type tabType, PropertyTabScope tabScope)
        {
            var tab = _owner.CreatePropertyTab(tabType);
            _owner._extraTabs.Add((tab, tabScope));
        }

        public void RemoveTabType(Type tabType)
        {
            ArgumentNullException.ThrowIfNull(tabType);

            var index = _owner._extraTabs.FindIndex(entry => entry.Tab.GetType() == tabType);
            if (index < 0) return;

            var removed = _owner._extraTabs[index].Tab;
            _owner._extraTabs.RemoveAt(index);
            removed.Dispose();
        }

        /// <summary>Removes every tab at scope or a more specific scope: Clear(PropertyTabScope.Component) drops only Component tabs, Clear(PropertyTabScope.Document) drops Document and Component tabs. Only Document and Component are valid here - Static and Global tabs are never something a caller clears, so scope being either throws ArgumentException, same as Microsoft's grid.</summary>
        public void Clear(PropertyTabScope scope)
        {
            if (scope is not (PropertyTabScope.Document or PropertyTabScope.Component))
            {
                throw new ArgumentException("Only Document and Component tabs can be removed.", nameof(scope));
            }

            for (var i = _owner._extraTabs.Count - 1; i >= 0; i--)
            {
                var (tab, tabScope) = _owner._extraTabs[i];
                if (tabScope < scope) continue;

                _owner._extraTabs.RemoveAt(i);
                tab.Dispose();
            }
        }

        /// <summary>Test-only: the scope the most recently added instance of tabType was given, so AddTabType's default can be checked without Clear being able to observe Static/Global any more.</summary>
        internal PropertyTabScope ScopeOf(Type tabType) =>
            _owner._extraTabs.Last(entry => entry.Tab.GetType() == tabType).Scope;

        public IEnumerator GetEnumerator()
        {
            yield return _owner._defaultTab;
            foreach (var (tab, _) in _owner._extraTabs) yield return tab;
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
