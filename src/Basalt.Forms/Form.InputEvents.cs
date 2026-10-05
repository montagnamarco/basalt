namespace System.Windows.Forms;

public partial class Form
{
    public event InputLanguageChangedEventHandler? InputLanguageChanged;
    public event InputLanguageChangingEventHandler? InputLanguageChanging;
    public event EventHandler? ResizeBegin;
    public event EventHandler? ResizeEnd;
    public event EventHandler? MenuStart;
    public event EventHandler? MenuComplete;

    protected virtual void OnInputLanguageChanged(InputLanguageChangedEventArgs e) => InputLanguageChanged?.Invoke(this, e);
    protected virtual void OnInputLanguageChanging(InputLanguageChangingEventArgs e) => InputLanguageChanging?.Invoke(this, e);
    protected virtual void OnResizeBegin(EventArgs e) => ResizeBegin?.Invoke(this, e);
    protected virtual void OnResizeEnd(EventArgs e) => ResizeEnd?.Invoke(this, e);
    protected virtual void OnMenuStart(EventArgs e) => MenuStart?.Invoke(this, e);
    protected virtual void OnMenuComplete(EventArgs e) => MenuComplete?.Invoke(this, e);

    public bool IsRestrictedWindow => false;
}
