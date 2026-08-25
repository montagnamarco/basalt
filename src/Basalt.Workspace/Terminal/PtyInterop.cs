using System.Runtime.InteropServices;

namespace Basalt.Workspace.Terminal;

/// <summary>
/// Native entry points for pseudo-terminals on Unix-like systems.
///
/// On macOS and Linux both symbols live in libc, so no extra native package is
/// required. Windows uses a different mechanism entirely (ConPTY) and is
/// handled by <see cref="ConPtyInterop"/>.
/// </summary>
internal static partial class UnixPty
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinSize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixel;
        public ushort YPixel;
    }

    /// <summary>Allocates a pseudo-terminal pair and returns 0 on success.</summary>
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int openpty(
        out int master, out int worker, IntPtr name, IntPtr termios, ref WinSize winsize);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial nint read(int fd, byte[] buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial nint write(int fd, byte[] buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int close(int fd);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int kill(int pid, int signal);

    /// <summary>
    /// Puts the process into its own process group, so the whole group can be
    /// signalled at once. Without it a terminal cannot reliably terminate the
    /// shell it started: the launcher process sits between the two and the
    /// signal never reaches the shell.
    /// </summary>
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int setpgid(int pid, int pgid);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int getpgid(int pid);

    /// <summary>
    /// TIOCSWINSZ would resize a live terminal, but ioctl is variadic and
    /// P/Invoke marshals its payload incorrectly on this platform: the call
    /// reports success while the kernel reads the wrong bytes. Verified against
    /// an equivalent C program, which applies the size correctly. Resizing is
    /// therefore not attempted; the size is fixed when the terminal is created.
    /// </summary>
    internal static nuint TiocSwinsz => OperatingSystem.IsMacOS() ? 0x80087467 : 0x5414;

    internal const int SIGHUP = 1;
    internal const int SIGINT = 2;
    internal const int SIGKILL = 9;
}
