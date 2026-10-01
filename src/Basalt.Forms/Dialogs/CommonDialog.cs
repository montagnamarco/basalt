using System.ComponentModel;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// Base of the standard dialog boxes. ShowDialog resolves the owner window,
/// asks the concrete dialog to run through RunDialog and maps the result:
/// Windows' own common dialogs (file pickers, colour, font) are native
/// windows driven by a Win32 DialogProc, so RunDialog there sends a
/// WM_INITDIALOG-style message loop; here it drives Avalonia's own picker
/// (see FileDialog and FolderBrowserDialog) and waits for it synchronously.
/// </summary>
[ToolboxItemFilter("System.Windows.Forms")]
public abstract class CommonDialog : Component
{
    private IWin32Window? _owner;

    public CommonDialog()
    {
    }

    [Bindable(true)]
    [DefaultValue(null)]
    public object? Tag { get; set; }

    public event EventHandler? HelpRequest;

    /// <summary>
    /// Windows subclasses the native dialog's window procedure to intercept
    /// its messages; there is no such window here, so this hook is never
    /// called by our own RunDialog implementations. A caller that overrides
    /// it for a Win32 message it expects to see gets a clear failure instead
    /// of a silent no-op.
    /// </summary>
    protected virtual IntPtr HookProc(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam) =>
        throw new PlatformNotSupportedException("CommonDialog.HookProc is not supported: dialogs run through Avalonia's StorageProvider, not a Win32 DialogProc.");

    protected virtual void OnHelpRequest(EventArgs e) => HelpRequest?.Invoke(this, e);

    /// <summary>Windows subclasses the owner's window procedure while the dialog is open; there is no such hook here.</summary>
    protected virtual IntPtr OwnerWndProc(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam) =>
        throw new PlatformNotSupportedException("CommonDialog.OwnerWndProc is not supported: there is no owner window procedure to subclass on this platform.");

    public abstract void Reset();

    /// <summary>Runs the platform picker and reports whether the user confirmed a choice.</summary>
    protected abstract bool RunDialog(IntPtr hwndOwner);

    public DialogResult ShowDialog() => ShowDialog(null);

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        _owner = owner;

        try
        {
            return RunDialog(owner?.Handle ?? IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;
        }
        finally
        {
            _owner = null;
        }
    }

    /// <summary>
    /// The native window to open the platform picker over: the explicit
    /// owner's, else the active form's, else the first open form's, as the
    /// task asks (Form.Host, Form.ActiveForm, Application.OpenForms).
    /// </summary>
    private protected HostWindow? ResolveOwnerHost()
    {
        var ownerForm = _owner switch
        {
            Form form => form,
            Control control => control.FindForm() ?? control.TopLevelControl as Form,
            _ => null,
        };

        if (ownerForm?.Host is { } ownerHost) return ownerHost;
        if (Form.ActiveForm?.Host is { } activeHost) return activeHost;

        return Application.OpenForms.Snapshot().Select(form => form.Host).FirstOrDefault(host => host is not null);
    }
}
