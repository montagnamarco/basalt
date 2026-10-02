namespace System.Windows.Forms;

/// <summary>What ComponentEditorForm gives each ComponentEditorPage it hosts: the Win32-window-less Control to parent the page's own controls under, and a way for the page to tell the form its component was changed.</summary>
public interface IComponentEditorPageSite
{
    Control GetControl();

    void SetDirty();
}
