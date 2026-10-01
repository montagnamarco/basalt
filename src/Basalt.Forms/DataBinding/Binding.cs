using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace System.Windows.Forms;

/// <summary>
/// One control property tied to one field of one data source: it pushes the
/// field's value into the property on bind and on every position change,
/// and pulls the property's value back into the field by default when the
/// control validates.
/// </summary>
/// <remarks>
/// Microsoft decorates this class with [TypeConverter(typeof(ListBindingConverter))];
/// ListBindingConverter is a design-time type this family does not implement, so the
/// attribute, which ApiSurfaceTests never inspects, is left off.
/// </remarks>
public class Binding
{
    private readonly BindingMemberInfo _bindingMemberInfo;

    private IBindableComponent? _bindableComponent;
    private BindingManagerBase? _bindingManagerBase;
    private PropertyInfo? _controlProperty;
    private EventInfo? _controlChangedEvent;
    private Delegate? _controlChangedHandler;
    private bool _wiredToValidated;
    private INotifyPropertyChanged? _wiredCurrentItem;
    private bool _isBinding;

    public Binding(string propertyName, object? dataSource, string? dataMember)
        : this(propertyName, dataSource, dataMember, false, DataSourceUpdateMode.OnValidation)
    {
    }

    public Binding(string propertyName, object? dataSource, string? dataMember, bool formattingEnabled)
        : this(propertyName, dataSource, dataMember, formattingEnabled, DataSourceUpdateMode.OnValidation)
    {
    }

    public Binding(string propertyName, object? dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode dataSourceUpdateMode)
        : this(propertyName, dataSource, dataMember, formattingEnabled, dataSourceUpdateMode, null)
    {
    }

    public Binding(
        string propertyName, object? dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode dataSourceUpdateMode,
        object? nullValue)
        : this(propertyName, dataSource, dataMember, formattingEnabled, dataSourceUpdateMode, nullValue, string.Empty)
    {
    }

    public Binding(
        string propertyName, object? dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode dataSourceUpdateMode,
        object? nullValue, string formatString)
        : this(propertyName, dataSource, dataMember, formattingEnabled, dataSourceUpdateMode, nullValue, formatString, null)
    {
    }

    public Binding(
        string propertyName, object? dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode dataSourceUpdateMode,
        object? nullValue, string formatString, IFormatProvider? formatInfo)
    {
        PropertyName = propertyName;
        DataSource = dataSource;
        _bindingMemberInfo = new BindingMemberInfo(dataMember);
        FormattingEnabled = formattingEnabled;
        DataSourceUpdateMode = dataSourceUpdateMode;
        NullValue = nullValue;
        FormatString = formatString;
        FormatInfo = formatInfo;
    }

    [DefaultValue(null)]
    public IBindableComponent? BindableComponent => _bindableComponent;

    public BindingManagerBase? BindingManagerBase => _bindingManagerBase;

    public BindingMemberInfo BindingMemberInfo => _bindingMemberInfo;

    [DefaultValue(null)]
    public Control? Control => _bindableComponent as Control;

    [DefaultValue(ControlUpdateMode.OnPropertyChanged)]
    public ControlUpdateMode ControlUpdateMode { get; set; } = ControlUpdateMode.OnPropertyChanged;

    public object? DataSource { get; }

    public object? DataSourceNullValue { get; set; } = DBNull.Value;

    [DefaultValue(DataSourceUpdateMode.OnValidation)]
    public DataSourceUpdateMode DataSourceUpdateMode { get; set; }

    [DefaultValue(null)]
    public IFormatProvider? FormatInfo { get; set; }

    public string FormatString { get; set; }

    [DefaultValue(false)]
    public bool FormattingEnabled { get; set; }

    public bool IsBinding => _isBinding;

    public object? NullValue { get; set; }

    [DefaultValue("")]
    public string PropertyName { get; }

    public event BindingCompleteEventHandler? BindingComplete;

    public event ConvertEventHandler? Format;

    public event ConvertEventHandler? Parse;

    protected virtual void OnBindingComplete(BindingCompleteEventArgs e) => BindingComplete?.Invoke(this, e);

    protected virtual void OnFormat(ConvertEventArgs cevent) => Format?.Invoke(this, cevent);

    protected virtual void OnParse(ConvertEventArgs cevent) => Parse?.Invoke(this, cevent);

    /// <summary>Forces the control's property to the data source's current value, as if the source had just pushed it.</summary>
    public void ReadValue() => PushToControl();

