using Basalt.Core.Services;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Stepping through a QuickBASIC program in the IDE.
///
/// Through DebugSession, which is what the toolbar and the panels talk to, so
/// what these check is what a user gets rather than what the interpreter can
/// do in isolation.
/// </summary>
public sealed class InterpretedDebuggingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-interp-ide", Guid.NewGuid().ToString("N"));

    public InterpretedDebuggingTests() => Directory.CreateDirectory(_root);

    /// <summary>A program on disk, as one written in the IDE would be.</summary>
    private async Task<string> WriteAsync(string source, string name = "Program.bas")
    {
        var path = Path.Combine(_root, name);

        await File.WriteAllTextAsync(path, source);

        return path;
    }

    private const string Counting = """
        x = 1
        x = 2
        x = 3
        PRINT x
        """;

    [Fact]
    public async Task RunsAQuickBasicProgramWithoutADotNetDebugger()
    {
        // netcoredbg speaks .NET, so a .bas file has to go elsewhere; this is
        // the whole reason the interpreter exists.
        using var session = new DebugSession();

        var printed = new List<string>();
        session.OutputReceived += (_, text) => printed.Add(text);

        await session.StartAsync(await WriteAsync(Counting));

        Assert.Contains(printed, t => t.Contains("3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopsAtABreakpointSetInTheEditor()
    {
        using var session = new DebugSession();

        var path = await WriteAsync(Counting);

        await session.ToggleBreakpointAsync(path, 3);
        await session.StartAsync(path);

        Assert.True(session.IsPaused);
        Assert.Equal(3, session.CurrentFrame?.Line);
    }

    [Fact]
    public async Task ShowsTheLineItStoppedOnSoTheEditorCanHighlightIt()
    {
        using var session = new DebugSession();

        var path = await WriteAsync(Counting);

        StackFrame? paused = null;
        session.Paused += (_, frame) => paused = frame;

        await session.ToggleBreakpointAsync(path, 2);
        await session.StartAsync(path);

        Assert.Equal(2, paused?.Line);
        Assert.Equal(path, paused?.FilePath);
    }

    [Fact]
    public async Task ShowsTheVariablesAsTheyAreAtTheStop()
    {
        using var session = new DebugSession();

        var path = await WriteAsync(Counting);

        await session.ToggleBreakpointAsync(path, 3);
        await session.StartAsync(path);

        var locals = await session.GetLocalsAsync();

        var x = locals.SingleOrDefault(v => v.Name.Equals("x", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(x);

        // Lines 1 and 2 have run, so x is 2 rather than 3.
        Assert.Equal("2", x.Value);
    }

    [Fact]
    public async Task ShowsTheCallStack()
    {
        using var session = new DebugSession();

        var path = await WriteAsync("""
            CALL Inner
            SUB Inner
                x = 1
            END SUB
            """);

        await session.ToggleBreakpointAsync(path, 3);
        await session.StartAsync(path);

        var stack = await session.GetCallStackAsync();

        Assert.Equal(2, stack.Count);
        Assert.Equal("Inner", stack[0].Method);
        Assert.Equal("main", stack[1].Method);
    }

    [Fact]
    public async Task StepsOneLineAtATime()
    {
        using var session = new DebugSession();

        var path = await WriteAsync(Counting);

        await session.ToggleBreakpointAsync(path, 1);
        await session.StartAsync(path);

        Assert.Equal(1, session.CurrentFrame?.Line);

        await session.StepOverAsync();
        Assert.Equal(2, session.CurrentFrame?.Line);

        await session.StepOverAsync();
        Assert.Equal(3, session.CurrentFrame?.Line);
    }

    [Fact]
    public async Task StepsIntoAProcedure()
    {
        using var session = new DebugSession();

        var path = await WriteAsync("""
            CALL Inner
            PRINT "after"
            SUB Inner
                x = 1
            END SUB
            """);

        await session.ToggleBreakpointAsync(path, 1);
        await session.StartAsync(path);

        await session.StepIntoAsync();

        Assert.Equal(4, session.CurrentFrame?.Line);
    }

    [Fact]
    public async Task WorksOutAWatchExpressionInBasic()
    {
        // In BASIC rather than translated to C#: the user is looking at BASIC.
        using var session = new DebugSession();

        var path = await WriteAsync(Counting);

        await session.ToggleBreakpointAsync(path, 4);
        await session.StartAsync(path);

        Assert.Equal("3", await session.EvaluateAsync("x"));
        Assert.Equal("6", await session.EvaluateAsync("x * 2"));
        Assert.Equal("-1", await session.EvaluateAsync("x = 3"));
    }

    [Fact]
    public async Task StopsOnlyWhenAConditionInBasicHolds()
    {
        using var session = new DebugSession();

        var path = await WriteAsync("""
            FOR i = 1 TO 5
                PRINT i
            NEXT i
            """);

        await session.ToggleBreakpointAsync(path, 2);
        await session.SetBreakpointConditionAsync(path, 2, "i = 3");

        await session.StartAsync(path);

        Assert.True(session.IsPaused);
        Assert.Equal("3", await session.EvaluateAsync("i"));
    }

    [Fact]
    public async Task ChangesAVariableWhileTheProgramIsStopped()
    {
        using var session = new DebugSession();

        var path = await WriteAsync(Counting);

        var printed = new List<string>();
        session.OutputReceived += (_, text) => printed.Add(text);

        await session.ToggleBreakpointAsync(path, 4);
        await session.StartAsync(path);

        Assert.True(session.SetVariable("x", "42"));

        await session.ContinueAsync();

        Assert.Contains(printed, t => t.Contains("42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaysWhatIsWrongWithAProgramThatDoesNotRead()
    {
        using var session = new DebugSession();

        var failures = new List<string>();
        session.Failed += (_, message) => failures.Add(message);

        await session.StartAsync(await WriteAsync("FOR i = 1 TO"));

        Assert.NotEmpty(failures);
    }

    [Fact]
    public async Task AsksTheIdeWhenTheProgramWantsInput()
    {
        // An interpreted program has no console of its own, so INPUT has to
        // come through the IDE or it would silently read nothing.
        using var session = new DebugSession();

        var printed = new List<string>();
        session.OutputReceived += (_, text) => printed.Add(text);

        var prompts = new List<string>();

        session.InputRequested += (_, request) =>
        {
            prompts.Add(request.Prompt);
            request.Response = "7";
        };

        await session.StartAsync(await WriteAsync("INPUT n\nPRINT n * 2"));

        Assert.NotEmpty(prompts);
        Assert.Contains(printed, t => t.Contains("14", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopsTheProgramWhenTheIdeAsks()
    {
        using var session = new DebugSession();

        var path = await WriteAsync("""
            DO
                x = x + 1
            LOOP
            """);

        var run = session.StartAsync(path);

        await Task.Delay(50);

        await session.StopAsync();
        await run;

        Assert.False(session.IsPaused);
    }

    [Fact]
    public async Task LeavesADotNetAssemblyToTheDotNetDebugger()
    {
        // The choice is by what is being debugged: a .dll must not be handed
        // to the QuickBASIC interpreter, which would fail to read it.
        using var session = new DebugSession();

        var failures = new List<string>();
        session.Failed += (_, message) => failures.Add(message);

        await session.StartAsync(Path.Combine(_root, "Nothing.dll"));

        // Either netcoredbg is missing or the file is not there; either way
        // the interpreter was not asked to read an assembly.
        Assert.All(failures, message =>
            Assert.DoesNotContain("QB0", message, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
