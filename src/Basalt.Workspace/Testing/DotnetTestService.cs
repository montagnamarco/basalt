using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Workspace.Testing;

/// <summary>
/// Finds and runs tests through the dotnet CLI.
///
/// Results are read from the TRX report rather than from the console output:
/// the console text is translated, so parsing it would work in English and
/// fail in Italian, and it carries no stack trace to jump to. The report is
/// the same XML whatever the language.
/// </summary>
public sealed class DotnetTestService : ITestService
{
    /// <summary>The session that retargets Windows desktop projects, shared with the build.</summary>
    public WindowsDesktopCompatibilitySession Compatibility { get; set; } = new();

    private readonly BuildGate _gate;

    /// <summary>
    /// Takes the gate every build of the project passes through, because
    /// "dotnet test" builds it: two dotnet builds over one obj folder corrupt
    /// each other. Required, so a service cannot be left ungated by forgetting
    /// to wire it.
    /// </summary>
    public DotnetTestService(BuildGate gate) => _gate = gate;

    /// <summary>The gate has to be waited for: carries who holds it.</summary>
    public event EventHandler<string>? WaitingForGate;

    /// <summary>The wait is over, whether the gate was won or the run stopped.</summary>
    public event EventHandler? WaitEnded;

    public event EventHandler<string>? OutputReceived;

    public async Task<IReadOnlyList<TestCase>> DiscoverAsync(
        string projectPath, CancellationToken ct = default)
    {
        var (_, output) = await RunDotnetAsync(
            projectPath, ["test", projectPath, "--list-tests", "--nologo"], ct).ConfigureAwait(false);

        return [.. ParseDiscovered(output).Select(name => new TestCase(name, projectPath))];
    }

