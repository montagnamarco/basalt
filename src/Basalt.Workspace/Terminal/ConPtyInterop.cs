using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Basalt.Workspace.Terminal;

/// <summary>
/// Native entry points for ConPTY, the pseudo-console Windows introduced to
/// answer the same need <c>openpty</c> answers on Unix: give a child process a
/// device it believes is a real terminal. <see cref="UnixPty"/> covers macOS
/// and Linux; the two are different enough at the API level that sharing one
/// interop surface would obscure both.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class ConPtyInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    /// <summary>
    /// Identifies the pseudo-console attribute in a process thread attribute
    /// list. Documented nowhere but the ConPTY samples themselves; every one
    /// of them, including Microsoft's own, hard-codes this value.
    /// </summary>
    internal const nuint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, uint nSize);

    [LibraryImport("kernel32.dll")]
    internal static partial int CreatePseudoConsole(
        COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr hPC);

    [LibraryImport("kernel32.dll")]
    internal static partial int ResizePseudoConsole(SafePseudoConsoleHandle hPC, COORD size);

    [LibraryImport("kernel32.dll")]
    internal static partial void ClosePseudoConsole(IntPtr hPC);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref nint lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        nuint Attribute,
        IntPtr lpValue,
        nint cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    /// <summary>
    /// lpApplicationName and lpCommandLine stay as raw pointers rather than
    /// marshalled strings: Win32 documents lpCommandLine as a buffer the call
    /// may write into, which the source-generated marshaller for an immutable
    /// .NET string does not offer. The caller allocates and owns that buffer.
    /// </summary>
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcessW(
        IntPtr lpApplicationName,
        IntPtr lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);

    /// <summary>Signalled value returned by <see cref="WaitForSingleObject"/> for an object already set, here a process that has already exited.</summary>
    internal const uint WAIT_OBJECT_0 = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
}

/// <summary>
/// Owns the HPCON handle ConPTY hands back.
///
/// It is closed with <c>ClosePseudoConsole</c> rather than <c>CloseHandle</c>:
/// an HPCON is not a kernel object but a console-subsystem handle, and the two
/// close functions are not interchangeable.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SafePseudoConsoleHandle : SafeHandle
{
    public SafePseudoConsoleHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public SafePseudoConsoleHandle(IntPtr handle) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(handle);

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        ConPtyInterop.ClosePseudoConsole(handle);
        return true;
    }
}
