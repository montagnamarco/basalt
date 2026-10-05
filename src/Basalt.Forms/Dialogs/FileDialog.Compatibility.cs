namespace System.Windows.Forms;

public abstract partial class FileDialog
{
    private readonly FileDialogCustomPlacesCollection _customPlaces = [];

    public FileDialogCustomPlacesCollection CustomPlaces => _customPlaces;

    protected override IntPtr HookProc(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam) =>
        throw new PlatformNotSupportedException("Win32 common-dialog hooks require a native dialog handle.");

    protected virtual IntPtr Instance =>
        throw new PlatformNotSupportedException("Win32 common-dialog module instances are unavailable for platform pickers.");

    protected int Options =>
        throw new PlatformNotSupportedException("Win32 OPENFILENAME flags are unavailable for platform pickers.");
}
