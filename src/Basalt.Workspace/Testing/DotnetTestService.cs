using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
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
    public event EventHandler<string>? OutputReceived;

    public async Task<IReadOnlyList<TestCase>> DiscoverAsync(
        string projectPath, CancellationToken ct = default)
    {
        var (_, output) = await RunDotnetAsync(
            ["test", projectPath, "--list-tests", "--nologo"], ct).ConfigureAwait(false);

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
            await RunDotnetAsync(arguments, ct).ConfigureAwait(false);

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

    private async Task<(int ExitCode, string Output)> RunDotnetAsync(
        IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The dotnet CLI could not be started.");

        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var text = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);

        if (text.Length > 0) OutputReceived?.Invoke(this, text);

        return (process.ExitCode, text);
    }
}
