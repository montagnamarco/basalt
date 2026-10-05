using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>Exposes the drawn controls through Avalonia's platform accessibility bridge.</summary>
internal sealed class FormsAutomationPeer : ControlAutomationPeer, IInvokeProvider, IValueProvider, IToggleProvider
{
    private readonly FormsCanvas _canvas;
    private readonly AccessibleObject _accessible;
    private readonly ConditionalWeakTable<AccessibleObject, FormsAutomationPeer> _peers;
    private readonly List<FormsAutomationPeer> _children = [];
    private readonly Avalonia.Controls.Control? _visualOwner;
    private readonly ControlEventHandler _treeChanged;
    private readonly EventHandler _visibilityChanged;
    private readonly EventHandler _textChanged;
    private readonly EventHandler _childVisibilityChanged;
    private readonly HashSet<Control> _observedChildren = [];
    private bool _disposed;

    internal FormsAutomationPeer(FormsCanvas canvas)
        : this(canvas, canvas.Root.AccessibilityObject, new(), canvas)
    {
    }

    private FormsAutomationPeer(FormsCanvas canvas, AccessibleObject accessible,
        ConditionalWeakTable<AccessibleObject, FormsAutomationPeer> peers, Avalonia.Controls.Control owner) : base(owner)
    {
        _canvas = canvas;
        _accessible = accessible;
        _peers = peers;
        _visualOwner = owner == canvas ? null : owner;
        _treeChanged = (_, _) =>
        {
            SynchronizeChildren();
            InvalidateChildren();
        };
        _visibilityChanged = (_, _) =>
        {
            SynchronizeChildren();
            InvalidateChildren();
            if (GetParent() is FormsAutomationPeer parent)
            {
                parent.SynchronizeChildren();
                parent.InvalidateChildren();
            }
        };
        _textChanged = (_, _) => RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, null, GetName());
        _childVisibilityChanged = (_, _) =>
        {
            SynchronizeChildren();
            InvalidateChildren();
        };
        if (accessible.OwnerControl is { } control)
        {
            control.ControlAdded += _treeChanged;
            control.ControlRemoved += _treeChanged;
            control.VisibleChanged += _visibilityChanged;
            control.TextChanged += _textChanged;
        }

