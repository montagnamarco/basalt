using System.Text.Json.Nodes;
using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// How the debug service asks netcoredbg for a value: Visual Basic tried in its C# readings, a C# frame left
/// alone, no reading sent after one that may have run code, and the frames of an exception stop matched to
/// their own locals. The fake adapter's frames are Main in Module1.vb and Caller in Helpers.cs.
/// </summary>
public sealed class NetCoreDebugEvaluationTests
{
    private static async Task<(FakeDapConnection Dap, NetCoreDebugService Service)> StoppedAsync(
        string reason = "breakpoint", bool framesHaveModule = false, Action<FakeDapConnection>? arrange = null)
    {
        var dap = new FakeDapConnection { FramesHaveModule = framesHaveModule };
        arrange?.Invoke(dap);
        var service = new NetCoreDebugService(() => dap);
        var paused = new TaskCompletionSource();
        service.Paused += (_, _) => paused.TrySetResult();

        await service.LaunchAsync("/app/App.dll");
        dap.Raise("stopped", new JsonObject { ["threadId"] = 1, ["reason"] = reason });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        return (dap, service);
    }

    private static List<string> Evaluated(FakeDapConnection dap) =>
        [.. dap.Requests.Where(r => r.Command == "evaluate").Select(r => r.Arguments!["expression"]!.GetValue<string>())];

    [Fact]
    public async Task AVisualBasicWatchIsTriedInItsNextReadingWhenTheFirstIsRefused()
    {
        var (dap, service) = await StoppedAsync();

        // "numbers(1)" is a function here, not an array: only the call is accepted.
        dap.EvaluateAnswer = arguments => arguments!["expression"]!.GetValue<string>() == "numbers(1)"
            ? FakeDapConnection.Answer(true, new JsonObject { ["result"] = "42" })
            : FakeDapConnection.Answer(false, new JsonObject(), "error: 0x80070057");

        Assert.Equal("42", await service.EvaluateAsync("numbers(1)", frameIndex: 0));
        Assert.Equal(["numbers[1]", "numbers(1)"], Evaluated(dap));
    }

    [Fact]
    public async Task TheImmediateWindowSendsNoReadingAfterOneThatCallsSomethingWasRefused()
    {
        // "!(calc.Compute(n))" runs Compute before ! is refused on an Integer; in the Immediate window "~" would
        // run it a second time. The readings that run nothing go first, both negations of the index.
        var (dap, service) = await StoppedAsync();
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(false, new JsonObject(), "error: Operator '!' cannot be applied");

        var repl = await service.EvaluateReplAsync("Not calc.Compute(n)", isExpression: true, frameIndex: 0);

        Assert.False(repl.Succeeded);
        Assert.Equal(["!(calc.Compute[n])", "~(calc.Compute[n])", "!(calc.Compute(n))"], Evaluated(dap));
    }

    [Fact]
    public async Task TheImmediateWindowFindsAChainedCallFirstTime()
    {
        // Before, the index reading of Substring went first, was refused, and as it holds a call (ToUpper) the
        // Immediate window stopped there; the unchanged text, sent before this work, had answered.
        var (dap, service) = await StoppedAsync();
        dap.EvaluateAnswer = arguments => arguments!["expression"]!.GetValue<string>() == "(Name.Substring(0, 3)).ToUpper()"
            ? FakeDapConnection.Answer(true, new JsonObject { ["result"] = "\"ROS\"" })
            : FakeDapConnection.Answer(false, new JsonObject(), "error: 0x80070057");

        var answer = await service.EvaluateReplAsync("Name.Substring(0, 3).ToUpper()", isExpression: true, frameIndex: 0);

        Assert.True(answer.Succeeded);
        Assert.Equal("\"ROS\"", answer.Text);
    }