    /// <summary>
    /// Reads the names out of what "--list-tests" printed.
    ///
    /// The heading above the list is translated, so it cannot be matched on.
    /// What identifies a test is the shape of the line: indented, with a dot
    /// separating class from method, and no path separators.
    ///
    /// A theory is listed once per case, as
    /// "Class.Method(a: 1, b: 2)" — so spaces inside the brackets are normal
    /// and a line cannot be rejected for containing one. Rejecting them was a
    /// real bug: every parameterised test vanished from the window.
    /// </summary>
    internal static IReadOnlyList<string> ParseDiscovered(string output)
    {
        var names = new List<string>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (!line.StartsWith("    ", StringComparison.Ordinal)) continue;

            var name = line.Trim();

            if (name.Length == 0 || !name.Contains('.')) continue;

            // A path printed by the build is indented and has a dot too.
            if (name.Contains('/') || name.Contains('\\')) continue;

            // Outside the arguments a space means this is prose, not a name.
            var bracket = name.IndexOf('(');
            var beforeArguments = bracket < 0 ? name : name[..bracket];

            if (beforeArguments.Contains(' ')) continue;

            names.Add(name);
        }

        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<TestResult>> RunAsync(
        string projectPath, string? filter = null, CancellationToken ct = default)
    {
        // A directory of its own per run: two runs of the same project would
        // otherwise read each other's report.
        var reportDirectory = Path.Combine(
            Path.GetTempPath(), "basalt-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(reportDirectory);

        var arguments = new List<string>
        {
            "test", projectPath, "--nologo",
            "--logger", "trx;LogFileName=results.trx",
            "--results-directory", reportDirectory
        };

        if (filter is { Length: > 0 })
        {
            arguments.Add("--filter");
            arguments.Add(filter);
        }

        try
        {
            await RunDotnetAsync(projectPath, arguments, ct).ConfigureAwait(false);

            var report = Path.Combine(reportDirectory, "results.trx");

            // No report means the run never got as far as testing: a build
            // failure, usually, which the output panel has already shown.
            return File.Exists(report)
                ? ParseReport(await File.ReadAllTextAsync(report, ct).ConfigureAwait(false))
                : [];
        }
        finally
        {
            try { Directory.Delete(reportDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Reads the results out of a TRX report.</summary>
    internal static IReadOnlyList<TestResult> ParseReport(string trx)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(trx);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        var results = new List<TestResult>();

        foreach (var element in document.Descendants(ns + "UnitTestResult"))
        {
            var name = element.Attribute("testName")?.Value;
            if (name is null) continue;

            var message = element.Descendants(ns + "Message").FirstOrDefault()?.Value;
            var stack = element.Descendants(ns + "StackTrace").FirstOrDefault()?.Value;

            var (file, line) = LocationOf(stack);

            results.Add(new TestResult(
                name,
                ReadOutcome(element.Attribute("outcome")?.Value),
                ReadDuration(element.Attribute("duration")?.Value))
            {
                Message = message,
                StackTrace = stack,
                FilePath = file,
                Line = line
            });
        }

        return results;
    }

    private static TestOutcome ReadOutcome(string? outcome) => outcome switch
    {
        "Passed" => TestOutcome.Passed,
        "Failed" => TestOutcome.Failed,
        "NotExecuted" or "Skipped" => TestOutcome.Skipped,
        _ => TestOutcome.NotRun
    };

    private static TimeSpan ReadDuration(string? duration) =>
        TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var value)
            ? value
            : TimeSpan.Zero;

    /// <summary>
    /// Where a failure happened, read from the stack trace.
    ///
    /// The first frame carrying a file is the test's own: the frames above it
    /// belong to the assertion library, which is not where the user wants to
    /// be taken.
    /// </summary>
    internal static (string? FilePath, int Line) LocationOf(string? stackTrace)
    {
        if (stackTrace is not { Length: > 0 }) return (null, 0);

        foreach (var raw in stackTrace.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            // " at Namespace.Class.Method() in /path/File.vb:line 42"
            var marker = line.LastIndexOf(" in ", StringComparison.Ordinal);
            if (marker < 0) continue;

            var location = line[(marker + 4)..].Trim();

            var separator = location.LastIndexOf(':');
            if (separator <= 0) continue;

            var path = location[..separator];
            var number = location[(separator + 1)..].Trim();

            // "line 42" in English, "riga 42" in Italian: the digits are what
            // matter, and they are at the end either way.
            var digits = new string([.. number.Where(char.IsDigit)]);

            if (digits.Length == 0 || !int.TryParse(digits, out var lineNumber)) continue;

            return (path, lineNumber);
        }

        return (null, 0);
    }

    /// <summary>
    /// Builds a filter that runs exactly the named tests.
    ///
    /// Names are matched in full rather than by prefix: "AddsTwo" would
    /// otherwise also run "AddsTwoNegatives".
    /// </summary>
    public static string FilterFor(IEnumerable<string> fullyQualifiedNames) =>
        string.Join("|", fullyQualifiedNames.Select(name => $"FullyQualifiedName={name}"));

    /// <summary>The process the last discovery or run started, for the tests.</summary>
    internal ProcessStartInfo? LastStartInfo { get; private set; }

    private async Task<(int ExitCode, string Output)> RunDotnetAsync(
        string projectPath, IReadOnlyList<string> arguments, CancellationToken callerToken)
    {
        // Read now, so that a stop requested after this run began reaches it,
        // and one requested later does not.
        CancellationToken stop;
        lock (_running) stop = _stopRuns.Token;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, stop);
        var ct = linked.Token;

        var waited = false;

        IDisposable held;

        try
        {
            held = await _gate
                .AcquireAsync(
                    Localizer.Get(StringKeys.GateTests),
                    holder =>
                    {
                        waited = true;
                        WaitingForGate?.Invoke(this, holder);
                    },
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            if (waited) WaitEnded?.Invoke(this, EventArgs.Empty);
        }

        using (held)
        {
            return await RunDotnetCoreAsync(projectPath, arguments, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The dotnet processes started and not yet finished, for the tests that
    /// check nothing of a run is left behind.
    /// </summary>
    internal IReadOnlyList<int> RunningProcessIds
    {
        get { lock (_running) return [.. _running.Select(p => p.Id)]; }
    }

    /// <summary>
    /// Ends every run in progress, and the processes they started: the
    /// dotnet test and the test host that dotnet test starts in turn.
    ///
    /// Called when the window or the solution goes away. Nothing else would
    /// end a run, so without this the run goes on in the background after
    /// the window that asked for it is gone. Runs started after this call
    /// are not affected.
    /// </summary>
    public void StopAll()
    {
        CancellationTokenSource stopped;

        lock (_running)
        {
            stopped = _stopRuns;
            _stopRuns = new CancellationTokenSource();

            // Killed here, not only by the cancelled waits: those resume on
            // the thread pool later, and the caller expects the processes to
            // be gone when StopAll returns.
            foreach (var process in _running) KillTree(process);
        }

        stopped.Cancel();

        // Only runs that read the old token before the swap hold it, and they
        // were cancelled above; the source itself has no further use.
        stopped.Dispose();
    }

    private readonly HashSet<Process> _running = [];

    /// <summary>
    /// The token every run started now is bound to. <see cref="StopAll"/>
    /// replaces it, so only the runs already in progress are cancelled.
    /// </summary>
    private CancellationTokenSource _stopRuns = new();

    private async Task<(int ExitCode, string Output)> RunDotnetCoreAsync(
        string projectPath, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = DotnetProcess.CreateStartInfo();
        LastStartInfo = startInfo;

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        // "dotnet test" restores and builds, so it needs the properties the
        // IDE's own build used, or it would rebuild the project the way
        // Windows does and fail off Windows.
        foreach (var argument in await Compatibility.GetArgumentsAsync(projectPath, ct).ConfigureAwait(false))
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The dotnet CLI could not be started.");

        lock (_running) _running.Add(process);

        try
        {
            // Not bound to the token: a cancelled read would fault while the
            // process it reads from is still being killed below.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // "Cancelled" by the caller, a stop or a closed window. Waiting
                // alone only abandons the wait: the dotnet test and the test
                // host it started would keep running.
                KillTree(process);
                await DrainAfterKillAsync(output, error).ConfigureAwait(false);
                throw;
            }

            var text = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);

            if (text.Length > 0) OutputReceived?.Invoke(this, text);

            return (process.ExitCode, text);
        }
        finally
        {
            lock (_running) _running.Remove(process);
        }
    }

    /// <summary>
    /// Ends the process and everything it started, while it can still be
    /// found: a child whose parent has exited is no longer attached to it.
    /// </summary>
    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Exited between the check and the kill.
        }
    }

    /// <summary>
    /// Waits, for a bounded time, for the output pipes to close once the tree
    /// is killed. A grandchild that escaped the kill can keep them open, and
    /// cancelling must not hang the caller on it.
    /// </summary>
    private static async Task DrainAfterKillAsync(Task output, Task error)
    {
        try
        {
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
    }
}