    /// <summary>Forces the data source's field to the control's current value, as if the control had just validated.</summary>
    public void WriteValue() => PullFromControl();

    internal void SetBindableComponent(IBindableComponent component) => _bindableComponent = component;

    /// <summary>
    /// Resolves the manager for DataSource at this binding's path and attaches to it, or
    /// waits for the component to get a BindingContext when it does not have one yet.
    /// </summary>
    internal void TryBind()
    {
        if (_isBinding || _bindableComponent is null || DataSource is null) return;

        var context = _bindableComponent.BindingContext;
        if (context is null)
        {
            if (_bindableComponent is Control component) component.BindingContextChanged += OnBindableComponentBindingContextChanged;

            return;
        }

        if (_bindableComponent is Control boundComponent) boundComponent.BindingContextChanged -= OnBindableComponentBindingContextChanged;

        _bindingManagerBase = context[DataSource, _bindingMemberInfo.BindingPath];
        _bindingManagerBase.Bindings.AddInternal(this);
        _bindingManagerBase.CurrentChanged += OnCurrentChanged;

        WireControlProperty();
        RewireCurrentItemNotification();

        _isBinding = true;

        PushToControl();
    }

    internal void Unbind()
    {
        if (_bindableComponent is Control component) component.BindingContextChanged -= OnBindableComponentBindingContextChanged;

        UnwireControlProperty();
        UnwireCurrentItemNotification();

        if (_bindingManagerBase is not null)
        {
            _bindingManagerBase.CurrentChanged -= OnCurrentChanged;
            _bindingManagerBase.Bindings.RemoveInternal(this);
        }

        _bindingManagerBase = null;
        _isBinding = false;
    }

    internal void PushToControl()
    {
        if (_bindableComponent is null || ControlUpdateMode == ControlUpdateMode.Never) return;
        if (_controlProperty is null && !WireControlProperty()) return;

        try
        {
            var current = _bindingManagerBase?.Current;
            var raw = current is null ? null : ReadSourceField(current);
            if (IsDataSourceNull(raw)) raw = NullValue;

            var formatArgs = new ConvertEventArgs(raw, _controlProperty!.PropertyType);
            OnFormat(formatArgs);
            var formatted = ApplyFormatting(formatArgs.Value, raw);

            _controlProperty.SetValue(_bindableComponent, formatted);

            Complete(BindingCompleteState.Success, BindingCompleteContext.ControlUpdate, string.Empty, null);
        }
        catch (Exception ex)
        {
            Complete(BindingCompleteState.Exception, BindingCompleteContext.ControlUpdate, ex.Message, ex);
        }
    }

    internal void PullFromControl()
    {
        if (_bindableComponent is null || DataSourceUpdateMode == DataSourceUpdateMode.Never) return;
        if (_bindingManagerBase?.Current is not { } current) return;
        if (_controlProperty is null && !WireControlProperty()) return;

        try
        {
            var fieldType = SourceFieldType(current);
            var raw = _controlProperty!.GetValue(_bindableComponent);

            var parseArgs = new ConvertEventArgs(raw, fieldType);
            OnParse(parseArgs);
            var parsed = ApplyFormatting(parseArgs.Value, raw, fieldType);

            if (Equals(parsed, NullValue)) parsed = DataSourceNullValue;

            WriteSourceField(current, parsed);

            Complete(BindingCompleteState.Success, BindingCompleteContext.DataSourceUpdate, string.Empty, null);
        }
        catch (Exception ex)
        {
            Complete(BindingCompleteState.Exception, BindingCompleteContext.DataSourceUpdate, ex.Message, ex);
        }
    }

    /// <summary>Applies FormatString when the Format event left the value untouched, then converts to the target type.</summary>
    private object? ApplyFormatting(object? converted, object? original) => ApplyFormatting(converted, original, _controlProperty!.PropertyType);

    private object? ApplyFormatting(object? converted, object? original, Type targetType)
    {
        var value = converted;

        if (FormattingEnabled && ReferenceEquals(value, original) && !string.IsNullOrEmpty(FormatString))
        {
            // The same composite format WinForms wraps FormatString in, so a plain .NET
            // format specifier ("N2", "yyyy-MM-dd") works the way a bound DateTimePicker or
            // numeric field expects it to, through whatever provider FormatInfo names.
            value = string.Format(FormatInfo, "{0:" + FormatString + "}", value);
        }

