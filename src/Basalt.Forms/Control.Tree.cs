using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>The tree: parent, children and their z-order.</summary>
public partial class Control
{
    private ControlCollection? _controls;

    [Browsable(false)]
    public Control? Parent
    {
        get => _parent;
        set
        {
            if (value == _parent) return;

            if (value is not null) value.Controls.Add(this);
            else _parent!.Controls.Remove(this);
        }
    }

    /// <summary>The children, in z-order: index 0 is drawn on top and docked last.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ControlCollection Controls => _controls ??= CreateControlsInstance();

    [Browsable(false)]
    public bool HasChildren => _controls is { Count: > 0 };

    protected virtual ControlCollection CreateControlsInstance() => new(this);

    /// <summary>The top-level control this one is in, or itself when it is one.</summary>
    [Browsable(false)]
    public Control? TopLevelControl
    {
        get
        {
            for (var control = this; control is not null; control = control._parent)
            {
                if (control.GetTopLevel()) return control;
            }

            return null;
        }
    }

    /// <summary>The form this control is on, or null; a form's own FindForm is its parent form, if it has one.</summary>
    public Form? FindForm()
    {
        for (var control = _parent; control is not null; control = control._parent)
        {
            if (control is Form form) return form;
        }

        return null;
    }

    /// <summary>Whether the control is this one's child, grandchild, or deeper.</summary>
    public bool Contains(Control? ctl)
    {
        for (var control = ctl?._parent; control is not null; control = control._parent)
        {
            if (control == this) return true;
        }

        return false;
    }

    /// <summary>Moves the control to the front of its parent's z-order.</summary>
    public void BringToFront()
    {
        if (_parent is not null) _parent.Controls.SetChildIndex(this, 0);
    }

    /// <summary>Moves the control to the back of its parent's z-order.</summary>
    public void SendToBack()
    {
        if (_parent is not null) _parent.Controls.SetChildIndex(this, -1);
    }

    /// <summary>Puts the windows back in the order of the collection; there are no windows to reorder, only a repaint.</summary>
    protected void UpdateZOrder() => _parent?.Invalidate(Bounds, true);

    /// <summary>The topmost visible child under the point, in client coordinates.</summary>
    public Control? GetChildAtPoint(Point pt) => GetChildAtPoint(pt, GetChildAtPointSkip.None);

    public Control? GetChildAtPoint(Point pt, GetChildAtPointSkip skipValue)
    {
        if (_controls is null) return null;

        var point = pt;
        foreach (var child in _controls.Snapshot())
        {
            if (!child.Bounds.Contains(point) || !child.RegionContains(point)) continue;

            if ((skipValue & GetChildAtPointSkip.Invisible) != 0 && !child.Visible) continue;
            if ((skipValue & GetChildAtPointSkip.Disabled) != 0 && !child.Enabled) continue;
            if ((skipValue & GetChildAtPointSkip.Transparent) != 0 && child.BackColor.A == 0) continue;

            return child;
        }

        return null;
    }

    /// <summary>
    /// Sets the parent and tells the control about the ambient values it now
    /// inherits from a different place.
    /// </summary>
    private void AssignParent(Control? value)
    {
        var oldFont = Font;
        var oldBackColor = BackColor;
        var oldForeColor = ForeColor;
        var oldRightToLeft = RightToLeft;
        var oldEnabled = Enabled;
        var oldVisible = Visible;
        var oldDataContext = DataContext;

        _parent = value;

        OnParentChanged(EventArgs.Empty);

        if (oldEnabled != Enabled) OnEnabledChanged(EventArgs.Empty);
        if (oldVisible != Visible && !GetTopLevel()) OnVisibleChanged(EventArgs.Empty);
        if (!oldFont.Equals(Font)) OnFontChanged(EventArgs.Empty);
        if (oldBackColor != BackColor) OnBackColorChanged(EventArgs.Empty);
        if (oldForeColor != ForeColor) OnForeColorChanged(EventArgs.Empty);
        if (oldRightToLeft != RightToLeft) OnRightToLeftChanged(EventArgs.Empty);
        if (!Equals(oldDataContext, DataContext)) OnDataContextChanged(EventArgs.Empty);
    }

    protected virtual void OnParentChanged(EventArgs e) => ((EventHandler?)Events[s_parentEvent])?.Invoke(this, e);

    protected virtual void OnControlAdded(ControlEventArgs e) => ((ControlEventHandler?)Events[s_controlAddedEvent])?.Invoke(this, e);

    protected virtual void OnControlRemoved(ControlEventArgs e) => ((ControlEventHandler?)Events[s_controlRemovedEvent])?.Invoke(this, e);

