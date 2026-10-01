using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// What ListBox and ComboBox share: a bindable DataSource, the DisplayMember
/// and ValueMember pair that pick which property of each row is shown and
/// which is returned by SelectedValue, and the CurrencyManager that keeps a
/// bound list's current row and the control's own selection in step with
/// each other.
/// </summary>
public abstract class ListControl : Control
{
    private object? _dataSource;
    private string _displayMember = string.Empty;
    private string _valueMember = string.Empty;
    private bool _formattingEnabled;
    private string _formatString = string.Empty;
    private IFormatProvider? _formatInfo;
    private CurrencyManager? _dataManager;
    private object? _lastSelectedValue;

    protected ListControl()
    {
    }

    /// <summary>Whether the control currently accepts a selection; a ListBox with SelectionMode.None overrides this to false.</summary>
    protected virtual bool AllowSelection => true;

    /// <summary>The CurrencyManager bound to DataSource through BindingContext, or null while unbound.</summary>
    protected CurrencyManager? DataManager => _dataManager;

    [DefaultValue(null)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public object? DataSource
    {
        get => _dataSource;
        set
        {
            if (value is not null && value is not IList && value is not IListSource)
            {
                throw new ArgumentException("DataSource must be an IList or an IListSource.", nameof(value));
            }

            if (ReferenceEquals(_dataSource, value)) return;

            _dataSource = value;

            // A new data source has an unrelated schema: the member names picked for the old one no longer apply.
            if (_displayMember.Length > 0)
            {
                _displayMember = string.Empty;
                OnDisplayMemberChanged(EventArgs.Empty);
            }

            if (_valueMember.Length > 0)
            {
                _valueMember = string.Empty;
                OnValueMemberChanged(EventArgs.Empty);
            }

            SetDataConnection();
            OnDataSourceChanged(EventArgs.Empty);
        }
    }

    [DefaultValue("")]
    public string DisplayMember
    {
        get => _displayMember;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _displayMember, StringComparison.Ordinal)) return;

            _displayMember = value;
            OnDisplayMemberChanged(EventArgs.Empty);