        if (value is not null && !targetType.IsInstanceOfType(value))
        {
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            value = Convert.ChangeType(value, underlying, FormatInfo ?? CultureInfo.CurrentCulture);
        }

        return value;
    }

    private static bool IsDataSourceNull(object? value) => value is null or DBNull;

    private object ReadSourceField(object current)
    {
        if (_bindingMemberInfo.BindingField.Length == 0) return current;

        var descriptor = TypeDescriptor.GetProperties(current).Find(_bindingMemberInfo.BindingField, true);

        return descriptor is null ? current : descriptor.GetValue(current)!;
    }

    private void WriteSourceField(object current, object? value)
    {
        if (_bindingMemberInfo.BindingField.Length == 0) return;

        var descriptor = TypeDescriptor.GetProperties(current).Find(_bindingMemberInfo.BindingField, true);
        descriptor?.SetValue(current, value);
    }

    private Type SourceFieldType(object current)
    {
        if (_bindingMemberInfo.BindingField.Length == 0) return current.GetType();

        return TypeDescriptor.GetProperties(current).Find(_bindingMemberInfo.BindingField, true)?.PropertyType ?? typeof(object);
    }

    private bool WireControlProperty()
    {
        if (_bindableComponent is null) return false;

        _controlProperty ??= _bindableComponent.GetType().GetProperty(PropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (_controlProperty is null) return false;

        if (DataSourceUpdateMode == DataSourceUpdateMode.OnPropertyChanged) WireControlChangedEvent();
        if (DataSourceUpdateMode == DataSourceUpdateMode.OnValidation) WireValidated();

        return true;
    }

    private void UnwireControlProperty()
    {
        UnwireControlChangedEvent();
        UnwireValidated();
    }

    private void WireControlChangedEvent()
    {
        if (_controlChangedHandler is not null || _bindableComponent is null) return;

        _controlChangedEvent = _bindableComponent.GetType().GetEvent(PropertyName + "Changed", BindingFlags.Public | BindingFlags.Instance);
        if (_controlChangedEvent is null || _controlChangedEvent.EventHandlerType != typeof(EventHandler)) return;

        _controlChangedHandler = new EventHandler(OnControlPropertyChanged);
        _controlChangedEvent.AddEventHandler(_bindableComponent, _controlChangedHandler);
    }

    private void UnwireControlChangedEvent()
    {
        if (_controlChangedEvent is not null && _controlChangedHandler is not null) _controlChangedEvent.RemoveEventHandler(_bindableComponent, _controlChangedHandler);

        _controlChangedEvent = null;
        _controlChangedHandler = null;
    }

    private void WireValidated()
    {
        if (_wiredToValidated || Control is not { } control) return;

        control.Validated += OnControlValidated;
        _wiredToValidated = true;
    }

    private void UnwireValidated()
    {
        if (!_wiredToValidated || Control is not { } control) return;

        control.Validated -= OnControlValidated;
        _wiredToValidated = false;
    }

    private void RewireCurrentItemNotification()
    {
        UnwireCurrentItemNotification();

        if (ControlUpdateMode != ControlUpdateMode.OnPropertyChanged) return;
        if (_bindingManagerBase?.Current is not INotifyPropertyChanged notifying) return;

        notifying.PropertyChanged += OnSourcePropertyChanged;
        _wiredCurrentItem = notifying;
    }

    private void UnwireCurrentItemNotification()
    {
        if (_wiredCurrentItem is null) return;

        _wiredCurrentItem.PropertyChanged -= OnSourcePropertyChanged;
        _wiredCurrentItem = null;
    }

    private void OnBindableComponentBindingContextChanged(object? sender, EventArgs e) => TryBind();

    private void OnCurrentChanged(object? sender, EventArgs e) => RewireCurrentItemNotification();

    private void OnControlPropertyChanged(object? sender, EventArgs e) => PullFromControl();

    private void OnControlValidated(object? sender, EventArgs e) => PullFromControl();

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || string.Equals(e.PropertyName, _bindingMemberInfo.BindingField, StringComparison.OrdinalIgnoreCase))
        {
            PushToControl();
        }
    }

    private void Complete(BindingCompleteState state, BindingCompleteContext context, string errorText, Exception? exception)
    {
        var args = new BindingCompleteEventArgs(this, state, context, errorText, exception);
        OnBindingComplete(args);
        _bindingManagerBase?.NotifyBindingComplete(args);

        if (exception is not null) _bindingManagerBase?.NotifyDataError(exception);
    }
}
