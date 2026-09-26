using System.Diagnostics;

namespace Basalt.Workspace;

/// <summary>Runs the user's application and reports its output.</summary>
public sealed class ProcessRunService : IDisposable
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;

    /// <summary>Starts the project with "dotnet run" in the project directory.</summary>
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