        if (_visualOwner is null) canvas.DetachedFromVisualTree += (_, _) => Detach();
    }

    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        SynchronizeChildren();
        return _children.Cast<AutomationPeer>().ToArray();
    }

    private void SynchronizeChildren()
    {
        if (_disposed) return;
        if (_accessible.OwnerControl is { } control)
        {
            var current = control.Controls.Cast<Control>().ToHashSet();
            foreach (var removed in _observedChildren.Except(current).ToArray())
            {
                removed.VisibleChanged -= _childVisibilityChanged;
                _observedChildren.Remove(removed);
            }

            foreach (var added in current.Except(_observedChildren))
            {
                added.VisibleChanged += _childVisibilityChanged;
                _observedChildren.Add(added);
            }
        }

        var children = new List<FormsAutomationPeer>();
        for (var index = 0; index < _accessible.GetChildCount(); index++)
        {
            var child = _accessible.GetChild(index);
            children.Add(_peers.GetValue(child, accessible =>
            {
                var owner = new AutomationOwner { IsHitTestVisible = false };
                var peer = new FormsAutomationPeer(_canvas, accessible, _peers, owner);
                owner.Peer = peer;
                _canvas.AttachAutomationOwner(owner);
                return peer;
            }));
        }

        foreach (var removed in _children.Except(children).ToArray())
        {
            removed.Detach();
            _peers.Remove(removed._accessible);
        }

        _children.Clear();
        _children.AddRange(children);
    }

    private void Detach()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var child in _children) child.Detach();
        _children.Clear();
        _peers.Remove(_accessible);
        foreach (var child in _observedChildren) child.VisibleChanged -= _childVisibilityChanged;
        _observedChildren.Clear();
        if (_accessible.OwnerControl is { } control)
        {
            control.ControlAdded -= _treeChanged;
            control.ControlRemoved -= _treeChanged;
            control.VisibleChanged -= _visibilityChanged;
            control.TextChanged -= _textChanged;
        }

        if (_visualOwner is not null) _canvas.DetachAutomationOwner(_visualOwner);
    }

    protected override string? GetNameCore() => _accessible.Name;
    protected override string? GetHelpTextCore() => _accessible.Help ?? _accessible.Description;
    protected override string? GetAccessKeyCore() => _accessible.KeyboardShortcut;
    protected override string? GetAutomationIdCore() => _accessible.OwnerControl?.Name;
    protected override string GetClassNameCore() => _accessible.OwnerControl?.GetType().Name ?? _accessible.GetType().Name;
    protected override bool IsEnabledCore() => !_accessible.State.HasFlag(AccessibleStates.Unavailable);
    protected override bool IsKeyboardFocusableCore() => _accessible.State.HasFlag(AccessibleStates.Focusable);
    protected override bool HasKeyboardFocusCore() => _accessible.State.HasFlag(AccessibleStates.Focused);
    protected override bool IsOffscreenCore() => _accessible.State.HasFlag(AccessibleStates.Invisible)
        || _accessible.State.HasFlag(AccessibleStates.Offscreen);

    protected override Rect GetBoundingRectangleCore()
    {
        var bounds = _accessible.Bounds;
        var rootLocation = _canvas.Root.PointToScreen(Drawing.Point.Empty);
        return new Rect(bounds.X - rootLocation.X, bounds.Y - rootLocation.Y, bounds.Width, bounds.Height);
    }

    protected override void SetFocusCore() => _accessible.OwnerControl?.Focus();

    protected override AutomationControlType GetAutomationControlTypeCore() => _accessible.Role switch
    {
        AccessibleRole.PushButton => AutomationControlType.Button,
        AccessibleRole.CheckButton => AutomationControlType.CheckBox,
        AccessibleRole.RadioButton => AutomationControlType.RadioButton,
        AccessibleRole.Text => AutomationControlType.Edit,
        AccessibleRole.StaticText => AutomationControlType.Text,
        AccessibleRole.ComboBox => AutomationControlType.ComboBox,
        AccessibleRole.List => AutomationControlType.List,
        AccessibleRole.ListItem => AutomationControlType.ListItem,
        AccessibleRole.Outline => AutomationControlType.Tree,
        AccessibleRole.OutlineItem => AutomationControlType.TreeItem,
        AccessibleRole.Table => AutomationControlType.DataGrid,
        AccessibleRole.PageTab => AutomationControlType.TabItem,
        AccessibleRole.PageTabList => AutomationControlType.Tab,
        AccessibleRole.ProgressBar => AutomationControlType.ProgressBar,
        AccessibleRole.Slider => AutomationControlType.Slider,
        _ => _accessible.OwnerControl switch
        {
            Button => AutomationControlType.Button,
            CheckBox => AutomationControlType.CheckBox,
            RadioButton => AutomationControlType.RadioButton,
            TextBoxBase => AutomationControlType.Edit,
            Label => AutomationControlType.Text,
            Form => AutomationControlType.Window,
            _ => AutomationControlType.Pane,
        },
    };

    public void Invoke()
    {
        if (!IsEnabled()) throw new InvalidOperationException("The control is disabled.");
        _accessible.DoDefaultAction();
    }

    bool IValueProvider.IsReadOnly => _accessible.OwnerControl is not TextBoxBase text || text.ReadOnly;

    string? IValueProvider.Value => _accessible.OwnerControl?.Text;

    void IValueProvider.SetValue(string? value)
    {
        if (!IsEnabled()) throw new InvalidOperationException("The control is disabled.");
        if (_accessible.OwnerControl is not TextBoxBase text || text.ReadOnly)
            throw new InvalidOperationException("The control is read-only.");
        text.Text = value ?? string.Empty;
    }

    Avalonia.Automation.Provider.ToggleState IToggleProvider.ToggleState => _accessible.OwnerControl is CheckBox checkBox
        ? checkBox.CheckState switch
        {
            CheckState.Checked => Avalonia.Automation.Provider.ToggleState.On,
            CheckState.Indeterminate => Avalonia.Automation.Provider.ToggleState.Indeterminate,
            _ => Avalonia.Automation.Provider.ToggleState.Off,
        }
        : Avalonia.Automation.Provider.ToggleState.Off;

    void IToggleProvider.Toggle() => Invoke();

    protected override object? GetProviderCore(Type providerType)
    {
        if (providerType == typeof(IInvokeProvider))
        {
            return _accessible.OwnerControl is ButtonBase ? this : null;
        }

        if (providerType == typeof(IToggleProvider)) return _accessible.OwnerControl is CheckBox ? this : null;
        if (providerType == typeof(IValueProvider))
        {
            if (_accessible.OwnerControl is TextBox { UseSystemPasswordChar: true } or TextBox { PasswordChar: not '\0' })
                return null;
            return _accessible.OwnerControl is TextBoxBase ? this : null;
        }

        return base.GetProviderCore(providerType);
    }

    private sealed class AutomationOwner : Avalonia.Controls.Control
    {
        internal FormsAutomationPeer? Peer { get; set; }

        protected override AutomationPeer OnCreateAutomationPeer() => Peer!;
    }
}
