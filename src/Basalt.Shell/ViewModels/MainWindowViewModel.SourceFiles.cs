using System.ComponentModel;
using System.Diagnostics;
using Basalt.Core.Localization;
using Basalt.Workspace.Publishing;

namespace Basalt.Shell.ViewModels;

/// <summary>
/// Running a single source file that a language compiles, such as a
/// QuickBASIC program.
/// </summary>
/// <remarks>
/// The file is the whole program: there is no project for MSBuild to build.
/// The language's own backend turns it into an executable, which is then
/// started and whose output goes to the Output panel, as a project's does.
/// </remarks>
public sealed partial class MainWindowViewModel
{
    /// <summary>The native program the last Run started; its process is ended by Stop.</summary>
    private Process? _sourceFileProcess;

    /// <summary>
    /// The source file Run runs when no project is the startup project.
    ///
    /// The document in front wins, as it does for Debug > Start; otherwise the
    /// file the solution is. Null when neither is a file a language compiles.
    /// </summary>
    private string? SourceFileToRun()
    {
        if (ActiveDocument is { } document && CompilesSourceFile(document.FilePath))
            return document.FilePath;

        return SolutionPath is { } path && CompilesSourceFile(path) ? path : null;
    }

    private bool CompilesSourceFile(string path) => RoutedProviderFor(path)?.Compiler is not null;

    /// <summary>
    /// Compiles a source file with its language's backend, then runs the
    /// program it produced and reports its output.
    /// </summary>
    internal async Task RunSourceFileAsync(string path)
    {
        if (IsRunning)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusAlreadyRunning);
            return;
        }

        var backend = RoutedProviderFor(path)?.Compiler;
        if (backend is null)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return;
        }

        // Saved first: the compiler reads the file from disk, as a build reads
        // the project's files after SaveAllAsync.
        if (OpenDocuments.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.Ordinal)) is { } open)
            await open.SaveAsync().ConfigureAwait(true);

        BeginBusy();
        var operation = Operations.Start(Localizer.Get(StringKeys.StatusBuilding));

        // The backend is a single object for the session, so the listener is
        // attached for this compile only; a second one would print every line twice.
        EventHandler<string> echo = (_, line) => AppendOutput(line);

        try
        {
            BuildOutput.Clear();
            Diagnostics.Clear();
            StatusMessage = Localizer.Get(StringKeys.StatusBuilding);

            backend.OutputReceived += echo;

            var target = new Basalt.Extensibility.CompilationTarget(PublishTarget.Current().Rid)
            {
                OutputPath = SourceFileExecutablePath(path)
            };

            var result = await backend.CompileAsync(path, target, operation.Token).ConfigureAwait(true);

            foreach (var diagnostic in result.Diagnostics) Diagnostics.Add(ToIdeDiagnostic(diagnostic, path));

            var errors = result.Diagnostics.Count(d => d.Severity == Basalt.Extensibility.DiagnosticSeverity.Error);
            var warnings = result.Diagnostics.Count(d => d.Severity == Basalt.Extensibility.DiagnosticSeverity.Warning);

            if (!result.Succeeded || result.OutputPath is null)
            {
                StatusMessage = Localizer.Get(StringKeys.StatusBuildFailed, errors, warnings);
                return;
            }

            StartSourceFileProcess(result.OutputPath, Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusBuildStopped);
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusBuildInterrupted, ex.Message);
            AppendOutput($"[error] {ex.Message}");
        }
        finally
        {
            backend.OutputReceived -= echo;
            Operations.Finish(operation);
            EndBusy();
        }
    }

    /// <summary>
    /// Where the program is written: in bin beside the source, where a
    /// project's own output goes, so the folder the user works in stays clean.
    /// </summary>
    private static string SourceFileExecutablePath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var name = Path.GetFileNameWithoutExtension(sourcePath) + (OperatingSystem.IsWindows() ? ".exe" : "");

        return Path.Combine(directory, "bin", name);
    }

    private void StartSourceFileProcess(string executable, string workingDirectory)
    {
        // Counted like a project run, so a late exit from an earlier run, or a
        // Stop, is recognised by OnRunExited rather than reported as this one.
        var generation = ++_runGeneration;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) AppendOutput(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) AppendOutput(line); };

        // WaitForExit with no timeout also waits for the redirected output to
        // end, so the exit is reported after the program's last line.
        process.Exited += (_, _) =>
        {
            process.WaitForExit();
            OnRunExited(process.ExitCode, generation);
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            StatusMessage = Localizer.Get(StringKeys.StatusStartFailed, ex.Message);
            AppendOutput(StatusMessage);
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _sourceFileProcess?.Dispose();
        _sourceFileProcess = process;

        IsRunning = true;
        StatusMessage = Localizer.Get(StringKeys.StatusRunning);
    }

    /// <summary>The id of the native program the last Run started. For tests that check it is gone.</summary>
    internal int? SourceFileProcessIdForTests => _sourceFileProcess?.Id;

    /// <summary>Ends the native program a Run started, if it is still running.</summary>
    internal void StopSourceFileRun()
    {
        if (_sourceFileProcess is not { HasExited: false } process) return;

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Exited between the check and the call, or the OS refused the kill:
            // the same two cases DotnetTestService.KillTree ignores.
        }
    }
}
