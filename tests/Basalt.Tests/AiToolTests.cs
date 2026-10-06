using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Basalt.Workspace;
using Basalt.Workspace.Ai;

namespace Basalt.Tests;

/// <summary>The loop that lets an assistant ask for tools and read their results.</summary>
public class AiToolLoopTests
{
    private static AiTool Echo(Func<JsonObject, AiToolResult>? handler = null) => new(
        "echo", "Echoes.",
        AiSchema_Object("text"),
        (input, _) => Task.FromResult(handler?.Invoke(input) ?? new AiToolResult("echo: " + input["text"])));

    private static JsonObject AiSchema_Object(string required) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { [required] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray(required)
    };

    private static async Task<List<AiChatEvent>> CollectAsync(IAsyncEnumerable<AiChatEvent> stream)
    {
        var events = new List<AiChatEvent>();
        await foreach (var update in stream) events.Add(update);
        return events;
    }

    [Fact]
    public async Task RunsAToolAndFeedsTheResultBackUntilTheAssistantIsDone()
    {
        var backend = new ScriptedBackend(
            ScriptedBackend.Call("Let me look.", "c1", "echo", new JsonObject { ["text"] = "hi" }),
            ScriptedBackend.Say("It said hi."));

        var conversation = new AiConversation();

        var events = await CollectAsync(conversation.AskAsync(backend, "Say hi", tools: [Echo()]));

        // text, the call, its result, then the final text, in that order.
        Assert.Equal("Let me look.", Assert.IsType<AiTextChunk>(events[0]).Text);
        Assert.Equal("echo", Assert.IsType<AiToolStarted>(events[1]).Call.Name);
        Assert.Equal("echo: hi", Assert.IsType<AiToolFinished>(events[2]).Result.Content);
        Assert.Equal("It said hi.", Assert.IsType<AiTextChunk>(events[3]).Text);

        // The second request carries the first turn and the tool's answer.
        var second = backend.Requests[1].Messages;
        Assert.Equal(3, second.Count);
        var result = Assert.Single(second[2].Parts.OfType<AiToolResultPart>());
        Assert.Equal("c1", result.CallId);
        Assert.Equal("echo: hi", result.Content);

        Assert.Equal(4, conversation.History.Count);
    }

    [AvaloniaFact]
    public async Task RunsToolsOffTheInterfaceThread()
    {
        var caller = Environment.CurrentManagedThreadId;
        var toolThread = -1;

        var tool = Echo(_ =>
        {
            toolThread = Environment.CurrentManagedThreadId;
            return new AiToolResult("ok");
        });

        var backend = new ScriptedBackend(
            ScriptedBackend.Call("", "c1", "echo", new JsonObject { ["text"] = "x" }),
            ScriptedBackend.Say("done"));

        await CollectAsync(new AiConversation().AskAsync(backend, "q", tools: [tool]));

        Assert.NotEqual(caller, toolThread);
    }

    [Fact]
    public async Task AnswersAnUnknownToolWithAnErrorInsteadOfFailing()
    {
        var backend = new ScriptedBackend(
            ScriptedBackend.Call("", "c1", "delete_everything", []),
            ScriptedBackend.Say("sorry"));

        var events = await CollectAsync(new AiConversation().AskAsync(backend, "q", tools: [Echo()]));

        var finished = events.OfType<AiToolFinished>().Single();

        Assert.True(finished.Result.IsError);
        Assert.Contains("no tool named", finished.Result.Content);
    }

    [Fact]
    public async Task DoesNotRunAToolWhoseArgumentsDoNotFitItsSchema()
    {
        // The model's output is untrusted: a missing or mistyped argument is
        // reported back, never passed to the handler.
        var ran = false;

        var tool = Echo(_ =>
        {
            ran = true;
            return new AiToolResult("ran");
        });

        var backend = new ScriptedBackend(
            ScriptedBackend.Call("", "c1", "echo", new JsonObject { ["text"] = 42 }),
            ScriptedBackend.Say("done"));

        var events = await CollectAsync(new AiConversation().AskAsync(backend, "q", tools: [tool]));

        Assert.False(ran);
        Assert.Contains("must be a string", events.OfType<AiToolFinished>().Single().Result.Content);
    }

