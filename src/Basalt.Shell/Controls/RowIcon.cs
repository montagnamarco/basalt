using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Basalt.Shell.Controls;

/// <summary>
/// Lets the icon of a tree or list row follow the row's selection, in code.
///
/// A style cannot say "this icon's own row is selected" in this Avalonia version: a descendant selector also matches
/// the rows nested under a selected row, and the child combinator does not reach a row's header. So the icon finds
/// its own row (the nearest TreeViewItem or ListBoxItem) and the tree or list that owns it, and sets the
/// rowSelected class while the row is selected and its owner has the focus. Nesting depth cannot confuse it,
/// because it only ever looks at its own row.
/// </summary>
public static class RowIcon
{
    /// <summary>
    /// The class set on the icon while its row is selected and its owner has the focus. A class, not a pseudo-class:
    /// Avalonia lets only the control itself set or remove a pseudo-class.
    /// </summary>
    public const string SelectedClass = "rowSelected";

    public static readonly AttachedProperty<bool> FollowsSelectionProperty =
        AvaloniaProperty.RegisterAttached<IconView, bool>("FollowsSelection", typeof(RowIcon));

    private static readonly ConditionalWeakTable<IconView, Link> Links = new();

    static RowIcon()
    {
        FollowsSelectionProperty.Changed.AddClassHandler<IconView>((icon, e) =>
        {
            if (e.NewValue is true) Follow(icon);
            else Release(icon);
        });
    }

    public static bool GetFollowsSelection(IconView icon) => icon.GetValue(FollowsSelectionProperty);

    public static void SetFollowsSelection(IconView icon, bool value) => icon.SetValue(FollowsSelectionProperty, value);

    private static void Follow(IconView icon)
    {
        if (Links.TryGetValue(icon, out _)) return;

        Links.Add(icon, new Link(icon));
    }

    private static void Release(IconView icon)
    {
        if (!Links.TryGetValue(icon, out var link)) return;

        Links.Remove(icon);
        link.Dispose();
    }

    /// <summary>
    /// The subscriptions of one icon. The icon's own events and the two properties it watches (the row's IsSelected,
    /// the owner's IsKeyboardFocusWithin) are subscribed to the property itself, so a change to any other property
    /// of a large tree costs nothing here. The row's subscriptions are re-found whenever the icon is attached,
    /// detached or given a new data context, which is what a recycled container does.
    /// </summary>
    private sealed class Link : IDisposable
    {
        private readonly IconView _icon;
        private readonly EventHandler<VisualTreeAttachmentEventArgs> _attached;
        private readonly EventHandler<VisualTreeAttachmentEventArgs> _detached;
        private readonly EventHandler _dataContext;
        private readonly List<IDisposable> _watches = [];
        private Control? _row;
        private Control? _owner;
        private bool _selected;
        private bool _focused;
        private bool _active = true;

        public Link(IconView icon)
        {
            _icon = icon;

            _attached = (_, _) => Refresh();
            _detached = (_, _) => Detach();
            _dataContext = (_, _) => Refresh();

            icon.AttachedToVisualTree += _attached;
            icon.DetachedFromVisualTree += _detached;
            icon.DataContextChanged += _dataContext;

            Refresh();
        }

        private void Refresh()
        {
            if (!_active) return;

            Unwatch();

            _row = _icon.GetVisualAncestors().FirstOrDefault(v => v is TreeViewItem or ListBoxItem) as Control;

            if (_row is TreeViewItem) _owner = _row.FindAncestorOfType<TreeView>();
            else if (_row is ListBoxItem) _owner = _row.FindAncestorOfType<ListBox>();
            else _owner = null;

            if (_row is TreeViewItem treeItem)
                _watches.Add(treeItem.GetObservable(TreeViewItem.IsSelectedProperty).Subscribe(new Watch<bool>(v => { _selected = v; Update(); })));
            else if (_row is ListBoxItem listItem)
                _watches.Add(listItem.GetObservable(ListBoxItem.IsSelectedProperty).Subscribe(new Watch<bool>(v => { _selected = v; Update(); })));

            if (_owner is not null)
                _watches.Add(_owner.GetObservable(InputElement.IsKeyboardFocusWithinProperty).Subscribe(new Watch<bool>(v => { _focused = v; Update(); })));

            _selected = _row is TreeViewItem tree ? tree.IsSelected : _row is ListBoxItem list && list.IsSelected;
            _focused = _owner?.IsKeyboardFocusWithin == true;
            Update();
        }

        private void Update() => _icon.Classes.Set(SelectedClass, _active && _selected && _focused);

        /// <summary>Drops the watches on the row and the owner; the icon's class is cleared with them.</summary>
        private void Unwatch()
        {
            foreach (var watch in _watches) watch.Dispose();
            _watches.Clear();

            _row = null;
            _owner = null;
            _selected = false;
            _focused = false;
        }

        private void Detach()
        {
            Unwatch();
            Update();
        }

        public void Dispose()
        {
            _active = false;

            _icon.AttachedToVisualTree -= _attached;
            _icon.DetachedFromVisualTree -= _detached;
            _icon.DataContextChanged -= _dataContext;

            Unwatch();
            _icon.Classes.Set(SelectedClass, false);
        }
    }

    /// <summary>A plain observer, so the watch needs no reactive library: it forwards each value to a delegate.</summary>
    private sealed class Watch<T>(Action<T> onValue) : IObserver<T>
    {
        public void OnNext(T value) => onValue(value);

        public void OnError(Exception error) { }

        public void OnCompleted() { }
    }
}
