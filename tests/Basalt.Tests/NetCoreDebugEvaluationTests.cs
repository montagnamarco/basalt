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

        await service.EvaluateReplAsync("n + 0", isExpression: true, frameIndex: 0);

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

    [Theory]
    [InlineData("$VB$Local_n", "n")]
    [InlineData("$VB$ResumableLocal_before$0", "before")]
    [InlineData("$VB$ResumableLocal_doubled$12", "doubled")]
    [InlineData("$VB$Me", "Me")]
    [InlineData("$State", null)]
    [InlineData("$Builder", null)]
    [InlineData("$A0", null)]
    public void NamesAHoistedFieldAsTheSourceDid(string field, string? name) =>
        Assert.Equal(name, NetCoreDebugService.SourceNameOfHoisted(field));

    /// <remarks>
    /// Measured on netcoredbg 3.2.0 in ComputeAsync(n): the frame held only "this", the state machine, and the
    /// Variables panel showed nothing the user wrote; a watch on "n" was not available.
    /// </remarks>
    [Fact]
    public async Task AnAsyncMethodsLocalsAreShownAndReadUnderTheirOwnNames()
    {
        var (dap, service) = await StoppedAsync(arrange: dap =>
        {
            dap.Frames = [("Program.VB$StateMachine_1_ComputeAsync.MoveNext()", "/app/Program.vb", 12)];
            dap.LocalsById = new() { [100] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("this", "{Program.VB$StateMachine_1_ComputeAsync}", "Program.VB$StateMachine_1_ComputeAsync", 2)],
                [2] =
                [
                    ("$State", "-1", "int", 0),
                    ("$Builder", "{AsyncTaskMethodBuilder}", "AsyncTaskMethodBuilder<int>", 3),
                    ("$VB$Local_n", "5", "int", 0),
                    ("$VB$ResumableLocal_doubled$1", "10", "int", 0),
                ],
            };
        });
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(false, new JsonObject(), "error: The name 'n' does not exist in the current context");

        var locals = await service.GetLocalsAsync(frameIndex: 0);

        Assert.Equal(["n", "doubled"], locals.Select(v => v.Name));
        Assert.Equal("5", await service.EvaluateAsync("n", frameIndex: 0));
        Assert.Equal("10", await service.EvaluateAsync("Doubled", frameIndex: 0));
    }

    [Fact]
    public async Task ALambdasCapturedLocalsAreShownAndItsMeIsTheObjectNotTheClosure()
    {
        var (dap, service) = await StoppedAsync(arrange: dap =>
        {
            dap.Frames = [("Program._Closure$__0-0._Lambda$__1()", "/app/Program.vb", 7)];
            dap.LocalsById = new() { [100] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("this", "{Program._Closure$__0-0}", "Program._Closure$__0-0", 2), ("local", "42", "int", 0)],
                [2] = [("$VB$Local_total", "6", "int", 0), ("$VB$Me", "{Customer}", "Customer", 3)],
            };
        });

        var locals = await service.GetLocalsAsync(frameIndex: 0);

        Assert.Equal(["total", "Me", "local"], locals.Select(v => v.Name));
        Assert.Equal("{Customer}", locals.Single(v => v.Name == "Me").Value);
    }

    [Fact]
    public async Task AWatchOnAPlainNameWhoseStopMovedOnSaysNothing()
    {
        // The locals were read for a stop the program has left (F11 while they were being read): their value
        // belongs to another place.
        var (dap, service) = await StoppedAsync();
        dap.OnRequest = command =>
        {
            if (command != "variables") return;

            dap.OnRequest = null;
            dap.Raise("continued", new JsonObject { ["threadId"] = 1 });
            dap.Raise("stopped", new JsonObject { ["threadId"] = 1, ["reason"] = "step" });
        };

        Assert.Null(await service.EvaluateAsync("total", frameIndex: 0));
    }

    [Fact]
    public async Task APlainNameAtAnExceptionStopIsReadFromTheShiftedFrame()
    {
        var (dap, service) = await StoppedAsync("exception", arrange: dap =>
        {
            dap.Frames = [("Program.Fail(Int32 n)", "/app/Program.vb", 15), ("Program.Main()", "/app/Program.vb", 5)];
            dap.LocalsById = new() { [100] = [], [101] = [], [102] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("$exception", "{System.IndexOutOfRangeException}", "System.IndexOutOfRangeException", 0)],
                [1101] = [("n", "3", "int", 0)],
                [1102] = [("n", "99", "int", 0)],
            };
        });

        Assert.Equal("3", await service.EvaluateAsync("n", frameIndex: 0));
        Assert.Equal("3", (await service.EvaluateReplAsync("n", isExpression: true, frameIndex: 0)).Text);
    }

    [Fact]
    public async Task MeWhereThereIsNoneIsNotTheStateMachine()
    {
        // An Async Function in a Module: no $VB$Me field, and the evaluator's "this" is the compiler's machine.
        var (dap, service) = await StoppedAsync(arrange: dap =>
        {
            dap.Frames = [("Program.VB$StateMachine_1_RunAsync.MoveNext()", "/app/Program.vb", 20)];
            dap.LocalsById = new() { [100] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("this", "{Program.VB$StateMachine_1_RunAsync}", "Program.VB$StateMachine_1_RunAsync", 2)],
                [2] = [("$VB$Local_n", "5", "int", 0)],
            };
        });
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(true, new JsonObject { ["result"] = "{Program.VB$StateMachine_1_RunAsync}" });

        Assert.Null(await service.EvaluateAsync("Me", frameIndex: 0));
    }

    [Fact]
    public async Task TwoHoistedLocalsWithOneNameAreBothShownAndAWatchDoesNotGuessAndACapturedOneIsOpened()
    {
        var (dap, service) = await StoppedAsync(arrange: dap =>
        {
            dap.Frames = [("Program.VB$StateMachine_1_RunAsync.MoveNext()", "/app/Program.vb", 20)];
            dap.LocalsById = new() { [100] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("this", "{Program.VB$StateMachine_1_RunAsync}", "Program.VB$StateMachine_1_RunAsync", 2)],
                [2] =
                [
                    ("$VB$ResumableLocal_item$0", "1", "int", 0),
                    ("$VB$ResumableLocal_item$2", "9", "int", 0),
                    ("$VB$ResumableLocal_$VB$Closure_2$1", "{Program._Closure$__2-0}", "Program._Closure$__2-0", 5),
                ],
                [5] = [("$VB$Local_captured", "\"x\"", "string", 0)],
            };
        });

        var locals = await service.GetLocalsAsync(frameIndex: 0);

        Assert.Equal(["item", "item", "captured"], locals.Select(v => v.Name));
        Assert.Equal(["1", "9"], locals.Where(v => v.Name == "item").Select(v => v.Value));
        Assert.Null(await service.EvaluateAsync("item", frameIndex: 0));
    }

    [Fact]
    public async Task MeInAnAsyncMethodIsTheObjectNotTheStateMachine()
    {
        var (dap, service) = await StoppedAsync(arrange: dap =>
        {
            dap.Frames = [("Program.VB$StateMachine_1_RunAsync.MoveNext()", "/app/Program.vb", 20)];
            dap.LocalsById = new() { [100] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("this", "{Program.VB$StateMachine_1_RunAsync}", "Program.VB$StateMachine_1_RunAsync", 2)],
                [2] = [("$VB$Me", "{Customer}", "Customer", 3), ("$VB$Local_n", "5", "int", 0)],
            };
        });
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(true, new JsonObject { ["result"] = "{Program.VB$StateMachine_1_RunAsync}" });

        Assert.Equal("{Customer}", await service.EvaluateAsync("Me", frameIndex: 0));

        var repl = await service.EvaluateReplAsync("n", isExpression: true, frameIndex: 0);
        Assert.Equal("5", repl.Text);
        Assert.DoesNotContain(dap.Requests, r => r.Command == "evaluate");
    }

    [Fact]
    public async Task ChildrenAndImmediateAnswersOfAVisualBasicFrameAreShownInVisualBasic()
    {
        var (dap, service) = await StoppedAsync(arrange: dap =>
        {
            dap.Frames = [("Program.Main()", "/app/Module1.vb", 5)];
            dap.LocalsById = new() { [100] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("c", "{Customer}", "Customer", 7)],
                [7] = [("Name", "null", "string", 0), ("Tags", "{string[2]}", "string[]", 8)],
            };
        });
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(true, new JsonObject { ["result"] = "false" });

        await service.GetLocalsAsync(frameIndex: 0);
        var children = await service.ExpandAsync(7);

        Assert.Equal(["Nothing", "{Length=2}"], children.Select(v => v.Value));
        Assert.Equal(["String", "String()"], children.Select(v => v.Type));
        Assert.Equal("False", (await service.EvaluateReplAsync("c.Name Is Nothing", isExpression: true, frameIndex: 0)).Text);
    }

    [Fact]
    public async Task AVisualBasicFrameIsShownInVisualBasicAndACSharpFrameAsItCame()
    {
        var (dap, service) = await StoppedAsync(arrange: dap =>
        {
            dap.Frames = [("Program.Fail(Int32 n)", "/app/Module1.vb", 5), ("Helpers.Caller(Int32 n)", "/app/Helpers.cs", 9)];
            dap.LocalsById = new() { [100] = [], [101] = [] };
            dap.ChildrenByReference = new()
            {
                [1100] = [("this", "{Customer}", "Customer", 0), ("name", "null", "string", 0), ("ok", "true", "bool", 0)],
                [1101] = [("this", "{Helpers}", "Helpers", 0), ("name", "null", "string", 0)],
            };
        });
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(true, new JsonObject { ["result"] = "true" });

        var basic = await service.GetLocalsAsync(frameIndex: 0);
        Assert.Equal(["Me", "name", "ok"], basic.Select(v => v.Name));
        Assert.Equal(["Customer", "String", "Boolean"], basic.Select(v => v.Type));
        Assert.Equal(["{Customer}", "Nothing", "True"], basic.Select(v => v.Value));

        var csharp = await service.GetLocalsAsync(frameIndex: 1);
        Assert.Equal(["this", "name"], csharp.Select(v => v.Name));
        Assert.Equal(["Helpers", "string"], csharp.Select(v => v.Type));

        var stack = await service.GetCallStackAsync();
        Assert.Equal(["Program.Fail(n As Integer)", "Helpers.Caller(Int32 n)"], stack.Select(f => f.Method));

        Assert.Equal("True", await service.EvaluateAsync("ok", frameIndex: 0));
        Assert.Equal("true", await service.EvaluateAsync("ok", frameIndex: 1));
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

        // An expression, not a plain name: a plain name is found among the locals without asking the evaluator.
        await service.EvaluateAsync("depth + 0", frameIndex: 1);
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