    [Fact]
    public async Task DoesNotRunAToolWhoseArgumentsWereNotWholeJson()
    {
        var ran = false;
        var tool = Echo(_ => { ran = true; return new AiToolResult("ran"); });

        var broken = new AiTurnEnd(new AiMessage(AiRole.Assistant,
            [new AiToolCallPart("c1", "echo", [], "unterminated string")]), AiStopReason.ToolUse);

        var backend = new ScriptedBackend([broken], ScriptedBackend.Say("done"));

        var events = await CollectAsync(new AiConversation().AskAsync(backend, "q", tools: [tool]));

        Assert.False(ran);
        Assert.Contains("could not be read", events.OfType<AiToolFinished>().Single().Result.Content);
    }

    [Fact]
    public async Task TurnsAToolExceptionIntoAnErrorTheAssistantCanReadAndRetry()
    {
        var tool = new AiTool("boom", "Fails.", AiSchema_Object("text"),
            (_, _) => throw new AiToolException("no such file"));

        var backend = new ScriptedBackend(
            ScriptedBackend.Call("", "c1", "boom", new JsonObject { ["text"] = "x" }),
            ScriptedBackend.Say("ok"));

        var events = await CollectAsync(new AiConversation().AskAsync(backend, "q", tools: [tool]));

        var finished = events.OfType<AiToolFinished>().Single();

        Assert.True(finished.Result.IsError);
        Assert.Equal("no such file", finished.Result.Content);
    }

    [Fact]
    public async Task TellsTheUserWhenARefusalEndsTheReplyAndRunsNoTool()
    {
        var ran = false;
        var tool = Echo(_ => { ran = true; return new AiToolResult("ran"); });

        // A refusal can cut a call off mid-input.
        var refused = new AiTurnEnd(new AiMessage(AiRole.Assistant,
            [new AiToolCallPart("c1", "echo", new JsonObject { ["text"] = "x" })]),
            AiStopReason.Refusal, StopCategory: "cyber");

        var conversation = new AiConversation();
        var events = await CollectAsync(conversation.AskAsync(new ScriptedBackend([refused]), "q", tools: [tool]));

        var notice = events.OfType<AiNotice>().Single();

        Assert.False(ran);
        Assert.Equal(AiNoticeKind.Refusal, notice.Kind);
        Assert.Contains("declined", notice.Text);
        Assert.Contains("cyber", notice.Text);

        // Nothing unanswered is left in the history.
        Assert.DoesNotContain(conversation.History, m => m.ToolCalls.Count > 0);
    }

    [Fact]
    public async Task TellsTheUserWhenAFallbackModelAnswered()
    {
        var answer = new AiTurnEnd(new AiMessage(AiRole.Assistant, [new AiTextPart("Hello")]),
            AiStopReason.EndTurn, ModelUsed: "claude-opus-4-8", FellBack: true);

        var events = await CollectAsync(new AiConversation().AskAsync(new ScriptedBackend([answer]), "q"));

        var notice = events.OfType<AiNotice>().Single();

        Assert.Equal(AiNoticeKind.FallbackModel, notice.Kind);
        Assert.Contains("claude-opus-4-8", notice.Text);
    }

    [Fact]
    public async Task DoesNotRunAToolCallThatWasCutOffByTheLengthLimit()
    {
        var ran = false;
        var tool = Echo(_ => { ran = true; return new AiToolResult("ran"); });

        var cut = new AiTurnEnd(new AiMessage(AiRole.Assistant,
            [new AiToolCallPart("c1", "echo", new JsonObject { ["text"] = "half" })]), AiStopReason.MaxTokens);

        var events = await CollectAsync(new AiConversation().AskAsync(new ScriptedBackend([cut]), "q", tools: [tool]));

        Assert.False(ran);
        Assert.Equal(AiNoticeKind.Truncated, events.OfType<AiNotice>().Single().Kind);
    }

    [Fact]
    public async Task CancellingStopsTheLoopAndLeavesNoUnansweredCall()
    {
        using var cancel = new CancellationTokenSource();

        var tool = Echo(_ =>
        {
            cancel.Cancel();
            return new AiToolResult("ran");
        });

        var backend = new ScriptedBackend(
            ScriptedBackend.Call("", "c1", "echo", new JsonObject { ["text"] = "x" }),
            ScriptedBackend.Say("never reached"));

        var conversation = new AiConversation();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(conversation.AskAsync(backend, "q", tools: [tool], ct: cancel.Token)));

