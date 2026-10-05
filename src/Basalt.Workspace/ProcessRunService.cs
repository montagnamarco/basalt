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
    /// </summary>
    public void Start(string projectPath, string configuration = "Debug")
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

        foreach (var argument in new[] { "run", "--project", projectPath, "-c", configuration, "--no-build" })
            startInfo.ArgumentList.Add(argument);

        LastArguments = [.. startInfo.ArgumentList];

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Emit(e.Data);
        _process.ErrorDataReceived += (_, e) => Emit(e.Data);
        _process.Exited += (_, _) => Exited?.Invoke(this, _process?.ExitCode ?? -1);

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
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
