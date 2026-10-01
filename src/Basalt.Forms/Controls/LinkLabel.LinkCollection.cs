using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

public partial class LinkLabel
{
    /// <summary>
    /// The links a LinkLabel paints and reacts to, kept in Start order so
    /// painting and Tab navigation can walk them left to right. Empty until
    /// the application adds to it; a LinkLabel with nothing added instead
    /// shows the single implicit link <see cref="LinkArea"/> describes.
    /// </summary>
    public class LinkCollection : ICollection, IEnumerable, IList
    {
        private readonly LinkLabel _owner;
        private readonly List<Link> _links = [];
        private bool _linksAdded;

        public LinkCollection(LinkLabel owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
        }

        [Browsable(false)]
        public int Count => _links.Count;

        public bool IsReadOnly => false;

        /// <summary>Whether the application has ever added to this collection; once true, LinkArea is ignored.</summary>
        public bool LinksAdded => _linksAdded;

        public virtual Link this[int index]
        {
            get => _links[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                ThrowIfOverlapping(value, skip: _links[index]);

                _links[index].Owner = null;
                _links[index] = value;
                value.Owner = _owner;
                _owner.InvalidateLinkLayout();
            }
        }

        public virtual Link? this[string key] => Find(key);

        public Link Add(int start, int length)
        {
            var link = new Link(start, length);
            Add(link);

            return link;
        }

        public Link Add(int start, int length, object? linkData)
        {
            var link = new Link(start, length, linkData);
            Add(link);

            return link;
        }

        /// <summary>Adds a link, in Start order; two links that claim the same characters cannot coexist.</summary>
        public int Add(Link value)
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfOverlapping(value, skip: null);

            _linksAdded = true;
            var index = SortedInsertIndex(value);
            _links.Insert(index, value);
            value.Owner = _owner;
            _owner.InvalidateLinkLayout();

            return index;
        }

        public virtual void Clear()
        {
            foreach (var link in _links) link.Owner = null;

            _links.Clear();
            _owner.InvalidateLinkLayout();
        }

        public bool Contains(Link link) => link is not null && _links.Contains(link);

        public virtual bool ContainsKey(string? key) => Find(key) is not null;

        public IEnumerator GetEnumerator() => _links.GetEnumerator();

        public int IndexOf(Link link) => link is null ? -1 : _links.IndexOf(link);

        public virtual int IndexOfKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            for (var i = 0; i < _links.Count; i++)
            {
                if (string.Equals(_links[i].Name, key, StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }

        public void Remove(Link value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_links.Remove(value)) return;

            value.Owner = null;
            _owner.InvalidateLinkLayout();
        }

        public void RemoveAt(int index)
        {
            _links[index].Owner = null;
            _links.RemoveAt(index);
            _owner.InvalidateLinkLayout();
        }

        public virtual void RemoveByKey(string? key)
        {
            var index = IndexOfKey(key);
            if (index >= 0) RemoveAt(index);
        }

        /// <summary>The links in the order painting and keyboard navigation use; not a copy.</summary>
        internal List<Link> Ordered => _links;

        private Link? Find(string? key)
        {
            var index = IndexOfKey(key);

            return index >= 0 ? _links[index] : null;
        }

        private int SortedInsertIndex(Link value)
        {
            var index = 0;
            while (index < _links.Count && _links[index].Start <= value.Start) index++;

            return index;
        }

        /// <summary>Two links whose character ranges intersect would fight over the same pixels and the same click.</summary>
        private void ThrowIfOverlapping(Link value, Link? skip)
        {
            foreach (var existing in _links)
            {
                if (ReferenceEquals(existing, skip)) continue;

                if (Overlaps(existing, value))
                {
                    throw new InvalidOperationException("Overlapping link regions are not permitted on a LinkLabel's LinkCollection.");
                }
            }
        }

        private static bool Overlaps(Link a, Link b)
        {
            if (a.Length == 0 || b.Length == 0) return false;

            return a.Start < End(b) && b.Start < End(a);
        }

        /// <summary>A negative length runs to the end of the text, whatever text the label gets later, as painting resolves it.</summary>
        private static int End(Link link) => link.Length < 0 ? int.MaxValue : link.Start + link.Length;

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_links).CopyTo(array, index);

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        bool IList.IsFixedSize => false;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (Link)value!;
        }

        int IList.Add(object? value) => Add((Link)value!);

        void IList.Clear() => Clear();

        bool IList.Contains(object? value) => value is Link link && Contains(link);

        int IList.IndexOf(object? value) => value is Link link ? IndexOf(link) : -1;

        void IList.Insert(int index, object? value) => throw new NotSupportedException("A LinkCollection keeps itself sorted by Start; use Add instead of Insert.");

        void IList.Remove(object? value)
        {
            if (value is Link link) Remove(link);
        }

        void IList.RemoveAt(int index) => RemoveAt(index);
    }
}