        // The loop stopped before asking again, and every call it did make
        // has its answer: a history a vendor would still accept.
        Assert.Single(backend.Requests);

        var answered = conversation.History.SelectMany(m => m.Parts.OfType<AiToolResultPart>()).Select(r => r.CallId);
        var asked = conversation.History.SelectMany(m => m.ToolCalls).Select(c => c.Id);

        Assert.Equal(asked.Order(), answered.Order());
    }

    [Fact]
    public async Task StopsAnAssistantThatNeverStopsAskingForTools()
    {
        var turns = Enumerable.Range(0, AiToolLoop.MaxRounds)
            .Select(i => ScriptedBackend.Call("", $"c{i}", "echo", new JsonObject { ["text"] = "x" }))
            .ToArray();

        var events = await CollectAsync(new AiConversation().AskAsync(
            new ScriptedBackend(turns), "q", tools: [Echo()]));

        Assert.Equal(AiNoticeKind.RoundLimit, events.OfType<AiNotice>().Single().Kind);
    }

    [Fact]
    public async Task NeverRewritesWhatWasAlreadySent()
    {
        // Append-only: a long result from an early question stays in full, so
        // each request starts with exactly the messages of the one before.
        var big = new string('x', 5000);
        var conversation = new AiConversation();
        var tool = Echo(_ => new AiToolResult(big));
        var backend = new ScriptedBackend(
            Enumerable.Range(0, 4).SelectMany(i => new[]
            {
                ScriptedBackend.Call("", $"c{i}", "echo", new JsonObject { ["text"] = "x" }),
                ScriptedBackend.Say("ok")
            }).ToArray());

        for (var i = 0; i < 4; i++)
            await CollectAsync(conversation.AskAsync(backend, $"q{i}", tools: [tool]));

        for (var i = 1; i < backend.Requests.Count; i++)
        {
            var earlier = backend.Requests[i - 1].Messages;
            var later = backend.Requests[i].Messages;

            Assert.True(later.Count > earlier.Count);
            Assert.Equal(earlier, later.Take(earlier.Count));
        }

        Assert.All(conversation.History.SelectMany(m => m.Parts.OfType<AiToolResultPart>()),
            r => Assert.Equal(big, r.Content));
    }

    [Fact]
    public async Task ClearingTheHistoryMidRunDoesNotBreakTheLoop()
    {
        using var cancel = new CancellationTokenSource();
        var conversation = new AiConversation();

        var tool = Echo(_ =>
        {
            conversation.Clear();
            cancel.Cancel();
            return new AiToolResult("ran");
        });

        var backend = new ScriptedBackend(
            ScriptedBackend.Call("", "c1", "echo", new JsonObject { ["text"] = "x" }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CollectAsync(conversation.AskAsync(backend, "q", tools: [tool], ct: cancel.Token)));
    }

    [Fact]
    public void NeverCutsASurrogatePairInHalf()
    {
        var text = new string('a', 9) + "\U0001F600" + "tail";

        var cut = AiText.Truncate(text, 10);

        Assert.Equal(new string('a', 9), cut);
        Assert.False(char.IsHighSurrogate(cut[^1]));
    }
}

/// <summary>What the assistant may read, and where it may not go.</summary>
public sealed class AiWorkspaceToolTests : IDisposable
{
    private readonly FakeHost _host = new();
    private readonly AiWorkspaceTools _tools;

    public AiWorkspaceToolTests() => _tools = new AiWorkspaceTools(_host);

    private async Task<AiToolResult> CallAsync(string tool, JsonObject input)
    {
        var found = _tools.Create().Single(t => t.Name == tool);

        Assert.Null(found.Validate(input));

        return await found.Handler(input, CancellationToken.None);
    }

    private async Task<string> FailureOfAsync(string tool, JsonObject input) =>
        (await Assert.ThrowsAsync<AiToolException>(() => CallAsync(tool, input))).Message;

    public void Dispose() => _host.Dispose();

