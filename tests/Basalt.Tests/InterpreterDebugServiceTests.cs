using Basalt.Extensibility.Interpretation;
using Basalt.Core.Services;
using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// An interpreter behind the debug service the panels already use.
///
/// The point of these is that nothing above has to know an interpreter is
/// running: what the panels ask for comes back in the shapes they expect.
/// </summary>
public sealed class InterpreterDebugServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-interp-debug", Guid.NewGuid().ToString("N"));

    public InterpreterDebugServiceTests() => Directory.CreateDirectory(_root);

    private string WriteProgram(string text = "one\ntwo\nthree\n")
    {
        var path = Path.Combine(_root, "Program.bas");
        File.WriteAllText(path, text);
        return path;
    }

    private static FakeInterpreter ThreeLines() =>
        new((1, 0), (2, 0), (3, 0));

    [Fact]
    public async Task RunsTheProgramItIsGiven()
    {
        var interpreter = ThreeLines();
        using var service = new InterpreterDebugService(interpreter);

        await service.LaunchAsync(WriteProgram());

        Assert.Equal([1, 2, 3], interpreter.Ran);
    }

    [Fact]
    public async Task StopsAtABreakpointTheIdeSet()
    {
        var interpreter = ThreeLines();
        using var service = new InterpreterDebugService(interpreter);

        var path = WriteProgram();

        await service.SetBreakpointsAsync(path, [new Breakpoint(path, 2)]);
        await service.LaunchAsync(path);

        Assert.True(service.IsPaused);
        Assert.Equal([1], interpreter.Ran);
    }

    [Fact]
    public async Task RaisesPausedWithTheLineItStoppedOn()
    {
        // What moves the highlight in the editor.
        var interpreter = ThreeLines();
        using var service = new InterpreterDebugService(interpreter);

        var path = WriteProgram();

        Core.Services.StackFrame? paused = null;
        service.Paused += (_, frame) => paused = frame;

        await service.SetBreakpointsAsync(path, [new Breakpoint(path, 2)]);
        await service.LaunchAsync(path);

        Assert.Equal(2, paused?.Line);
        Assert.Equal(path, paused?.FilePath);
    }

    [Fact]
    public async Task CarriesOnWhenTheIdeAsksIt()
    {
        var interpreter = ThreeLines();
        using var service = new InterpreterDebugService(interpreter);

        var path = WriteProgram();

        await service.SetBreakpointsAsync(path, [new Breakpoint(path, 2)]);
        await service.LaunchAsync(path);

        await service.ContinueAsync();

        Assert.Equal([1, 2, 3], interpreter.Ran);
    }

    [Fact]
    public async Task GivesTheCallStackInTheShapeThePanelExpects()
    {
        var interpreter = ThreeLines();
        using var service = new InterpreterDebugService(interpreter);

        var path = WriteProgram();

        await service.SetBreakpointsAsync(path, [new Breakpoint(path, 2)]);
        await service.LaunchAsync(path);

        var stack = await service.GetCallStackAsync();

        Assert.NotEmpty(stack);
        Assert.Equal(path, stack[0].FilePath);
    }

    [Fact]
    public async Task WorksOutAnExpressionInTheLanguageBeingDebugged()
    {
        // No translation to C#: the interpreter reads the same language the
        // user is looking at.
        var interpreter = ThreeLines();
        interpreter.Evaluator = expression => expression == "i * 2" ? "84" : null;

        using var service = new InterpreterDebugService(interpreter);

        await service.LaunchAsync(WriteProgram());

        Assert.Equal("84", await service.EvaluateAsync("i * 2", 0));
    }

    [Fact]
    public async Task ReportsTheProblemsThatStopAProgramRunning()
    {
        var interpreter = new RefusingInterpreter();
        using var service = new InterpreterDebugService(interpreter);

        await service.LaunchAsync(WriteProgram());

        Assert.NotEmpty(service.Errors);

        // Nothing ran, because nothing could.
        Assert.Equal(RunState.Ready, interpreter.State);
    }

    [Fact]
    public async Task SaysWhyAttachingIsNotPossible()
    {
        using var service = new InterpreterDebugService(ThreeLines());

        var error = await Assert.ThrowsAsync<NotSupportedException>(
            () => service.AttachAsync(1234));

        Assert.Contains("inside the IDE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PassesOnWhatTheProgramPrinted()
    {
        var interpreter = ThreeLines();
        using var service = new InterpreterDebugService(interpreter);

        var written = new List<string>();
        service.OutputReceived += (_, text) => written.Add(text);

        await service.LaunchAsync(WriteProgram());

        interpreter.Say("hello");

        Assert.Equal(["hello"], written);
    }

    [Fact]
    public async Task StopsAProgramWhenTheIdeAsksIt()
    {
        var interpreter = new EndlessInterpreter();
        using var service = new InterpreterDebugService(interpreter);

        var launch = service.LaunchAsync(WriteProgram());

        while (interpreter.Statements < 50) await Task.Yield();

        await service.StopAsync();
        await launch;

        Assert.Equal(RunState.Finished, interpreter.State);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

/// <summary>An interpreter that refuses to read its program.</summary>
internal sealed class RefusingInterpreter : InterpreterBase
{
    public override int CurrentLine => 0;
    protected override int CallDepth => 0;

    protected override bool ExecuteNextStatement(CancellationToken ct) => false;

    public override IReadOnlyList<InterpreterError> Load(string source, string? filePath = null) =>
        [new InterpreterError("Something is wrong with this program.", 1)];

    public override IReadOnlyList<InterpreterFrame> GetCallStack() => [];
    public override IReadOnlyList<InterpreterVariable> GetVariables(int frameIndex) => [];
    public override string? Evaluate(string expression, int frameIndex) => null;
    public override bool SetVariable(string name, string value, int frameIndex) => false;
}
