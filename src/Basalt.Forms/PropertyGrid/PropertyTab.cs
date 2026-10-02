using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms.Design;

/// <summary>
/// A page a PropertyGrid can show: which properties of a component it lists,
/// in what order. PropertyGrid.PropertyTabs only ever holds the default
/// PropertiesTab this family ships (see the family's left-out list for why).
/// </summary>
public abstract class PropertyTab : IExtenderProvider
{
    private bool _disposed;

    protected PropertyTab()
    {
    }

    public virtual Bitmap? Bitmap => null;

    public virtual object[]? Components { get; set; }

    public virtual string? HelpKeyword => TabName;

    public abstract string? TabName { get; }

    /// <summary>Whether this tab applies to extendee; the default accepts every component.</summary>
    public virtual bool CanExtend(object? extendee) => true;

    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;
    }

    ~PropertyTab()
    {
        if (!_disposed) Dispose(false);
    }

    public virtual PropertyDescriptor? GetDefaultProperty(object component) => TypeDescriptor.GetDefaultProperty(component);

    public virtual PropertyDescriptorCollection GetProperties(object component) => GetProperties(component, null);

    public abstract PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes);

    public virtual PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object component, Attribute[]? attributes) =>
        GetProperties(component, attributes);
}
