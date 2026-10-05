using System.Diagnostics;

namespace Basalt.Workspace;

/// <summary>Runs the user's application and reports its output.</summary>
public sealed class ProcessRunService : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>The arguments the most recent <see cref="Start"/> passed to
    /// "dotnet", for tests that must tell "run the build's output" apart from
    /// a rebuild on run.</summary>
    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    /// <summary>
    /// Used by <see cref="StartAsync"/> to look where the build wrote, with the
    /// same session the build used.
    /// </summary>
    public WindowsDesktopCompatibilitySession Compatibility { get; set; } = new();

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;

    /// <summary>
    /// Starts the project with "dotnet run --no-build" in the project
    /// directory.
    ///
    /// Launching through "dotnet run" rather than "dotnet &lt;dll&gt;" is
    /// deliberate: only "dotnet run" applies Properties/launchSettings.json
    /// (environment variables, arguments, applicationUrl), and without it
    /// web and Blazor apps start in Production. The caller builds first, so
    /// "--no-build" keeps run from compiling a second time.
    ///
    /// <paramref name="compatibilityArguments"/> are the MSBuild properties the
    /// build used (<see cref="WindowsDesktopCompatibilitySession.GetArgumentsAsync"/>).
    /// They are passed in, already read, so that starting never waits on the
    /// disk: this method runs on the interface thread.
    /// </summary>
    public void Start(string projectPath, string configuration = "Debug",
        IReadOnlyList<string>? compatibilityArguments = null)
    {
        if (IsRunning)
            throw new InvalidOperationException("The application is already running.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // The build put its output in bin/basalt; without the same properties
        // "dotnet run" would look in bin/Debug and find nothing, or a
        // Windows-only framework it cannot run.
        var arguments = new List<string> { "run", "--project", projectPath, "-c", configuration, "--no-build" };
        if (compatibilityArguments is not null) arguments.AddRange(compatibilityArguments);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        LastArguments = [.. startInfo.ArgumentList];

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Emit(e.Data);
        _process.ErrorDataReceived += (_, e) => Emit(e.Data);
        _process.Exited += (_, _) => Exited?.Invoke(this, _process?.ExitCode ?? -1);

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>
    /// <see cref="Start"/> with the properties of <see cref="Compatibility"/>,
    /// read off the calling thread first.
    /// </summary>
    public async Task StartAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default)
    {
        var arguments = await Compatibility.GetArgumentsAsync(projectPath, ct).ConfigureAwait(false);
        Start(projectPath, configuration, arguments);
    }

    public void Stop()
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            // The whole tree: "dotnet run" starts the application as a child.
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the call.
        }
    }

    private void Emit(string? line)
    {
        if (line is not null) OutputReceived?.Invoke(this, line);
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
    }
}