    [Category("Property Changed")]
    public event EventHandler? ParentChanged
    {
        add => Events.AddHandler(s_parentEvent, value);
        remove => Events.RemoveHandler(s_parentEvent, value);
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [Category("Behavior")]
    public event ControlEventHandler? ControlAdded
    {
        add => Events.AddHandler(s_controlAddedEvent, value);
        remove => Events.RemoveHandler(s_controlAddedEvent, value);
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [Category("Behavior")]
    public event ControlEventHandler? ControlRemoved
    {
        add => Events.AddHandler(s_controlRemovedEvent, value);
        remove => Events.RemoveHandler(s_controlRemovedEvent, value);
    }

    private static readonly object s_parentEvent = new();
    private static readonly object s_controlAddedEvent = new();
    private static readonly object s_controlRemovedEvent = new();

    IArrangedElement? IArrangedElement.Container => _parent;

    ArrangedElementCollection IArrangedElement.Children => Controls;

    /// <summary>The children of a control, in z-order (index 0 on top).</summary>
    [ListBindable(false)]
    public class ControlCollection : ArrangedElementCollection, ICloneable
    {
        private int _lastAccessedIndex = -1;

        public ControlCollection(Control owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            Owner = owner;
        }

        public Control Owner { get; }

        public virtual Control this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index {index} is out of range.");

                return (Control)Items[index];
            }
        }

        /// <summary>The first child with the name, compared without regard to case; null when none has it.</summary>
        public virtual Control? this[string? key]
        {
            get
            {
                var index = IndexOfKey(key);

                return index >= 0 ? this[index] : null;
            }
        }

        /// <summary>
        /// Adds the control at the back of the z-order. A control already
        /// here moves to the back; one in another collection leaves it first.
        /// </summary>
        public virtual void Add(Control? value)
        {
            if (value is null) return;

            if (value.GetTopLevel()) throw new ArgumentException("Top-level control cannot be added to a control.", nameof(value));

            if (value == Owner || value.Contains(Owner)) throw new ArgumentException("A circular control reference has been made. A control cannot be owned by or parented to itself.", nameof(value));

            if (value._parent == Owner)
            {
                value.SendToBack();
                return;
            }

            var oldCanvas = value.TopLevelControl?.Canvas;
            var nativeScopes = value._parent is null ? null : BeginNativeReparenting(value);
            try
            {
                value._parent?.Controls.Remove(value);
                // Detach from the old visual tree while the engine's reparenting
                // scope is active, before the destination canvas attaches it.
                if (nativeScopes is not null) oldCanvas?.RequestFrame();

                Items.Add(value);

                // A control added without a tab index of its own goes after the others.
                if (value._tabIndex == -1) value._tabIndex = NextTabIndex(value);

                Owner.SuspendLayout();
                try
                {
                    value.AssignParent(Owner);

                    // Added to a control that is on screen: it gets its handle now, as its window would be created.
                    if (Owner.Created && value.Visible) value.CreateControl();

                    value.InitLayout();
                }
                finally
                {
                    Owner.ResumeLayout(false);
                }

                Owner.DoLayout(value, PropertyNames.Parent);
                Owner.OnControlAdded(new ControlEventArgs(value));
                Owner.Invalidate(value.Bounds, true);
                if (nativeScopes is not null) Owner.TopLevelControl?.Canvas?.RequestFrame();
            }
            finally
            {
                if (nativeScopes is not null)
                    for (var i = nativeScopes.Count - 1; i >= 0; i--) nativeScopes[i].Dispose();
            }
        }

        private static List<IDisposable>? BeginNativeReparenting(Control control)
        {
            List<IDisposable>? scopes = null;
            Begin(control);
            return scopes;

            void Begin(Control descendant)
            {
                if (descendant is WebBrowser browser && browser.BeginNativeReparenting() is { } scope)
                    (scopes ??= []).Add(scope);

                foreach (Control child in descendant.Controls) Begin(child);
            }
        }

        private protected override int AddFromList(object? control)
        {
            if (control is not Control value) throw new ArgumentException("Value must be a Control.", nameof(control));

            Add(value);

            return IndexOf(value);
        }

        public virtual void AddRange(params Control[] controls)
        {
            ArgumentNullException.ThrowIfNull(controls);

            if (controls.Length == 0) return;

            Owner.SuspendLayout();

            try
            {
                foreach (var control in controls) Add(control);
            }
            finally
            {
                Owner.ResumeLayout(true);
            }
        }

        object ICloneable.Clone()
        {
            var clone = Owner.CreateControlsInstance();
            clone.Items.AddRange(Items);

            return clone;
        }

        public bool Contains(Control? control) => control is not null && Items.Contains(control);

        public virtual bool ContainsKey(string? key) => IndexOfKey(key) >= 0;

        /// <summary>The children with the name, compared without regard to case, and theirs when searching all children.</summary>
        public Control[] Find(string key, bool searchAllChildren)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key), "Key cannot be empty.");

            var found = new List<Control>();
            Find(key, searchAllChildren, found);

            return [.. found];
        }

        private void Find(string key, bool searchAllChildren, List<Control> found)
        {
            foreach (var control in Snapshot())
            {
                if (string.Equals(control.Name, key, StringComparison.OrdinalIgnoreCase)) found.Add(control);
            }

            if (!searchAllChildren) return;

            foreach (var control in Snapshot())
            {
                if (control.HasChildren) control.Controls.Find(key, true, found);
            }
        }

        public override Collections.IEnumerator GetEnumerator() => Snapshot().GetEnumerator();

        public int IndexOf(Control? control) => control is null ? -1 : Items.IndexOf(control);

        /// <summary>The index of the first child with the name, compared without regard to case.</summary>
        public virtual int IndexOfKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            // The last match is tried first: lookups by key tend to repeat.
            if (_lastAccessedIndex >= 0 && _lastAccessedIndex < Count && string.Equals(this[_lastAccessedIndex].Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return _lastAccessedIndex;
            }

            for (var i = 0; i < Count; i++)
            {
                if (string.Equals(this[i].Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lastAccessedIndex = i;
                    return i;
                }
            }

            _lastAccessedIndex = -1;

            return -1;
        }

        public virtual void Remove(Control? value)
        {
            if (value is null || value._parent != Owner) return;

            var bounds = value.Bounds;

            // A control leaving the window takes the focus with it; the window must not point at it any more.
            if (Owner.TopLevelControl is { } window) window.ForgetFocusIn(value);
            if (Owner.GetContainerControl() is ContainerControl container) container.ForgetActive(value);
            if (Owner is ContainerControl ownerContainer) ownerContainer.ForgetActive(value);

            Items.Remove(value);
            value.AssignParent(null);

            Owner.DoLayout(value, PropertyNames.Parent);
            Owner.OnControlRemoved(new ControlEventArgs(value));
            Owner.Invalidate(bounds, true);
        }

        private protected override void RemoveFromList(object? control)
        {
            if (control is Control value) Remove(value);
        }

        private protected override void ClearFromList() => Clear();

        public void RemoveAt(int index) => Remove(this[index]);

        public virtual void RemoveByKey(string? key)
        {
            var index = IndexOfKey(key);
            if (index >= 0) RemoveAt(index);
        }

        /// <summary>Removes every child, from the back of the z-order to the front.</summary>
        public virtual void Clear()
        {
            Owner.SuspendLayout();

            try
            {
                while (Count > 0) RemoveAt(Count - 1);
            }
            finally
            {
                Owner.ResumeLayout();
            }
        }

        public int GetChildIndex(Control child) => GetChildIndex(child, true);

        public virtual int GetChildIndex(Control child, bool throwException)
        {
            var index = IndexOf(child);
            if (index == -1 && throwException) throw new ArgumentException("Child is not a member of this control's collection.", nameof(child));

            return index;
        }

        /// <summary>Moves the child in the z-order; an index past the end, or -1, puts it at the back.</summary>
        public virtual void SetChildIndex(Control child, int newIndex)
        {
            ArgumentNullException.ThrowIfNull(child);

            var index = GetChildIndex(child);
            if (index == newIndex) return;

            if (newIndex >= Count || newIndex == -1) newIndex = Count - 1;

            Items.RemoveAt(index);
            Items.Insert(newIndex, child);

            // Docking follows the z-order, so a new order is a new layout.
            Owner.DoLayout(child, PropertyNames.ChildIndex);
            Owner.Invalidate(child.Bounds, true);
        }

        /// <summary>The children at this moment, safe to enumerate while handlers change the collection.</summary>
        internal Control[] Snapshot()
        {
            var children = new Control[Items.Count];
            for (var i = 0; i < children.Length; i++) children[i] = (Control)Items[i];

            return children;
        }

        internal void ClearWithoutNotification() => Items.Clear();

        private int NextTabIndex(Control added)
        {
            var next = 0;
            foreach (var control in Snapshot())
            {
                if (control != added && control._tabIndex >= next) next = control._tabIndex + 1;
            }

            return next;
        }
    }
}
