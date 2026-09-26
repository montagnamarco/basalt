using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Basalt.Workspace.Terminal;

/// <summary>
/// Pseudo-terminal for Windows, built on ConPTY.
///
/// ConPTY answers the same need <see cref="UnixPtyConnection"/> answers with
/// <c>openpty</c>: a child process gets a device it believes is a real
/// console, so shells print prompts and full-screen programs redraw
/// correctly. Windows solves it with a dedicated pseudo-console handle plus
/// two anonymous pipes rather than a single master/worker descriptor pair.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPtyConnection : IPtyConnection
{
    /// <summary>ETX, the byte a terminal sends when Ctrl+C is pressed.</summary>
    private const string InterruptCharacter = "\u0003";

    private readonly SafePseudoConsoleHandle _pseudoConsole;
    private readonly FileStream _inputWriter;
    private readonly FileStream _outputReader;
    private readonly Process _process;
    private readonly CancellationTokenSource _reading = new();
    private readonly Lock _writeGate = new();
    private bool _disposed;

    public WindowsPtyConnection(
        string shellPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int columns = 80,
        int rows = 24,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        // Two pipes, each carrying data in one direction: the console reads
        // its "keyboard" input from one and writes its "screen" output to the
        // other. We keep the opposite end of each so we can feed keystrokes in
        // and drain output out.
        if (!ConPtyInterop.CreatePipe(out var consoleInputRead, out var inputWriter, IntPtr.Zero, 0) ||
            !ConPtyInterop.CreatePipe(out var outputReader, out var consoleOutputWrite, IntPtr.Zero, 0))
            throw new IOException($"Could not create the pipes for a pseudo-console: {Marshal.GetLastWin32Error()}");

        _inputWriter = new FileStream(inputWriter, FileAccess.Write);
        _outputReader = new FileStream(outputReader, FileAccess.Read);

        var size = new ConPtyInterop.COORD { X = (short)columns, Y = (short)rows };
        var created = ConPtyInterop.CreatePseudoConsole(size, consoleInputRead, consoleOutputWrite, 0, out var handle);

        // The pseudo-console duplicates the handles it needs; ours would
        // otherwise keep the pipes open after the child is gone.
        consoleInputRead.Dispose();
        consoleOutputWrite.Dispose();

        if (created != 0)
            throw new IOException($"Could not create a pseudo-console: HRESULT 0x{created:X8}");

        _pseudoConsole = new SafePseudoConsoleHandle(handle);

        try
        {
            _process = StartProcess(shellPath, arguments, workingDirectory, environment);
        }
        catch
        {
            _pseudoConsole.Dispose();
            _inputWriter.Dispose();
            _outputReader.Dispose();
            throw;
        }

        _ = Task.Run(PumpOutputAsync);
    }

    public bool IsRunning => !_process.HasExited;

    /// <summary>
    /// The id CreateProcessW assigned the shell, captured independently of
    /// the <see cref="Process"/> wrapper so it stays readable after this
    /// connection, and the wrapper, are disposed. Tests use it to confirm the
    /// child actually terminates rather than trusting a managed flag alone.
    /// </summary>
    public int ProcessId { get; private set; }

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;

    public void Write(string text)
    {
        if (!IsRunning) return;

        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_writeGate)
        {
            try
            {
                _inputWriter.Write(bytes, 0, bytes.Length);
                _inputWriter.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The child closed its input, or we are disposing; either way
                // there is nowhere left for these keystrokes to go.
            }
        }
    }

    /// <summary>
    /// Applies the new size live: unlike the Unix implementation, ConPTY has a
    /// dedicated call for this and does not need the workaround the comment
    /// on <see cref="UnixPtyConnection.Resize"/> describes.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        if (!IsRunning) return;

        ConPtyInterop.ResizePseudoConsole(_pseudoConsole, new ConPtyInterop.COORD
        {
            X = (short)columns,
            Y = (short)rows
        });
    }

    /// <summary>
    /// Writes the interrupt character into the terminal rather than signalling
    /// the shell directly, so the foreground job receives it, which is what
    /// pressing Ctrl+C in a terminal actually does.
    /// </summary>
    public void Interrupt() => Write(InterruptCharacter);

    private async Task PumpOutputAsync()
    {
        var buffer = new byte[8192];
        var decoder = Encoding.UTF8.GetDecoder();
        var characters = new char[8192];

        while (!_reading.IsCancellationRequested)
        {
            int count;
            try
            {
                count = await Task.Run(() => _outputReader.Read(buffer, 0, buffer.Length))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                break;
            }

            // Zero means the far end of the pipe closed, which ConPTY does
            // once the child and its descendants have all exited.
            if (count <= 0) break;

            // A multi-byte character can straddle two reads, so the decoder is
            // kept across iterations instead of decoding each buffer alone.
            var produced = decoder.GetChars(buffer, 0, count, characters, 0);
            if (produced > 0) OutputReceived?.Invoke(this, new string(characters, 0, produced));
        }
    }

    /// <summary>
    /// Launches the shell attached to the pseudo-console.
    ///
    /// CreateProcessW needs an attribute list carrying the pseudo-console
    /// handle and an extended STARTUPINFO to go with it; neither has a
    /// managed equivalent, which is why this cannot go through
    /// <see cref="Process.Start"/>. Once the process exists, it is handed to
    /// a normal <see cref="Process"/> object by id, so exit notification,
    /// waiting and killing behave exactly like the Unix implementation.
    /// </summary>
    private Process StartProcess(
        string shellPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        nint attributeListSize = 0;
        ConPtyInterop.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);

        var attributeList = Marshal.AllocHGlobal(attributeListSize);
        var commandLine = Marshal.StringToHGlobalUni(BuildCommandLine(shellPath, arguments));
        var environmentBlock = BuildEnvironmentBlock(environment);

        // UpdateProcThreadAttribute takes a pointer TO the attribute value,
        // not the value itself: for PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE that
        // value is the HPCON handle, so a handle-sized cell holding it is
        // needed, separate from the handle itself. Passing the handle value
        // where a pointer was expected does not fail loudly - CreateProcessW
        // silently starts the shell attached to this process's own console
        // instead, which is what produced the earlier "exits immediately"
        // symptom this comment now documents.
        var pseudoConsoleCell = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(pseudoConsoleCell, _pseudoConsole.DangerousGetHandle());

        try
        {
            if (!ConPtyInterop.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
                throw new IOException($"Could not initialize the process attribute list: {Marshal.GetLastWin32Error()}");

            if (!ConPtyInterop.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ConPtyInterop.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    pseudoConsoleCell,
                    IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
                throw new IOException($"Could not attach the pseudo-console to the process: {Marshal.GetLastWin32Error()}");

            var startupInfo = new ConPtyInterop.STARTUPINFOEX
            {
                StartupInfo = new ConPtyInterop.STARTUPINFO
                {
                    cb = Marshal.SizeOf<ConPtyInterop.STARTUPINFOEX>()
                },
                lpAttributeList = attributeList
            };

            var creationFlags = ConPtyInterop.EXTENDED_STARTUPINFO_PRESENT | ConPtyInterop.CREATE_UNICODE_ENVIRONMENT;

            // No inherited handles: the child's console is wired up through the
            // pseudo-console attribute, not through inherited stdio handles.
            var created = ConPtyInterop.CreateProcessW(
                IntPtr.Zero,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                bInheritHandles: false,
                creationFlags,
                environmentBlock,
                workingDirectory,
                ref startupInfo,
                out var processInformation);

            if (!created)
                throw new IOException($"Could not start '{shellPath}': {Marshal.GetLastWin32Error()}");

            ConPtyInterop.CloseHandle(processInformation.hThread);

            // CreateProcessW can hand back a PID for a process that has
            // already died before we get here - for example when the console
            // subsystem could not finish attaching the new process to the
            // pseudo-console, which fails with STATUS_DLL_INIT_FAILED rather
            // than a CreateProcessW error. Process.GetProcessById throws a
            // bare ArgumentException in that case; checking first turns that
            // into a message that names the exit code, which is what a user
            // needs to tell "no shell available" apart from "ConPTY itself is
            // unusable in this session" (for example, no interactive window
            // station, which is outside anything this class can fix).
            if (ConPtyInterop.WaitForSingleObject(processInformation.hProcess, 0) == ConPtyInterop.WAIT_OBJECT_0)
            {
                ConPtyInterop.GetExitCodeProcess(processInformation.hProcess, out var earlyExitCode);
                ConPtyInterop.CloseHandle(processInformation.hProcess);
                throw new IOException(
                    $"'{shellPath}' exited immediately (exit code 0x{earlyExitCode:X8}) instead of attaching to the pseudo-console.");
            }

            ProcessId = processInformation.dwProcessId;

            Process process;

            try
            {
                process = Process.GetProcessById(processInformation.dwProcessId);
                process.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // It died between the check above and here: the same failure,
                // a moment later. Read from the handle still held, so the
                // message names the exit code rather than a missing process.
                ConPtyInterop.GetExitCodeProcess(processInformation.hProcess, out var lateExitCode);
                ConPtyInterop.CloseHandle(processInformation.hProcess);

                throw new IOException(
                    $"'{shellPath}' exited immediately (exit code 0x{lateExitCode:X8}) instead of attaching to the pseudo-console.",
                    ex);
            }

            process.Exited += (_, _) => Exited?.Invoke(this, SafeExitCode(process));

            ConPtyInterop.CloseHandle(processInformation.hProcess);

            return process;
        }
        finally
        {
            ConPtyInterop.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            Marshal.FreeHGlobal(commandLine);
            Marshal.FreeHGlobal(environmentBlock);
            Marshal.FreeHGlobal(pseudoConsoleCell);
        }
    }

    /// <summary>
    /// Reads the exit code defensively: a process that exited abnormally can
    /// leave this unavailable, and reporting a fallback beats letting the
    /// exit notification itself throw.
    /// </summary>
    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    /// <summary>
    /// Builds a Win32 command line, quoting each argument per the rules
    /// CommandLineToArgvW expects. Unlike the Unix implementation, no shell is
    /// interposed: CreateProcessW parses this string itself, so getting the
    /// quoting wrong here would corrupt the very first argument the shell sees.
    /// </summary>
    private static string BuildCommandLine(string shellPath, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, shellPath);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendArgument(builder, argument);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes one argument using the algorithm CommandLineToArgvW documents:
    /// backslashes only need doubling when they immediately precede a quote
    /// that must itself be escaped, or when they fall at the end of a quoted
    /// argument.
    /// </summary>
    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');

        for (var i = 0; i < argument.Length;)
        {
            var current = argument[i++];

            if (current == '\\')
            {
                var backslashCount = 1;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashCount++;
                    i++;
                }

                if (i == argument.Length)
                    builder.Append('\\', backslashCount * 2);
                else if (argument[i] == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append('"');
                    i++;
                }
                else
                    builder.Append('\\', backslashCount);
            }
            else if (current == '"')
                builder.Append('\\').Append('"');
            else
                builder.Append(current);
        }

        builder.Append('"');
    }

    /// <summary>
    /// Builds the double-null-terminated block CreateProcessW expects: the
    /// parent's own environment, with the caller's overrides applied on top,
    /// mirroring what the Unix constructor accepts.
    /// </summary>
    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string>? overrides)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            variables[(string)entry.Key] = (string)entry.Value!;

        if (overrides is not null)
            foreach (var (key, value) in overrides)
                variables[key] = value;

        var block = new StringBuilder();
        foreach (var (key, value) in variables)
            block.Append(key).Append('=').Append(value).Append('\0');
        block.Append('\0');

        // StringToHGlobalUni copies the string's own length verbatim, embedded
        // nulls included, then appends one more: exactly the double
        // termination CreateProcessW requires for CREATE_UNICODE_ENVIRONMENT.
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reading.Cancel();

        // Closing the pseudo-console first is what a terminal emulator does
        // when its window goes away: the underlying pipes break, which
        // unblocks the reader thread that is otherwise parked in Read().
        _pseudoConsole.Dispose();
        _inputWriter.Dispose();
        _outputReader.Dispose();

        try
        {
            if (!_process.HasExited && !_process.WaitForExit(300))
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(300);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            // Already gone.
        }

        _process.Dispose();
        _reading.Dispose();
    }
}
