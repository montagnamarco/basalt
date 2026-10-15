using System.Runtime.InteropServices;

namespace Basalt.Workspace.Terminal;

/// <summary>
/// Starts a terminal's child in a session of its own, with the terminal as its standard streams.
/// </summary>
/// <remarks>
/// <para>
/// Process.Start leaves the child in the IDE's session and process group. A shell started that way runs its
/// commands in the same group, since without a controlling terminal it has no job control: when the shell got
/// SIGHUP (its tab closed) or exited with jobs in the background, it passed SIGHUP on to that group, and the IDE
/// itself was in it. Measured: a shell running "sleep 30", closed, ended the process that started it with
/// SIGHUP (exit status 129). posix_spawn with POSIX_SPAWN_SETSID gives the child its own session, so its group
/// is its own and whatever it signals stays there.
/// </para>
/// <para>
/// The child also gets the signal defaults and an empty signal mask: posix_spawn otherwise passes on the
/// runtime's dispositions, and .NET ignores SIGPIPE, which a pipeline such as "yes | head" relies on.
/// </para>
/// </remarks>
internal static partial class UnixSpawn
{
    // spawn.h: macOS and glibc number these flags differently.
    private static short SetSignalDefaults => 0x04;
    private static short SetSignalMask => 0x08;
    private static short SetSession => (short)(OperatingSystem.IsMacOS() ? 0x0400 : 0x80);

    /// <summary>Closes every descriptor the file actions do not name (macOS only).</summary>
    private const short CloseOthersOnMacOS = 0x4000;

    // posix_spawnattr_t and posix_spawn_file_actions_t are a pointer on macOS and a structure of a few hundred
    // bytes in glibc; a generous block serves both. sigset_t is 4 bytes on macOS and 128 in glibc.
    private const int OpaqueSize = 1024;
    private const int SignalSetSize = 128;

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawnattr_init(IntPtr attributes);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawnattr_destroy(IntPtr attributes);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawnattr_setflags(IntPtr attributes, short flags);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawnattr_setsigmask(IntPtr attributes, IntPtr mask);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawnattr_setsigdefault(IntPtr attributes, IntPtr signals);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawn_file_actions_init(IntPtr actions);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawn_file_actions_destroy(IntPtr actions);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawn_file_actions_adddup2(IntPtr actions, int descriptor, int target);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int posix_spawn_file_actions_addclose(IntPtr actions, int descriptor);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawn_file_actions_addopen(IntPtr actions, int descriptor, string path, int flags, int mode);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int ttyname_r(int descriptor, byte[] buffer, nint size);