    // Confinement

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    public async Task RefusesAPathThatClimbsOutOfTheSolutionFolder(string path)
    {
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_host.RootDirectory)!, "outside.txt"), "secret");

        try
        {
            Assert.Contains("outside", await FailureOfAsync("read_file", new JsonObject { ["path"] = path }));
        }
        finally
        {
            File.Delete(Path.Combine(Path.GetDirectoryName(_host.RootDirectory)!, "outside.txt"));
        }
    }

    [Fact]
    public async Task RefusesAnAbsolutePathElsewhere()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "basalt-elsewhere.txt");
        File.WriteAllText(elsewhere, "x");

        try
        {
            Assert.Contains("outside", await FailureOfAsync("read_file", new JsonObject { ["path"] = elsewhere }));
        }
        finally
        {
            File.Delete(elsewhere);
        }
    }

    [Fact]
    public async Task RefusesASymbolicLinkThatLeadsOutOfTheFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a link needs privileges on Windows.");

        var target = Directory.CreateTempSubdirectory("basalt-target-").FullName;
        File.WriteAllText(Path.Combine(target, "data.txt"), "outside data");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_host.RootDirectory, "link"), target);
            File.CreateSymbolicLink(Path.Combine(_host.RootDirectory, "file-link.txt"), Path.Combine(target, "data.txt"));

            Assert.Contains("outside", await FailureOfAsync("read_file", new JsonObject { ["path"] = "link/data.txt" }));
            Assert.Contains("outside", await FailureOfAsync("read_file", new JsonObject { ["path"] = "file-link.txt" }));
        }
        finally
        {
            Directory.Delete(target, recursive: true);
        }
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.production")]
    [InlineData("config/.env.local")]
    [InlineData("deploy/server.pem")]
    [InlineData("certs/app.key")]
    [InlineData("home/.ssh/id_rsa")]
    [InlineData("secrets.json")]
    public async Task RefusesFilesThatMayHoldSecrets(string path)
    {
        _host.Write(path, "API_KEY=sk-live-123");

        Assert.Contains("secrets", await FailureOfAsync("read_file", new JsonObject { ["path"] = path }));
    }

    [Theory]
    [InlineData(".git/config")]
    [InlineData("nuget.config")]
    [InlineData("NuGet.Config")]
    [InlineData("deploy/site.publishsettings")]
    [InlineData("Properties/PublishProfiles/prod.pubxml.user")]
    [InlineData("keys/app.snk")]
    [InlineData("appsettings.Development.json")]
    [InlineData("appsettings.Production.json")]
    [InlineData("Properties/launchSettings.json")]
    [InlineData(".pgpass")]
    [InlineData("infra/prod.tfvars")]
    public async Task RefusesMoreFilesThatCarryCredentials(string path)
    {
        _host.Write(path, "password=hunter2");

        Assert.Contains("secrets", await FailureOfAsync("read_file", new JsonObject { ["path"] = path }));
    }

    [Fact]
    public async Task StillReadsTheSharedAppSettings()
    {
        _host.Write("appsettings.json", "{}");

        Assert.Contains("{}", (await CallAsync("read_file", new JsonObject { ["path"] = "appsettings.json" })).Content);
    }

    [Theory]
    [InlineData("bin/Debug/App.dll")]
    [InlineData("obj/project.assets.json")]
    [InlineData("src/obj/x.vb")]
    [InlineData(".git/hooks/pre-commit")]
    public async Task NeverProposesToWriteBuildOutputOrRepositoryData(string path)
    {
        var message = await FailureOfAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = path, ["content"] = "x" })
        });

        Assert.True(message.Contains("not edited") || message.Contains("secrets"), message);
    }

    [Fact]
    public async Task RefusesAnAlternateDataStreamNameOnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Alternate data streams are a Windows file system feature.");

        _host.Write("ok.txt", "x");

        Assert.Contains("data streams", await FailureOfAsync("read_file", new JsonObject { ["path"] = ".env::$DATA" }));
        Assert.Contains("data streams", await FailureOfAsync("read_file", new JsonObject { ["path"] = "ok.txt:hidden" }));
    }

    [Fact]
    public async Task WorksThroughASymbolicLinkToTheSolutionFolder()
    {
        // /tmp and /var are links on macOS; a solution opened through one
        // must find its buffers and its files whichever spelling is used.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a link needs privileges on Windows.");

        var real = _host.RootDirectory;
        var link = Path.Combine(Directory.CreateTempSubdirectory("basalt-link-").FullName, "solution");
        Directory.CreateSymbolicLink(link, real);

        try
        {
            var file = _host.Write("A.vb", "on disk");
            var linked = new FakeHost(link);
            linked.Buffers[Path.Combine(link, "A.vb")] = "typed in the editor";

            var tools = new AiWorkspaceTools(linked).Create();
            var read = await tools.Single(t => t.Name == "read_file").Handler(new JsonObject { ["path"] = "A.vb" }, default);
            var found = await tools.Single(t => t.Name == "search_text").Handler(new JsonObject { ["query"] = "editor" }, default);

            Assert.Contains("typed in the editor", read.Content);
            Assert.Contains("A.vb:1", found.Content);
            Assert.Equal(PathIdentity.Canonical(file), PathIdentity.Canonical(Path.Combine(link, "A.vb")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(link)!, recursive: true);
        }
    }

    [Fact]
    public async Task FindsTheFileWhateverCaseTheModelWritesItIn()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows(),
            "Only a case-insensitive disk answers to the wrong case.");

        var path = _host.Write("Sub/Program.vb", "Module P");
        _host.Buffers[path] = "Module P ' edited";

        var read = (await CallAsync("read_file", new JsonObject { ["path"] = "sub/PROGRAM.vb" })).Content;

        Assert.Contains("edited", read);
        Assert.Equal(path, PathIdentity.Canonical(Path.Combine(_host.RootDirectory, "sub", "PROGRAM.vb")));
    }

    [Fact]
    public async Task KeepsTheLineEndingsOfAFileItRewritesAndNeverShowsItsByteOrderMark()
    {
        // Reading strips a BOM, so the text the assistant sees and rewrites has
        // none; keeping it on disk is the document's job when it is saved.
        var path = Path.Combine(_host.RootDirectory, "A.vb");
        File.WriteAllBytes(path, [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. "Sub A()\r\nEnd Sub\r\n"u8.ToArray()]);

        var shown = (await CallAsync("read_file", new JsonObject { ["path"] = "A.vb" })).Content;
        Assert.False(shown.Contains('\uFEFF'));

        await CallAsync("read_file", new JsonObject { ["path"] = "A.vb" });

        var result = await CallAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = "A.vb", ["content"] = "Sub B()\nEnd Sub\n" })
        });

        Assert.Equal("Sub B()\r\nEnd Sub\r\n", result.Proposal!.Files[0].NewText);
    }

    [Fact]
    public async Task AnswersAPathologicalGlobWithAnErrorInsteadOfHanging()
    {
        _host.Write(new string('a', 60) + ".txt", "x");

        var message = await FailureOfAsync("list_files", new JsonObject { ["glob"] = string.Concat(Enumerable.Repeat("*a", 14)) + "*b" });

        Assert.Contains("took too long", message);
    }

    [Fact]
    public async Task EveryToolAnswersWithAnErrorWhenNoSolutionIsOpen()
    {
        var none = new AiWorkspaceTools(() => null).Create();

        // The same six tools exist with or without a solution.
        Assert.Equal(_tools.Create().Select(t => t.Name), none.Select(t => t.Name));
        Assert.Equal(_tools.Create().Select(t => t.Description), none.Select(t => t.Description));

        var failure = await Assert.ThrowsAsync<AiToolException>(
            () => none.Single(t => t.Name == "list_files").Handler([], default));

        Assert.Contains("No solution", failure.Message);
    }

    [Fact]
    public async Task RefusesTheIdesOwnKeyStoreWhereverTheSolutionIs()
    {
        var keys = Path.Combine(ApiKeyStore.DefaultFallbackPath, "claude.key");

        Assert.True(AiWorkspaceSandbox.IsSecret(keys));
        Assert.Contains("secrets", await FailureOfAsync("read_file", new JsonObject { ["path"] = keys }));
    }

    [Fact]
    public async Task RefusesALinkToASecretInsideTheFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a link needs privileges on Windows.");

        var secret = _host.Write(".env", "KEY=1");
        File.CreateSymbolicLink(Path.Combine(_host.RootDirectory, "innocent.txt"), secret);

        Assert.Contains("secrets", await FailureOfAsync("read_file", new JsonObject { ["path"] = "innocent.txt" }));
    }

    [Fact]
    public async Task NeverProposesToWriteASecretOrOutsideTheFolder()
    {
        Assert.Contains("secrets", await FailureOfAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = ".env", ["content"] = "KEY=2" })
        }));

        Assert.Contains("outside", await FailureOfAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = "../x.vb", ["content"] = "x" })
        }));
    }

    // Reading

    [Fact]
    public async Task ListsFilesRelativeToTheFolderLeavingOutSecretsAndBuildOutput()
    {
        _host.Write("src/A.vb", "x");
        _host.Write("src/B.cs", "x");
        _host.Write("bin/Debug/A.dll", "x");
        _host.Write(".env", "KEY=1");

        var listed = (await CallAsync("list_files", [])).Content.Split('\n');

        Assert.Equal(["src/A.vb", "src/B.cs"], listed);

        Assert.Equal("src/A.vb", (await CallAsync("list_files", new JsonObject { ["glob"] = "**/*.vb" })).Content);
    }

    [Fact]
    public async Task ReadsTheEditorsBufferRatherThanTheFileOnDisk()
    {
        var path = _host.Write("A.vb", "on disk");
        _host.Buffers[path] = "unsaved edit";

        var read = (await CallAsync("read_file", new JsonObject { ["path"] = "A.vb" })).Content;

        Assert.Contains("unsaved edit", read);
        Assert.DoesNotContain("on disk", read);
    }

    [Fact]
    public async Task ReadsALineRangeWithLineNumbers()
    {
        _host.Write("A.vb", "one\ntwo\nthree\nfour");

        var read = (await CallAsync("read_file", new JsonObject
        {
            ["path"] = "A.vb", ["start_line"] = 2, ["end_line"] = 3
        })).Content;

        Assert.Contains("2| two", read);
        Assert.Contains("3| three", read);
        Assert.DoesNotContain("one", read);
        Assert.DoesNotContain("four", read);
    }

    [Fact]
    public async Task SearchesFilesAndOpenBuffers()
    {
        _host.Write("A.vb", "Dim needle = 1");
        var b = _host.Write("B.vb", "nothing here");
        _host.Buffers[b] = "typed needle only in the editor";

        var found = (await CallAsync("search_text", new JsonObject { ["query"] = "needle" })).Content;

        Assert.Contains("A.vb:1", found);
        Assert.Contains("B.vb:1", found);
    }

    [Fact]
    public async Task SearchesWithARegularExpressionAndReportsABadOne()
    {
        _host.Write("A.vb", "Sub Foo123()");

        var found = (await CallAsync("search_text",
            new JsonObject { ["query"] = @"Foo\d+", ["regex"] = true })).Content;

        Assert.Contains("A.vb:1", found);
        Assert.Contains("Invalid regular expression", await FailureOfAsync("search_text",
            new JsonObject { ["query"] = "(", ["regex"] = true }));
    }

    [Fact]
    public async Task SearchNeverLooksInsideSecretFiles()
    {
        _host.Write(".env", "needle=1");

        Assert.Equal("No matches.", (await CallAsync("search_text", new JsonObject { ["query"] = "needle" })).Content);
    }

    [Fact]
    public async Task ReportsDiagnosticsAndFindsSymbolsThroughTheHost()
    {
        var path = _host.Write("A.vb", "x");
        _host.Diagnostics.Add(new AiDiagnosticInfo("BC30002", "Type 'Foo' is not defined.", "Error", path, 3, 5));
        _host.Symbols.Add(new AiSymbolInfo("Customer", "Class", path, 7, 14));

        Assert.Contains("A.vb(3,5): Error BC30002", (await CallAsync("get_diagnostics", [])).Content);
        Assert.Contains("A.vb:7:14", (await CallAsync("find_symbol", new JsonObject { ["name"] = "cust" })).Content);
    }

    [Fact]
    public async Task DropsSymbolsAndDiagnosticsOutsideTheFolder()
    {
        _host.Symbols.Add(new AiSymbolInfo("Outside", "Class", "/somewhere/else/X.vb", 1, 1));

        Assert.Contains("No symbol", (await CallAsync("find_symbol", new JsonObject { ["name"] = "Outside" })).Content);
    }

    [Fact]
    public void DeclaresTheSixToolsInAStableOrder()
    {
        Assert.Equal(
            ["find_symbol", "get_diagnostics", "list_files", "propose_edits", "read_file", "search_text"],
            _tools.Create().Select(t => t.Name));
    }

    // Proposing

    [Fact]
    public async Task ProposingWritesNothingToDiskOrToTheEditors()
    {
        var path = _host.Write("A.vb", "Module A\nEnd Module\n");
        _host.Buffers[path] = "Module A\nEnd Module\n";

        await CallAsync("read_file", new JsonObject { ["path"] = "A.vb" });

        AiChangeSet? proposed = null;
        _tools.ChangeProposed += (_, set) => proposed = set;

        var result = await CallAsync("propose_edits", new JsonObject
        {
            ["summary"] = "Rename",
            ["edits"] = new JsonArray(
                new JsonObject { ["path"] = "A.vb", ["old_text"] = "Module A", ["new_text"] = "Module B" },
                new JsonObject { ["path"] = "New.vb", ["content"] = "Module New\nEnd Module" })
        });

        Assert.False(result.IsError);
        Assert.Same(proposed, result.Proposal);
        Assert.Equal(2, proposed!.Files.Count);
        Assert.All(proposed.Files, f => Assert.Equal(AiChangeState.Pending, f.State));

        Assert.Equal("Module A\nEnd Module\n", File.ReadAllText(path));
        Assert.Equal("Module A\nEnd Module\n", _host.Buffers[path]);
        Assert.False(File.Exists(Path.Combine(_host.RootDirectory, "New.vb")));

        var change = proposed.Files.Single(f => f.RelativePath == "A.vb");
        Assert.Equal("Module B\nEnd Module\n", change.NewText);
        Assert.Contains("-Module A", change.UnifiedDiff);
        Assert.Contains("+Module B", change.UnifiedDiff);
        Assert.True(proposed.Files.Single(f => f.RelativePath == "New.vb").IsNewFile);
    }

    [Fact]
    public async Task RefusesAnEditWhoseTextIsNotThereOrIsThereTwice()
    {
        _host.Write("A.vb", "x = 1\nx = 1\n");
        await CallAsync("read_file", new JsonObject { ["path"] = "A.vb" });

        Assert.Contains("not found", await FailureOfAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = "A.vb", ["old_text"] = "y = 2", ["new_text"] = "z" })
        }));

        Assert.Contains("2 times", await FailureOfAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = "A.vb", ["old_text"] = "x = 1", ["new_text"] = "z" })
        }));
    }

    [Fact]
    public async Task RefusesToReplaceAFileItNeverRead()
    {
        _host.Write("A.vb", "original");

        Assert.Contains("read the file", await FailureOfAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = "A.vb", ["content"] = "overwritten" })
        }));
    }

    [Fact]
    public async Task RefusesAProposalWhenTheFileChangedSinceItWasRead()
    {
        var path = _host.Write("A.vb", "Module A");
        _host.Buffers[path] = "Module A";

        await CallAsync("read_file", new JsonObject { ["path"] = "A.vb" });

        // The user types in the editor after the assistant read the file.
        _host.Buffers[path] = "Module A ' user edit";

        Assert.Contains("changed since you read it", await FailureOfAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject { ["path"] = "A.vb", ["old_text"] = "Module A", ["new_text"] = "Module B" })
        }));
    }

    [Fact]
    public async Task EditsAFileWithWindowsLineEndingsInItsOwnConvention()
    {
        _host.Write("A.vb", "Sub A()\r\n    x = 1\r\nEnd Sub\r\n");
        await CallAsync("read_file", new JsonObject { ["path"] = "A.vb" });

        var result = await CallAsync("propose_edits", new JsonObject
        {
            ["edits"] = new JsonArray(new JsonObject
            {
                ["path"] = "A.vb", ["old_text"] = "    x = 1\n", ["new_text"] = "    x = 2\n"
            })
        });

        Assert.Equal("Sub A()\r\n    x = 2\r\nEnd Sub\r\n", result.Proposal!.Files[0].NewText);
    }
}

/// <summary>The unified diff shown for each file.</summary>
public class TextDiffTests
{
    [Fact]
    public void ShowsOnlyTheChangedLinesWithContext()
    {
        var before = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var after = before.Replace("line 10", "line ten");

        var diff = TextDiff.Unified("A.vb", before, after);

        Assert.Contains("--- a/A.vb", diff);
        Assert.Contains("@@ -7,7 +7,7 @@", diff);
        Assert.Contains("-line 10", diff);
        Assert.Contains("+line ten", diff);
        Assert.DoesNotContain("line 1\n", diff);
    }

    [Fact]
    public void SaysNothingWhenNothingChanged() =>
        Assert.Equal("", TextDiff.Unified("A.vb", "same", "same"));

    [Fact]
    public void ShowsANewFileAsAllAdded()
    {
        var diff = TextDiff.Unified("New.vb", "", "a\nb");

        Assert.Contains("+a", diff);
        Assert.Contains("+b", diff);
    }
}