    [Fact]
    public async Task TheImmediateWindowAtAnExceptionStopAsksTheFrameThatHoldsItsLocals()
    {
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Fail(Int32 n)", "/app/Program.vb", 15), ("Program.Main()", "/app/Program.vb", 5)];
            dap.LocalsById = new() { [100] = ["$exception"], [101] = ["$exception", "n"], [102] = ["$exception", "marker"] };
        });

        await service.EvaluateReplAsync("n", isExpression: true, frameIndex: 0);

        Assert.Equal(101, dap.ArgumentsOf("evaluate")!["frameId"]!.GetValue<int>());
    }

    [Fact]
    public async Task AnExceptionStopWithMoreHiddenFramesThanAreLookedThroughShowsNoLocals()
    {
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Fail(Int32 n)", "/app/Program.vb", 15)];
            dap.LocalsById = Enumerable.Range(100, 40).ToDictionary(id => id, _ => new[] { "$exception", "other" });
        });

        Assert.Empty(await service.GetLocalsAsync(frameIndex: 0));
    }

    [Fact]
    public async Task AFrameWhoseIdCannotBeKnownIsNeitherEvaluatedNorAskedInTheImmediateWindow()
    {
        // Sent without a frame id, the question would be answered by the top frame: the hidden throw site.
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Fail(Int32 n)", "/app/Program.vb", 15)];
            dap.LocalsById = Enumerable.Range(100, 40).ToDictionary(id => id, _ => new[] { "$exception", "n" });
        });

        Assert.Null(await service.EvaluateAsync("n", frameIndex: 0));
        Assert.False((await service.EvaluateReplAsync("n", isExpression: true, frameIndex: 0)).Succeeded);
        Assert.Empty(Evaluated(dap));
    }

    [Fact]
    public async Task AsManyHiddenFramesAsTheLimitAreStillPlaced()
    {
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Fail(Int32 n)", "/app/Program.vb", 15)];
            dap.LocalsById = Enumerable.Range(100, 16).ToDictionary(id => id, _ => new[] { "$exception" });
            dap.LocalsById[116] = ["$exception", "n"];
        });

        Assert.Contains("n", (await service.GetLocalsAsync(frameIndex: 0)).Select(v => v.Name));
    }

    [Fact]
    public async Task AWatchTriesEveryReadingSoAChainedCallIsFound()
    {
        // "name.Substring(0, 3).ToUpper()": the index reading of Substring is refused before anything runs;
        // stopping there, as the Immediate window does, left the watch "not available".
        var (dap, service) = await StoppedAsync();
        dap.EvaluateAnswer = arguments => arguments!["expression"]!.GetValue<string>() == "(name.Substring(0, 3)).ToUpper()"
            ? FakeDapConnection.Answer(true, new JsonObject { ["result"] = "\"ROS\"" })
            : FakeDapConnection.Answer(false, new JsonObject(), "error: 0x80070057");

        Assert.Equal("\"ROS\"", await service.EvaluateAsync("name.Substring(0, 3).ToUpper()", frameIndex: 0));
    }

    [Fact]
    public async Task NoReadingIsSentAfterATimeOut()
    {
        var (dap, service) = await StoppedAsync();
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(false, new JsonObject(), "Evaluation timed out");

        Assert.Null(await service.EvaluateAsync("Not slow", frameIndex: 0));
        Assert.Single(Evaluated(dap));
    }

    [Fact]
    public async Task AnExpressionInACSharpFrameIsSentAsTyped()
    {
        var (dap, service) = await StoppedAsync();

        await service.EvaluateAsync("a = b", frameIndex: 1);

        Assert.Equal(["a = b"], Evaluated(dap));
    }

    [Fact]
    public async Task AtAnExceptionThrownInTheProgramEachFrameIsAskedForByTheIdThatHoldsItsLocals()
    {
        // Measured on netcoredbg 3.2.0, a throw in Fail three calls deep: the listed frames come without a
        // module, id 0 is the hidden throw site holding only $exception, and each listed frame answers one higher.
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Fail(Int32 n)", "/app/Program.vb", 15), ("Program.Middle(Int32 depth)", "/app/Program.vb", 10), ("Program.Main()", "/app/Program.vb", 5)];
            dap.LocalsById = new()
            {
                [100] = ["$exception"],
                [101] = ["$exception", "n", "items"],
                [102] = ["$exception", "depth", "middleLocal"],
                [103] = ["$exception", "marker"],
            };
        });

        Assert.Contains("n", (await service.GetLocalsAsync(frameIndex: 0)).Select(v => v.Name));
        Assert.Contains("marker", (await service.GetLocalsAsync(frameIndex: 2)).Select(v => v.Name));

        await service.EvaluateAsync("depth", frameIndex: 1);
        Assert.Equal(102, dap.ArgumentsOf("evaluate")!["frameId"]!.GetValue<int>());
    }

    [Fact]
    public async Task AtAnExceptionThrownInsideALibraryTheHiddenFramesAreSkipped()
    {
        // Measured: a FormatException from Integer.Parse in Middle. Two hidden library frames hold only
        // $exception, so Middle answers at 2 and Main at 3: a fixed shift of one showed nothing for Middle.
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Middle(String text)", "/app/Program.vb", 9), ("Program.Main()", "/app/Program.vb", 4)];
            dap.LocalsById = new()
            {
                [100] = ["$exception"],
                [101] = ["$exception"],
                [102] = ["$exception", "text", "middleLocal", "value"],
                [103] = ["$exception", "marker"],
            };
        });

        Assert.Contains("text", (await service.GetLocalsAsync(frameIndex: 0)).Select(v => v.Name));
        Assert.Contains("marker", (await service.GetLocalsAsync(frameIndex: 1)).Select(v => v.Name));
    }

    [Fact]
    public async Task AFrameWithNoParametersOrLocalsKeepsItsOwnEmptyLocalsAtAnExceptionStop()
    {
        // Fail() has nothing to show: it must show that, not Main's marker. The shift is counted from the last
        // id, Main's, not guessed from parameters.
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Fail()", "/app/Program.vb", 12), ("Program.Main()", "/app/Program.vb", 4)];
            dap.LocalsById = new()
            {
                [100] = ["$exception"],
                [101] = ["$exception"],
                [102] = ["$exception", "marker"],
            };
        });

        Assert.DoesNotContain("marker", (await service.GetLocalsAsync(frameIndex: 0)).Select(v => v.Name));
        Assert.Contains("marker", (await service.GetLocalsAsync(frameIndex: 1)).Select(v => v.Name));
    }

    [Fact]
    public async Task AnExceptionStopWhoseFramesCarryAModuleIsNotShifted()
    {
        // The shift is netcoredbg's way with the frames it builds from an exception's trace, which have no
        // module; frames read from the running stack have one and their ids are right.
        var (dap, service) = await StoppedAsync("exception", framesHaveModule: true,
            arrange: dap => dap.LocalsById = new() { [100] = ["$exception", "total"], [101] = ["$exception", "total"] });

        await service.GetLocalsAsync(frameIndex: 0);

        Assert.Equal(100, dap.ArgumentsOf("scopes")!["frameId"]!.GetValue<int>());
    }

    [Fact]
    public async Task VisualBasicTheTranslatorCannotReadIsNotSentAsAnAssignment()
    {
        // An interpolated string is beyond the translator; sent as typed, "name = ..." would assign in C#.
        var (dap, service) = await StoppedAsync();

        await service.EvaluateReplAsync("name = $\"Hi {x}\"", isExpression: true, frameIndex: 0);

        Assert.Equal(["name == $\"Hi {x}\""], Evaluated(dap));
    }

    [Fact]
    public async Task AtABreakpointTheFrameIdsAreUsedAsGiven()
    {
        var (dap, service) = await StoppedAsync("breakpoint");

        await service.GetLocalsAsync(frameIndex: 0);

        Assert.Equal(100, dap.ArgumentsOf("scopes")!["frameId"]!.GetValue<int>());
    }

    [Fact]
    public async Task AnExceptionStopSaysWhatWasThrown()
    {
        var dap = new FakeDapConnection
        {
            ExceptionInfoAnswer = FakeDapConnection.Answer(true, new JsonObject
            {
                ["details"] = new JsonObject
                {
                    ["fullTypeName"] = "System.IndexOutOfRangeException",
                    ["message"] = "Index was outside the bounds of the array.",
                },
            }),
        };
        var service = new NetCoreDebugService(() => dap);
        var paused = new TaskCompletionSource<string?>();
        service.Paused += (_, _) => paused.TrySetResult(service.StoppedException);

        await service.LaunchAsync("/app/App.dll");
        dap.Raise("stopped", new JsonObject { ["threadId"] = 1, ["reason"] = "exception" });

        Assert.Equal("System.IndexOutOfRangeException: Index was outside the bounds of the array.",
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void ALinkedDirectoryIsMappedBackToTheSpellingTheIdeUsesAndNothingElseIs()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Directory symbolic links need privileges on Windows.");

        // As on macOS: "var" is a link to "private/var", so the two spellings share a name at their ends.
        // Resolved first: the system's own temporary folder sits under such a link on macOS.
        var root = NetCoreDebugService.ResolvePath(Directory.CreateTempSubdirectory("basalt-linked-prefix-").FullName);
        var real = Directory.CreateDirectory(Path.Combine(root, "private", "var", "App")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "private", "lib"));
        File.WriteAllText(Path.Combine(real, "App.dll"), "");
        var link = Path.Combine(root, "var");
        Directory.CreateSymbolicLink(link, Path.Combine(root, "private", "var"));

        try
        {
            var prefix = NetCoreDebugService.LinkedPrefix(Path.Combine(link, "App", "App.dll"));

            Assert.NotNull(prefix);
            var resolvedRoot = root;
            Assert.Equal(Path.Combine(resolvedRoot, "private", "var") + Path.DirectorySeparatorChar, prefix.Value.Resolved);
            Assert.Equal(link + Path.DirectorySeparatorChar, prefix.Value.Given);

            // A library beside the link, not through it, keeps its own path.
            Assert.False(Path.Combine(resolvedRoot, "private", "lib", "Lib.vb").StartsWith(prefix.Value.Resolved, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
