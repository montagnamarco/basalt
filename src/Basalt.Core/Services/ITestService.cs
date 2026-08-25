namespace Basalt.Core.Services;

/// <summary>How a test ended.</summary>
public enum TestOutcome { NotRun, Running, Passed, Failed, Skipped }

/// <summary>
/// A test the IDE knows about.
///
/// The fully-qualified name is the identity: it is what the runner reports and
/// what a filter matches, so the tree is built from it rather than from a
/// separate structure that could disagree.
/// </summary>
public sealed record TestCase(string FullyQualifiedName, string ProjectPath)
{
    /// <summary>The part after the last dot: the method's own name.</summary>
    public string DisplayName
    {
        get
        {
            var dot = FullyQualifiedName.LastIndexOf('.');

            return dot < 0 ? FullyQualifiedName : FullyQualifiedName[(dot + 1)..];
        }
    }

    /// <summary>Everything before the method: the class it is declared in.</summary>
    public string ClassName
    {
        get
        {
            var dot = FullyQualifiedName.LastIndexOf('.');

            return dot < 0 ? "" : FullyQualifiedName[..dot];
        }
    }
}

/// <summary>What happened when a test ran.</summary>
public sealed record TestResult(
    string FullyQualifiedName,
    TestOutcome Outcome,
    TimeSpan Duration)
{
    /// <summary>Why it failed, when it did.</summary>
    public string? Message { get; init; }

    public string? StackTrace { get; init; }

    /// <summary>Where the failure happened, read from the stack trace.</summary>
    public string? FilePath { get; init; }

    public int Line { get; init; }
}

/// <summary>Finding and running the tests of a solution.</summary>
public interface ITestService
{
    /// <summary>The tests a project declares.</summary>
    Task<IReadOnlyList<TestCase>> DiscoverAsync(
        string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Runs tests, optionally narrowed by a filter.
    ///
    /// A null filter runs everything the project has; a filter is the same
    /// expression "dotnet test --filter" takes.
    /// </summary>
    Task<IReadOnlyList<TestResult>> RunAsync(
        string projectPath, string? filter = null, CancellationToken ct = default);

    /// <summary>Output written while running, for the output panel.</summary>
    event EventHandler<string>? OutputReceived;
}