    /// <summary>The device name of a terminal descriptor, or null. ttyname_r, since terminals open on several threads.</summary>
    private static string? TerminalName(int descriptor)
    {
        var buffer = new byte[1024];
        if (ttyname_r(descriptor, buffer, buffer.Length) != 0) return null;

        var length = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawn_file_actions_addchdir_np(IntPtr actions, string path);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int posix_spawn(out int pid, string path, IntPtr actions, IntPtr attributes, IntPtr[] arguments, IntPtr[] environment);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int waitpid(int pid, out int status, int options);

    /// <summary>Starts the program and returns its process id. Throws when it cannot be started.</summary>
    public static int Start(
        string program, IReadOnlyList<string> arguments, string workingDirectory,
        IReadOnlyDictionary<string, string> environment, int terminal, int master)
    {
        var path = Resolve(program, environment.TryGetValue("PATH", out var searchPath) ? searchPath : null);

        var attributes = Marshal.AllocHGlobal(OpaqueSize);
        var actions = Marshal.AllocHGlobal(OpaqueSize);
        var mask = Marshal.AllocHGlobal(SignalSetSize);
        var defaults = Marshal.AllocHGlobal(SignalSetSize);
        var argv = Strings([path, .. arguments]);
        var envp = Strings([.. environment.Select(pair => pair.Key + "=" + pair.Value)]);

        try
        {
            Check(posix_spawnattr_init(attributes), "posix_spawnattr_init");
            Check(posix_spawn_file_actions_init(actions), "posix_spawn_file_actions_init");

            // An empty mask, and every signal back to its default.
            for (var i = 0; i < SignalSetSize; i++)
            {
                Marshal.WriteByte(mask, i, 0);
                Marshal.WriteByte(defaults, i, 0xFF);
            }

            Check(posix_spawnattr_setsigmask(attributes, mask), "posix_spawnattr_setsigmask");
            Check(posix_spawnattr_setsigdefault(attributes, defaults), "posix_spawnattr_setsigdefault");

            var flags = (short)(SetSession | SetSignalDefaults | SetSignalMask);
            if (OperatingSystem.IsMacOS()) flags |= CloseOthersOnMacOS;
            Check(posix_spawnattr_setflags(attributes, flags), "posix_spawnattr_setflags");

            // Opened by name rather than duplicated: the session leader opening its terminal makes it the
            // session's controlling terminal, so the kernel itself turns Ctrl+C into SIGINT for the foreground
            // job, honours raw mode, and lets programs open /dev/tty.
            var name = TerminalName(terminal);
            if (name is { Length: > 0 })
            {
                Check(posix_spawn_file_actions_addopen(actions, 0, name, 2 /* O_RDWR */, 0), "addopen");
                Check(posix_spawn_file_actions_adddup2(actions, 0, 1), "adddup2");
                Check(posix_spawn_file_actions_adddup2(actions, 0, 2), "adddup2");
            }
            else
            {
                Check(posix_spawn_file_actions_adddup2(actions, terminal, 0), "adddup2");
                Check(posix_spawn_file_actions_adddup2(actions, terminal, 1), "adddup2");
                Check(posix_spawn_file_actions_adddup2(actions, terminal, 2), "adddup2");
            }

            // On macOS everything else is closed by the flag; elsewhere the two ends are closed by name.
            if (!OperatingSystem.IsMacOS())
            {
                if (terminal > 2) Check(posix_spawn_file_actions_addclose(actions, terminal), "addclose");
                Check(posix_spawn_file_actions_addclose(actions, master), "addclose");
            }

            // addchdir_np is in glibc 2.29 and macOS 10.15. Without it /bin/sh changes the directory and then
            // replaces itself with the program, so the program keeps the id the caller is given.
            var started = path;
            try
            {
                Check(posix_spawn_file_actions_addchdir_np(actions, workingDirectory), "addchdir");
            }
            catch (EntryPointNotFoundException)
            {
                started = "/bin/sh";
                Free(argv);
                argv = Strings(["/bin/sh", "-c", "cd \"$0\" && exec \"$@\"", workingDirectory, path, .. arguments]);
            }

            var result = posix_spawn(out var pid, started, actions, attributes, argv, envp);
            if (result != 0) throw new IOException($"Could not start {program}: error {result}.");

            return pid;
        }
        finally
        {
            posix_spawn_file_actions_destroy(actions);
            posix_spawnattr_destroy(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(mask);
            Marshal.FreeHGlobal(defaults);
            Free(argv);
            Free(envp);
        }
    }

    /// <summary>
    /// The exit status waitpid reported, as a shell reports it: the code for a normal exit, 128 plus the signal
    /// for a process a signal ended.
    /// </summary>
    public static int ExitCodeOf(int status) =>
        (status & 0x7F) == 0 ? (status >> 8) & 0xFF : 128 + (status & 0x7F);

    /// <summary>A program named without a directory, found on the child's PATH as a shell would find it.</summary>
    private static string Resolve(string program, string? searchPath)
    {
        if (program.Contains('/')) return program;

        foreach (var folder in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, program);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"{program} was not found on the PATH.", program);
    }

    private static void Check(int result, string call)
    {
        if (result != 0) throw new IOException($"{call} failed: error {result}.");
    }

    private static IntPtr[] Strings(IReadOnlyList<string> values)
    {
        var pointers = new IntPtr[values.Count + 1];
        for (var i = 0; i < values.Count; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
        return pointers;
    }

    private static void Free(IntPtr[] pointers)
    {
        foreach (var pointer in pointers)
            if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
    }
}
