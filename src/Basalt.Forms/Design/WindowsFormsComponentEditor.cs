using System.ComponentModel;

namespace System.Windows.Forms.Design;

/// <summary>A ComponentEditor that shows its pages in a ComponentEditorForm: GetComponentEditorPages names the page types, EditComponent builds and shows the form modally.</summary>
public abstract class WindowsFormsComponentEditor : ComponentEditor
{
    protected WindowsFormsComponentEditor()
    {
    }

    protected virtual Type[] GetComponentEditorPages() => [];

    protected virtual int GetInitialComponentEditorPageIndex() => 0;

    public override bool EditComponent(ITypeDescriptorContext? context, object component) => EditComponent(context, component, owner: null);

    public virtual bool EditComponent(ITypeDescriptorContext? context, object component, IWin32Window? owner)
    {
        var pages = GetComponentEditorPages();
        if (pages.Length == 0) return false;

        using var form = new ComponentEditorForm(component, pages);
        form.ShowForm(owner, GetInitialComponentEditorPageIndex());

        return form.IsDirty;
    }

    public bool EditComponent(object component, IWin32Window? owner) => EditComponent(context: null, component, owner);
}