            // DisplayMember may also carry the navigation path (e.g. "Orders.OrderDate")
            // that decides which list the CurrencyManager walks, so it is reconnected here too.
            SetDataConnection();
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DefaultValue(null)]
    public IFormatProvider? FormatInfo
    {
        get => _formatInfo;
        set
        {
            if (Equals(value, _formatInfo)) return;

            _formatInfo = value;
            OnFormatInfoChanged(EventArgs.Empty);
        }
    }

    [DefaultValue("")]
    [MergableProperty(false)]
    public string FormatString
    {
        get => _formatString;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _formatString, StringComparison.Ordinal)) return;

            _formatString = value;
            OnFormatStringChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    public bool FormattingEnabled
    {
        get => _formattingEnabled;
        set
        {
            if (value == _formattingEnabled) return;

            _formattingEnabled = value;
            OnFormattingEnabledChanged(EventArgs.Empty);
        }
    }

    public abstract int SelectedIndex { get; set; }

    [DefaultValue(null)]
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Bindable(true)]
    public object? SelectedValue
    {
        get => ComputeSelectedValue();
        set
        {
            // As in WinForms: with a data source, a value that is not in the list selects nothing.
            if (_dataManager is null) return;

            SelectedIndex = FindItemIndexByValue(value);
        }
    }

    [DefaultValue("")]
    public string ValueMember
    {
        get => _valueMember;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _valueMember, StringComparison.Ordinal)) return;

            var member = new BindingMemberInfo(value);
            if (_dataManager is not null) CheckValueMember(member, _dataManager);

            _valueMember = value;
            OnValueMemberChanged(EventArgs.Empty);
            UpdateSelectedValueCache();
        }
    }

    public event EventHandler? DataSourceChanged
    {
        add => Events.AddHandler(s_dataSourceChangedEvent, value);
        remove => Events.RemoveHandler(s_dataSourceChangedEvent, value);
    }

    public event EventHandler? DisplayMemberChanged
    {
        add => Events.AddHandler(s_displayMemberChangedEvent, value);
        remove => Events.RemoveHandler(s_displayMemberChangedEvent, value);
    }

    public event ListControlConvertEventHandler? Format
    {
        add => Events.AddHandler(s_formatEvent, value);
        remove => Events.RemoveHandler(s_formatEvent, value);
    }

    public event EventHandler? FormatInfoChanged
    {
        add => Events.AddHandler(s_formatInfoChangedEvent, value);
        remove => Events.RemoveHandler(s_formatInfoChangedEvent, value);
    }

    public event EventHandler? FormatStringChanged
    {
        add => Events.AddHandler(s_formatStringChangedEvent, value);
        remove => Events.RemoveHandler(s_formatStringChangedEvent, value);
    }

    public event EventHandler? FormattingEnabledChanged
    {
        add => Events.AddHandler(s_formattingEnabledChangedEvent, value);
        remove => Events.RemoveHandler(s_formattingEnabledChangedEvent, value);
    }

    public event EventHandler? SelectedValueChanged
    {
        add => Events.AddHandler(s_selectedValueChangedEvent, value);
        remove => Events.RemoveHandler(s_selectedValueChangedEvent, value);
    }

    public event EventHandler? ValueMemberChanged
    {
        add => Events.AddHandler(s_valueMemberChangedEvent, value);
        remove => Events.RemoveHandler(s_valueMemberChangedEvent, value);
    }

    private static readonly object s_dataSourceChangedEvent = new();
    private static readonly object s_displayMemberChangedEvent = new();
    private static readonly object s_formatEvent = new();
    private static readonly object s_formatInfoChangedEvent = new();
    private static readonly object s_formatStringChangedEvent = new();
    private static readonly object s_formattingEnabledChangedEvent = new();
    private static readonly object s_selectedValueChangedEvent = new();
    private static readonly object s_valueMemberChangedEvent = new();

    /// <summary>The value of DisplayMember on item, or item itself when DisplayMember is empty.</summary>
    protected object FilterItemOnProperty(object? item) => FilterItemOnProperty(item, _displayMember);

    protected object FilterItemOnProperty(object? item, string? field)
    {
        if (item is null) return null!;
        if (string.IsNullOrEmpty(field)) return item;

        var property = TypeDescriptor.GetProperties(item).Find(field, true);

        return property is null ? item : property.GetValue(item) ?? null!;
    }

    /// <summary>
    /// The text an item displays: the DisplayMember field (or the item itself when
    /// DisplayMember is empty), converted with FormatInfo, and when FormattingEnabled
    /// is set run through the Format event first so a handler can substitute its own text.
    /// </summary>
    public string GetItemText(object? item)
    {
        if (item is null) return string.Empty;

        var value = _displayMember.Length == 0 ? item : FilterItemOnProperty(item, DisplayField);

        if (!_formattingEnabled) return Convert.ToString(value, _formatInfo) ?? string.Empty;

        var args = new ListControlConvertEventArgs(value, typeof(string), item);
        OnFormat(args);

        if (args.Value is string formatted) return formatted;

        if (_formatString.Length > 0 && args.Value is IFormattable formattable) return formattable.ToString(_formatString, _formatInfo);

        return Convert.ToString(args.Value, _formatInfo) ?? string.Empty;
    }

    /// <summary>Up/Down/PageUp/PageDown/Home/End drive the selection instead of leaving the control through dialog navigation.</summary>
    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.Alt) == Keys.Alt) return base.IsInputKey(keyData);

        return (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
            _ => base.IsInputKey(keyData),
        };
    }

    protected override void OnBindingContextChanged(EventArgs e)
    {
        SetDataConnection();
        base.OnBindingContextChanged(e);
    }

    protected virtual void OnDataSourceChanged(EventArgs e) => ((EventHandler?)Events[s_dataSourceChangedEvent])?.Invoke(this, e);

    protected virtual void OnDisplayMemberChanged(EventArgs e) => ((EventHandler?)Events[s_displayMemberChangedEvent])?.Invoke(this, e);

    protected virtual void OnFormat(ListControlConvertEventArgs e) => ((ListControlConvertEventHandler?)Events[s_formatEvent])?.Invoke(this, e);

    protected virtual void OnFormatInfoChanged(EventArgs e) => ((EventHandler?)Events[s_formatInfoChangedEvent])?.Invoke(this, e);

    protected virtual void OnFormatStringChanged(EventArgs e) => ((EventHandler?)Events[s_formatStringChangedEvent])?.Invoke(this, e);

    protected virtual void OnFormattingEnabledChanged(EventArgs e) => ((EventHandler?)Events[s_formattingEnabledChangedEvent])?.Invoke(this, e);

    /// <summary>
    /// Raised by a derived class's SelectedIndex setter after it updates its own selection
    /// state: pushes the new position into DataManager and refreshes the SelectedValue cache.
    /// </summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e)
    {
        if (_dataManager is not null && AllowSelection && !_followingPosition && _dataManager.Position != SelectedIndex)
        {
            _dataManager.Position = SelectedIndex;
        }

        UpdateSelectedValueCache();
    }

    protected virtual void OnSelectedValueChanged(EventArgs e) => ((EventHandler?)Events[s_selectedValueChangedEvent])?.Invoke(this, e);

    protected virtual void OnValueMemberChanged(EventArgs e) => ((EventHandler?)Events[s_valueMemberChangedEvent])?.Invoke(this, e);

    /// <summary>Re-reads one item: called when the bound list reports a single row changed.</summary>
    protected abstract void RefreshItem(int index);

    /// <summary>Re-reads every item; the base class has no item storage of its own, so there is nothing to do here.</summary>
    protected virtual void RefreshItems()
    {
    }

    /// <summary>Writes one item back into whatever storage a derived class keeps; a no-op until overridden.</summary>
    protected virtual void SetItemCore(int index, object value)
    {
    }

    /// <summary>Replaces every item, called whenever the bound list is (re)connected or fully reset.</summary>
    protected abstract void SetItemsCore(IList items);

    /// <summary>
    /// The field of a member that may carry a navigation path ("Orders.OrderDate"):
    /// the path picks the list, the field is read from each item of it.
    /// </summary>
    private string DisplayField => new BindingMemberInfo(_displayMember).BindingField;

    private string ValueField => new BindingMemberInfo(_valueMember).BindingField;

    /// <summary>
    /// A disposed control lets go of its data manager, which the binding context keeps
    /// alive for other controls: otherwise the manager would keep raising into it.
    /// </summary>
    internal override void ReleaseBindings()
    {
        DisconnectDataManager();
        base.ReleaseBindings();
    }

    private void DisconnectDataManager()
    {
        if (_dataManager is null) return;

        _dataManager.PositionChanged -= DataManagerPositionChanged;
        _dataManager.ItemChanged -= DataManagerItemChanged;
        _dataManager = null;
    }

    private static void CheckValueMember(BindingMemberInfo member, CurrencyManager manager)
    {
        if (member.BindingField.Length == 0) return;

        var properties = manager.GetItemProperties();
        if (properties.Find(member.BindingField, true) is null)
        {
            throw new ArgumentException($"Cannot bind to the new value member '{member.BindingField}': it is not a property of the items of '{manager.List.GetType()}'.", "value");
        }
    }

    private object? ComputeSelectedValue()
    {
        if (_dataManager is null) return null;

        var index = SelectedIndex;
        if (index < 0 || index >= _dataManager.Count) return null;

        var item = _dataManager.List[index];

        return _valueMember.Length == 0 ? item : FilterItemOnProperty(item, ValueField);
    }

    private int FindItemIndexByValue(object? value)
    {
        if (_dataManager is null) return -1;

        for (var index = 0; index < _dataManager.Count; index++)
        {
            var item = _dataManager.List[index];
            var candidate = _valueMember.Length == 0 ? item : FilterItemOnProperty(item, ValueField);

            if (Equals(candidate, value)) return index;
        }

        return -1;
    }

    private CurrencyManager? ResolveDataManager()
    {
        if (_dataSource is null || BindingContext is null) return null;

        var path = new BindingMemberInfo(_displayMember).BindingPath;

        return BindingContext[_dataSource, path] as CurrencyManager;
    }

    /// <summary>Re-resolves DataManager from DataSource/DisplayMember/BindingContext and repopulates the items from it.</summary>
    private void SetDataConnection()
    {
        var newManager = ResolveDataManager();
        if (ReferenceEquals(newManager, _dataManager)) return;

        // Validated before anything changes, so a refused member leaves the old connection whole.
        if (newManager is not null) CheckValueMember(new BindingMemberInfo(_valueMember), newManager);

        var wasBound = _dataManager is not null;
        DisconnectDataManager();
        _dataManager = newManager;

        if (_dataManager is null)
        {
            if (wasBound) SetItemsCore(Array.Empty<object>());

            UpdateSelectedValueCache();
            return;
        }

        _dataManager.PositionChanged += DataManagerPositionChanged;
        _dataManager.ItemChanged += DataManagerItemChanged;

        SetItemsCore(_dataManager.List);

        if (AllowSelection) SelectedIndex = _dataManager.Position;

        UpdateSelectedValueCache();
    }

    /// <summary>Set while the selection follows the manager, so the selection does not push back into it.</summary>
    private bool _followingPosition;

    private void DataManagerPositionChanged(object? sender, EventArgs e)
    {
        if (_dataManager is null || !AllowSelection || _followingPosition) return;

        _followingPosition = true;

        try
        {
            SelectedIndex = _dataManager.Position;
        }
        finally
        {
            _followingPosition = false;
        }
    }

    private void DataManagerItemChanged(object? sender, ItemChangedEventArgs e)
    {
        if (_dataManager is null) return;

        if (e.Index < 0)
        {
            SetItemsCore(_dataManager.List);
            return;
        }

        RefreshItem(e.Index);
    }

    private void UpdateSelectedValueCache()
    {
        var newValue = ComputeSelectedValue();
        if (Equals(newValue, _lastSelectedValue)) return;

        _lastSelectedValue = newValue;
        OnSelectedValueChanged(EventArgs.Empty);
    }
}
