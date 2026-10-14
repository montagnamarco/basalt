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

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoMethods =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public async Task<IReadOnlyList<TestCase>> DiscoverAsync(
        string projectPath, CancellationToken ct = default)
    {
        // The whole discovery holds the build gate: the build, the listing and
        // the read of the assembly. A build that started in between would
        // rewrite the assembly under the read, and a listing of half-written
        // output would name tests that no longer exist.
        var (listed, methods) = await RunGatedAsync(
            gated => ListTestsAsync(projectPath, gated), ct).ConfigureAwait(false);

        return [.. listed
            .SelectMany(name => TestNameResolver.Qualify(name, methods))
            .Distinct(StringComparer.Ordinal)
            .Select(name => new TestCase(name, projectPath))];
    }

    /// <summary>
    /// Builds and lists the project's tests, and reads the class of each bare
    /// name from the built assembly. The caller holds the build gate.
    /// </summary>
    private async Task<(IReadOnlyList<string> Listed, IReadOnlyDictionary<string, IReadOnlyList<string>> Methods)> ListTestsAsync(
        string projectPath, CancellationToken ct)
    {
        IReadOnlyList<string> listed;
        string? assembly;

        if (TestProjectDetector.UsesTestingPlatform(projectPath))
        {
            // Under the platform "dotnet test --list-tests" runs the tests
            // rather than listing them, so the project is built and its
            // program asked for the list instead.
            var (buildExit, built) = await RunDotnetCoreAsync(
                projectPath, ["build", projectPath, "--nologo"], [], ct).ConfigureAwait(false);

            RequireSuccess(buildExit, projectPath);

            assembly = AssemblyPathIn(built);
            listed = assembly is null
                ? []
                : ParseProgramListing(await ListByProgramAsync(assembly, ct).ConfigureAwait(false));
        }
        else
        {
            var (listExit, output) = await RunDotnetCoreAsync(
                projectPath, ["test", projectPath, "--list-tests", "--nologo"], [], ct).ConfigureAwait(false);

            RequireSuccess(listExit, projectPath);

            assembly = AssemblyPathIn(output);
            listed = ParseDiscovered(output);
        }

        // The class is read from the assembly only when a name lacks it.
        var methods = assembly is not null && listed.Any(n => !TestNameResolver.HasClass(n))
            ? TestNameResolver.TestMethodsByName(assembly)
            : NoMethods;

        return (listed, methods);
    }

    /// <summary>
    /// The assembly a build or a listing printed as "name -> path.dll", or
    /// null when none was printed.
    /// </summary>
    /// <remarks>
    /// The last one is taken: MSBuild builds the references first, so the
    /// project's own output comes last.
    /// </remarks>
    internal static string? AssemblyPathIn(string output)
    {
        string? found = null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();

            var arrow = line.LastIndexOf(" -> ", StringComparison.Ordinal);
            if (arrow < 0) continue;

            var path = line[(arrow + 4)..].Trim();
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) found = path;
        }

        return found;
    }

    /// <summary>
    /// A project that does not build has no tests to list. Reporting that as an
    /// empty list would empty the window, so it is a failure, which the caller
    /// reports and which leaves the tests already shown in place.
    /// </summary>
    private static void RequireSuccess(int exitCode, string projectPath)
    {
        if (exitCode != 0)
            throw new InvalidOperationException(Localizer.Get(StringKeys.TestDiscoveryFailed, Path.GetFileName(projectPath)));
    }

    /// <summary>
    /// Asks a built test program for its list of tests, without running them.
    /// </summary>
    private async Task<string> ListByProgramAsync(string assembly, CancellationToken callerToken)
    {
        CancellationToken stop;
        lock (_running) stop = _stopRuns.Token;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, stop);

        var startInfo = DotnetProcess.CreateStartInfo(Path.GetDirectoryName(assembly));
        startInfo.ArgumentList.Add(assembly);
        startInfo.ArgumentList.Add("--list-tests");

        var (_, text) = await RunTrackedAsync(startInfo, linked.Token).ConfigureAwait(false);

        return text;
    }

    /// <summary>
    /// Reads the names out of what "--list-tests" printed.
    ///
    /// The heading above the list is translated, so it cannot be matched on.
    /// What identifies a test is the shape of the line: indented, not a build
    /// or status line, and not ending as a sentence does.
    ///
    /// The runners differ in what they print. xUnit lists "Class.Method(a: 1)"
    /// with its class; MSTest and NUnit list "Method" or "Method (1,2)" alone,
    /// and the platform indents by two spaces where VSTest indents by four.
    /// Arguments in brackets may hold spaces and dots, so a line is judged by
    /// the text before them.
    /// </summary>
    internal static IReadOnlyList<string> ParseDiscovered(string output)
    {
        var names = new List<string>();
        var inList = false;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Trim().Length == 0) continue;

            // An unindented line is a heading or a line of the run. The list is
            // the lines indented by four that follow a heading ending in ':', and
            // any other unindented line ends it. Build output has no such heading,
            // so its summary and diagnostics are never read as tests.
            if (!char.IsWhiteSpace(line[0]))
            {
                inList = IsHeading(line);
                continue;
            }

            if (!inList || !line.StartsWith("    ", StringComparison.Ordinal)) continue;

            var name = line.Trim();

            if (name.Contains(" -> ", StringComparison.Ordinal) || BuildDiagnostic.IsMatch(name)) continue;

            // Names may end in a full stop or a colon, hold ": " before their
            // arguments, and hold spaces, slashes and backslashes: a display name
            // such as "Opens when the file is empty." or a DataRow argument that
            // is a path is a test. Dropping them lost those tests.
            names.Add(name);
        }

        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Whether an unindented line is the heading a list follows. A translated
    /// heading ends in ':' or, in some languages, in the full-width '：'.
    /// </summary>
    internal static bool IsHeading(string line)
    {
        var end = line.TrimEnd();

        return end.EndsWith(':') || end.EndsWith('\uFF1A');
    }

    /// <summary>"file.vb(12,29): warning BC40000: ...": a compiler diagnostic, as the build prints it.</summary>
    private static readonly System.Text.RegularExpressions.Regex BuildDiagnostic = new(
        @"\(\d+(,\d+)?\): (warning|error|info) [A-Za-z]+\d*:");

    /// <summary>
    /// Reads the names a test program prints for "--list-tests" when it is run
    /// directly, as the platform does.
    /// </summary>
    /// <remarks>
    /// The platform indents its names by two spaces, and prints its summary
    /// with two spaces too, after the list. The list ends at the first line
    /// that is not indented after the names began, which is the summary's
    /// heading; so the summary's "duration: 53ms" is not read as a test.
    /// </remarks>
    internal static IReadOnlyList<string> ParseProgramListing(string output)
    {
        var names = new List<string>();
        var inList = false;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Trim().Length == 0) continue;

            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                if (inList) break;

                continue;
            }

            inList = true;
            names.Add(line.Trim());
        }

        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<TestResult>> RunAsync(
        string projectPath, string? filter = null, CancellationToken ct = default)
    {
        // The build is made once, here, and the runs after it do not build again.
        // A build that fails ends the run: its errors are in the output already.
        var (buildExit, _) = await RunDotnetAsync(projectPath, ["build", projectPath, "--nologo"], ct)
            .ConfigureAwait(false);

        if (buildExit != 0) return [];

        var results = await RunOnceAsync(projectPath, filter, ct).ConfigureAwait(false);

        // xUnit's adapter does not match a case by its display name, so a case
        // filter runs nothing there. The case then runs with its whole method, which
        // reports every case under its own name; the one picked is among them.
        if (results.Count == 0 && SelectsACase(filter))
            results = await RunOnceAsync(projectPath, WholeMethodsOf(filter!), ct).ConfigureAwait(false);

        return results;
    }

    /// <summary>The same filter with each case reduced to its method.</summary>
    internal static string WholeMethodsOf(string filter) =>
        string.Join("|", SplitFilter(filter, '|')
            .Select(part => UnescapedPrefix(part, '&'))
            .Distinct(StringComparer.Ordinal));

    private async Task<IReadOnlyList<TestResult>> RunOnceAsync(
        string projectPath, string? filter, CancellationToken ct)
    {
        // A directory of its own per run: two runs of the same project would
        // otherwise read each other's report.
        var reportDirectory = Path.Combine(
            Path.GetTempPath(), "basalt-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(reportDirectory);

        // The build was made by RunAsync; running the tests again must not build the project a second time.
        var arguments = new List<string> { "test", projectPath, "--nologo", "--no-build" };
        var programArguments = new List<string>();

        if (TestProjectDetector.UsesTestingPlatform(projectPath))
        {
            // The platform writes its own report, and takes its options after
            // "--". Its name filter has a different syntax, which is not
            // translated here, so the whole project runs: the results still
            // name every test, and the ones asked for are among them.
            programArguments.AddRange(["--report-trx", "--results-directory", reportDirectory]);
        }
        else
        {
            arguments.AddRange(["--logger", "trx;LogFileName=results.trx", "--results-directory", reportDirectory]);

            if (filter is { Length: > 0 })
            {
                arguments.Add("--filter");
                arguments.Add(filter);
            }
        }

        try
        {
            await RunDotnetAsync(projectPath, arguments, ct, programArguments).ConfigureAwait(false);

            // No report means the run never got as far as testing: a build
            // failure, usually, which the output panel has already shown.
            var results = new List<TestResult>();

            foreach (var report in Directory.GetFiles(reportDirectory, "*.trx", SearchOption.AllDirectories))
            {
                results.AddRange(ParseReport(await File.ReadAllTextAsync(report, ct).ConfigureAwait(false)));
            }

            return results;
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

        var classes = ClassesById(document, ns);

        var results = new List<TestResult>();

        foreach (var element in document.Descendants(ns + "UnitTestResult"))
        {
            var listedName = element.Attribute("testName")?.Value;
            if (listedName is null) continue;

            // A result names the test as the runner displays it: MSTest and
            // NUnit leave the class out. The report's definition of the test
            // has it, so the name is made the same as the one discovery gave.
            var name = QualifiedName(listedName, element.Attribute("testId")?.Value, classes);

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

    /// <summary>The class of each test in the report, by the test's id.</summary>
    private static Dictionary<string, string> ClassesById(XDocument document, XNamespace ns)
    {
        var classes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var test in document.Descendants(ns + "UnitTest"))
        {
            var id = test.Attribute("id")?.Value;
            var className = test.Element(ns + "TestMethod")?.Attribute("className")?.Value;

            if (id is { Length: > 0 } && className is { Length: > 0 }) classes.TryAdd(id, className);
        }

        return classes;
    }

    /// <summary>
    /// The name a result is matched by: the listed name with its class in
    /// front, when the name has none and the report says what class it is.
    /// </summary>
    internal static string QualifiedName(string listedName, string? testId, IReadOnlyDictionary<string, string> classes)
    {
        if (TestNameResolver.HasClass(listedName)) return listedName;

        return testId is not null && classes.TryGetValue(testId, out var className)
            ? className + "." + listedName
            : listedName;
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
    /// otherwise also run "AddsTwoNegatives". A case of a theory or data-row
    /// test is filtered by its method, because the runner knows the method by
    /// its name and the arguments are not part of it (see
    /// <see cref="MethodFullyQualifiedName"/>).
    /// </summary>
    public static string FilterFor(IEnumerable<string> fullyQualifiedNames) =>
        string.Join("|", fullyQualifiedNames
            .Distinct(StringComparer.Ordinal)
            .Select(CaseFilter)
            .Distinct(StringComparer.Ordinal));

    /// <summary>
    /// The filter that runs one test: its method for a test with no arguments,
    /// and for a case the method together with the case's display name.
    /// </summary>
    /// <remarks>
    /// The runner's fully-qualified name is the method's for every case, so
    /// the method alone runs all of them. The display name ("Adds(2,2,5)") is
    /// what tells one case from another; the runner reads it as "Name", and
    /// its parentheses are escaped because they are operators in a filter.
    /// Checked against NUnit with two cases: the filter selects one.
    /// </remarks>
    internal static string CaseFilter(string name)
    {
        var method = MethodFullyQualifiedName(name);

        if (method.Length == name.Length) return $"FullyQualifiedName={EscapeFilterValue(method)}";

        // The display name is the method's own name with the arguments, without the class.
        var displayName = method[(method.LastIndexOf('.') + 1)..] + name[method.Length..];

        return $"FullyQualifiedName={EscapeFilterValue(method)}&Name={EscapeFilterValue(displayName)}";
    }

    /// <summary>
    /// The parts of a filter, cut at each separator that is not escaped. A
    /// backslash makes the character after it literal, so "\|" stays in its part.
    /// </summary>
    internal static List<string> SplitFilter(string filter, char separator)
    {
        var parts = new List<string>();
        var start = 0;

        for (var i = 0; i < filter.Length; i++)
        {
            if (filter[i] == '\\') { i++; continue; }

            if (filter[i] == separator)
            {
                parts.Add(filter[start..i]);
                start = i + 1;
            }
        }

        parts.Add(filter[start..]);

        return parts;
    }

    /// <summary>The text of a filter part before its first unescaped <paramref name="stop"/>.</summary>
    private static string UnescapedPrefix(string part, char stop)
    {
        for (var i = 0; i < part.Length; i++)
        {
            if (part[i] == '\\') { i++; continue; }

            if (part[i] == stop) return part[..i];
        }

        return part;
    }

    /// <summary>Whether a filter selects at least one case, by its display name.</summary>
    internal static bool SelectsACase(string? filter) =>
        filter is not null
        && SplitFilter(filter, '|').Any(part => UnescapedPrefix(part, '&').Length < part.Length);

    /// <summary>Escapes the characters a filter reads as operators.</summary>
    private static string EscapeFilterValue(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            if (c is '\\' or '(' or ')' or '&' or '|' or '!' or '=' or '~') builder.Append('\\');
            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The name the runner filters on for a test: the method's name, without
    /// the arguments of one case.
    /// </summary>
    /// <remarks>
    /// Filtering on "Class.Adds(a: 1, b: 2)" matched no test: the runner's
    /// fully-qualified name of every case is "Class.Adds", so the case never
    /// ran and the user saw nothing happen. Filtering on the method runs its
    /// cases, and each case reports under its own name, so the one picked is
    /// shown with a result.
    /// </remarks>
    internal static string MethodFullyQualifiedName(string name) => TestNameResolver.MethodOf(name);

    /// <summary>The process the last discovery or run started, for the tests.</summary>
    internal ProcessStartInfo? LastStartInfo { get; private set; }

    private Task<(int ExitCode, string Output)> RunDotnetAsync(
        string projectPath, IReadOnlyList<string> arguments, CancellationToken callerToken,
        IReadOnlyList<string>? programArguments = null) =>
        RunGatedAsync(ct => RunDotnetCoreAsync(projectPath, arguments, programArguments ?? [], ct), callerToken);

    /// <summary>
    /// Takes the build gate, runs the work under it, and releases the gate
    /// when the work ends, whether it succeeded or not.
    /// </summary>
    private async Task<T> RunGatedAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken callerToken)
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
            return await work(ct).ConfigureAwait(false);
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
        string projectPath, IReadOnlyList<string> arguments, IReadOnlyList<string> programArguments,
        CancellationToken ct)
    {
        var startInfo = DotnetProcess.CreateStartInfo();
        LastStartInfo = startInfo;

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        // "dotnet test" restores and builds, so it needs the properties the
        // IDE's own build used, or it would rebuild the project the way
        // Windows does and fail off Windows. They go before the "--": after
        // it they would reach the test program instead of the SDK.
        foreach (var argument in await Compatibility.GetArgumentsAsync(projectPath, ct).ConfigureAwait(false))
            startInfo.ArgumentList.Add(argument);

        if (programArguments.Count > 0)
        {
            startInfo.ArgumentList.Add("--");
            foreach (var argument in programArguments) startInfo.ArgumentList.Add(argument);
        }

        var (exitCode, text) = await RunTrackedAsync(startInfo, ct).ConfigureAwait(false);

        if (text.Length > 0) OutputReceived?.Invoke(this, text);

        return (exitCode, text);
    }

    /// <summary>
    /// Runs a process to its end, keeping it in the set <see cref="StopAll"/>
    /// ends, and returns its exit code and everything it wrote.
    /// </summary>
    private async Task<(int ExitCode, string Output)> RunTrackedAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
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
