using System.ComponentModel;

namespace System.Drawing.Design;

/// <summary>
/// A PropertyGrid row's "..." or drop-down editor: GetEditStyle says which
/// (none, a modal dialog, or a drop-down), EditValue runs it and returns the
/// new value. The context-less overloads are the pre-.NET 2.0 shape, kept
/// for binary compatibility and forwarding to the context-aware ones.
/// </summary>
public class UITypeEditor
{
    public UITypeEditor()
    {
    }

    public virtual bool IsDropDownResizable => false;

    public object? EditValue(IServiceProvider? provider, object? value) => EditValue(context: null, provider, value);

    public virtual object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value) => value;

    public UITypeEditorEditStyle GetEditStyle() => GetEditStyle(context: null);

    public virtual UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.None;

    public bool GetPaintValueSupported() => GetPaintValueSupported(context: null);

    public virtual bool GetPaintValueSupported(ITypeDescriptorContext? context) => false;

    public void PaintValue(object? value, Graphics canvas, Rectangle rectangle) =>
        PaintValue(new PaintValueEventArgs(context: null, value, canvas, rectangle));

    public virtual void PaintValue(PaintValueEventArgs e)
    {
    }
}
