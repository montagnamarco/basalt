namespace System.Windows.Forms;

/// <summary>The bindings a control (or any IBindableComponent) owns, one per bound property.</summary>
public class ControlBindingsCollection : BindingsCollection
{
    public ControlBindingsCollection(IBindableComponent control)
    {
        BindableComponent = control;
    }

    public IBindableComponent BindableComponent { get; }

    public Control? Control => BindableComponent as Control;

    public DataSourceUpdateMode DefaultDataSourceUpdateMode { get; set; } = DataSourceUpdateMode.OnValidation;

    public Binding? this[string propertyName]
    {
        get
        {
            foreach (var binding in Bindings())
            {
                if (string.Equals(binding.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase)) return binding;
            }

            return null;
        }
    }

    public new void Add(Binding binding) => base.Add(binding);

    public Binding Add(string propertyName, object dataSource, string? dataMember) =>
        Add(propertyName, dataSource, dataMember, false, DefaultDataSourceUpdateMode);

    public Binding Add(string propertyName, object dataSource, string? dataMember, bool formattingEnabled) =>
        Add(propertyName, dataSource, dataMember, formattingEnabled, DefaultDataSourceUpdateMode);

    public Binding Add(string propertyName, object dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode updateMode) =>
        Add(propertyName, dataSource, dataMember, formattingEnabled, updateMode, null);

    public Binding Add(
        string propertyName, object dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode updateMode, object? nullValue) =>
        Add(propertyName, dataSource, dataMember, formattingEnabled, updateMode, nullValue, string.Empty);

    public Binding Add(
        string propertyName, object dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode updateMode, object? nullValue,
        string formatString) =>
        Add(propertyName, dataSource, dataMember, formattingEnabled, updateMode, nullValue, formatString, null);

    public Binding Add(
        string propertyName, object dataSource, string? dataMember, bool formattingEnabled, DataSourceUpdateMode updateMode, object? nullValue,
        string formatString, IFormatProvider? formatInfo)
    {
        var binding = new Binding(propertyName, dataSource, dataMember, formattingEnabled, updateMode, nullValue, formatString, formatInfo);
        Add(binding);

        return binding;
    }

    protected override void AddCore(Binding dataBinding)
    {
        ArgumentNullException.ThrowIfNull(dataBinding);

        if (string.IsNullOrEmpty(dataBinding.PropertyName))
        {
            throw new ArgumentException("The PropertyName of the binding cannot be empty.", nameof(dataBinding));
        }

        foreach (var existing in Bindings())
        {
            if (string.Equals(existing.PropertyName, dataBinding.PropertyName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"A binding for the property '{dataBinding.PropertyName}' already exists.", nameof(dataBinding));
            }
        }

        base.AddCore(dataBinding);
        dataBinding.SetBindableComponent(BindableComponent);
        dataBinding.TryBind();
    }

    public new void Clear() => base.Clear();

    protected override void ClearCore()
    {
        foreach (var binding in Bindings()) binding.Unbind();

        base.ClearCore();
    }

    public new void Remove(Binding binding) => base.Remove(binding);

    public new void RemoveAt(int index) => base.RemoveAt(index);

    protected override void RemoveCore(Binding dataBinding)
    {
        dataBinding.Unbind();
        base.RemoveCore(dataBinding);
    }

    private IEnumerable<Binding> Bindings()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
}
