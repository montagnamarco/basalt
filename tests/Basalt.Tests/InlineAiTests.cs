using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Extensibility;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Ai;

namespace Basalt.Tests;

/// <summary>
/// Inline suggestions from the assistant: what is asked, when, and what is
/// done with the answer.
/// </summary>
/// <remarks>
/// Only fakes: no provider is called and no key is read. The editor is driven
/// through a provider that records what it was asked and answers when the test
/// says so, which is the only way to put an answer after a keystroke.
/// </remarks>
public class InlineAiTests
{
    // In "\n": the caret is placed by searching for "\n\n".
    private static readonly string Code = """
        Module Program
            Sub Main()

            End Sub
        End Module
        """.ReplaceLineEndings("\n");

    private static int BlankLine => Code.IndexOf("\n\n", StringComparison.Ordinal) + 1;

    /// <summary>A provider the test controls.</summary>
    private sealed class Script : IInlineSuggestionProvider
    {
        public bool IsAvailable { get; set; } = true;

        /// <summary>What to answer; given the call number (1-based).</summary>
        public Func<int, CancellationToken, Task<InlineSuggestion?>>? Answer { get; set; }

        public List<(string File, string Text, int Position, long At)> Calls { get; } = [];

        public int InFlight { get; private set; }
        public int MostInFlight { get; private set; }

        public async Task<InlineSuggestion?> SuggestAsync(
            string filePath, string text, int position, CancellationToken ct = default)
        {
            Calls.Add((filePath, text, position, Environment.TickCount64));
            MostInFlight = Math.Max(MostInFlight, ++InFlight);

            try
            {
                return Answer is null ? null : await Answer(Calls.Count, ct).ConfigureAwait(true);
            }
            finally
            {
                InFlight--;
            }
        }
    }

    private static Task<InlineSuggestion?> Said(string text, int position) =>
        Task.FromResult<InlineSuggestion?>(new InlineSuggestion(text, position));

    private static (Window Window, CodeEditor Editor, MainWindowViewModel Shell) Open(
        IInlineSuggestionProvider? provider, string path = "/x/Program.vb", string? code = null)
    {
        var shell = new MainWindowViewModel();
        var editor = new CodeEditor(new EditorDocumentViewModel(path, code ?? Code), shell)
        {
            SuggestionProvider = provider,
            WindowActiveOverride = () => true
        };

        // While the completion list is open no suggestion is asked for (tested
        // on its own); these tests are about the suggestion, so the list, which
        // opens on a letter, is not left to Roslyn's timing.
        var quiet = new Basalt.Core.Settings.IdeSettings();
        quiet.Editor.CompleteAutomatically = false;
        editor.ApplySettings(quiet);

        // Short, so a test waits for tens of milliseconds, not for what a
        // person would; the production values are asserted in their own test.
        editor.InlineOptions.Pause = TimeSpan.FromMilliseconds(30);
        editor.InlineOptions.MinInterval = TimeSpan.Zero;

        var window = new Window { Content = editor, Width = 700, Height = 500 };

        window.Show();
        window.UpdateLayout();

        editor.CaretOffsetForTests = code is null ? BlankLine : code.Length;

        return (window, editor, shell);
    }

    private static async Task<bool> UntilAsync(Func<bool> condition, int milliseconds = 5000)
    {
        var deadline = Environment.TickCount64 + milliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        return condition();
    }

    /// <summary>Waits long enough for a request that is not going to happen.</summary>
    private static async Task QuietAsync(int milliseconds = 400)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    // The text of the answer

    [Fact]
    public void AFenceAndTheProseAroundItAreStripped()
    {
        Assert.Equal("Console.WriteLine(1)",
            AiInlineCompletion.Clean("```vb\nConsole.WriteLine(1)\n```", "    ", ""));

        Assert.Equal("Console.WriteLine(1)",
            AiInlineCompletion.Clean("Here you go:\n```vbnet\nConsole.WriteLine(1)\n```\nHope it helps.", "    ", ""));

        // A fence the model never closed (the answer was cut).
        Assert.Equal("x = 1", AiInlineCompletion.Clean("```\nx = 1", "", ""));
    }

    [Fact]
    public void TheEchoedPrefixIsStripped()
    {
        // The model began with the line the caret is on.
        Assert.Equal("nsole.WriteLine(1)",
            AiInlineCompletion.Clean("Console.WriteLine(1)", "    Sub Main()\n        Co", ""));

        // Or with several lines of what is already there.
        Assert.Equal("\n        x = 1",
            AiInlineCompletion.Clean("    Sub Main()\n        Dim y = 2\n        x = 1",
                "Module M\n    Sub Main()\n        Dim y = 2", ""));
    }

    [Fact]
    public void WhatIsAlreadyAfterTheCaretIsNotSuggestedAgain()
    {
        // Wholly there: nothing to show.
        Assert.Null(AiInlineCompletion.Clean("End Sub", "    x = 1\n", "    End Sub\nEnd Module"));

        // Its tail is what follows the caret: only the new part is kept.
        Assert.Equal("x = 1", AiInlineCompletion.Clean("x = 1\n    End Sub", "    ", "\n    End Sub\nEnd Module"));
    }

    [Fact]
    public void ASuggestionIsAFewLinesAndNeverEmpty()
    {
        var long8 = string.Join("\n", Enumerable.Range(1, 8).Select(i => $"x{i} = {i}"));

        Assert.Equal(AiInlineCompletion.MaxLines, AiInlineCompletion.Clean(long8, "", "")!.Split('\n').Length);

        Assert.Null(AiInlineCompletion.Clean("", "x", ""));
        Assert.Null(AiInlineCompletion.Clean("  \n ", "x", ""));
        Assert.Null(AiInlineCompletion.Clean("```\n```", "x", ""));
    }

    [Fact]
    public void ThePromptHoldsTheWindowAroundTheCaretAndAStrictInstruction()
    {
        var text = new string('a', 10_000) + new string('b', 3_000);
        var caret = 10_000;

        Assert.Equal(AiInlineCompletion.PrefixChars, AiInlineCompletion.PrefixOf(text, caret).Length);
        Assert.Equal(AiInlineCompletion.SuffixChars, AiInlineCompletion.SuffixOf(text, caret).Length);
        Assert.Equal("", AiInlineCompletion.PrefixOf("abc", 0));

        Assert.Contains("ONLY the code to insert", AiInlineCompletion.SystemPrompt);
        Assert.Contains("no code fences", AiInlineCompletion.SystemPrompt);

        var message = AiInlineCompletion.BuildUserMessage("/x/Program.vb", "Dim a", " = 1");

        Assert.Contains("Language: Visual Basic", message);
        Assert.Contains("File: Program.vb", message);
        Assert.Contains("<before_caret>\nDim a</before_caret>", message);
    }

    // The provider

    private static AiSettings Enabled(string model = "", string chat = "claude-sonnet-5-5") => new()
    {
        Vendor = "Claude",
        Model = chat,
        InlineEnabled = true,
        InlineModel = model
    };

    [Fact]
    public async Task SuggestionsAreOffByDefaultAndNothingIsAskedOrBuilt()
    {
        Assert.False(new AiSettings().InlineEnabled);
        Assert.Equal("", new AiSettings().InlineModel);

        var built = 0;
        var keyReads = 0;

        // Even with a vendor chosen and a key stored.
        var provider = new AiInlineSuggestionProvider(
            () => new AiSettings { Vendor = "Claude", Model = "claude-sonnet-5-5" },
            _ => { keyReads++; return "k"; },
            (_, _, _) => { built++; return new ScriptedBackend(); });

        Assert.False(provider.IsAvailable);
        Assert.Null(await provider.SuggestAsync("/x/a.vb", "Dim a", 5));
        Assert.Equal(0, built);
        Assert.Equal(0, keyReads);
    }

    [AvaloniaFact]
    public async Task TypingWithSuggestionsOffMakesNoRequest()
    {
        var built = 0;

        var provider = new AiInlineSuggestionProvider(
            () => new AiSettings { Vendor = "Claude" }, _ => "k",
            (_, _, _) => { built++; return new ScriptedBackend(ScriptedBackend.Say("x")); });

        var (window, editor, _) = Open(provider);

        editor.TypeForTests("Co");
        await QuietAsync();

        Assert.Equal(0, editor.InlineRequestsForTests);
        Assert.Equal(0, built);
        Assert.False(editor.IsSuggesting);

        window.Close();
    }

    [Fact]
    public async Task WithoutAKeyNothingIsSent()
    {
        var built = 0;

        var provider = new AiInlineSuggestionProvider(
            () => Enabled(), _ => null,
            (_, _, _) => { built++; return new ScriptedBackend(); });

        Assert.True(provider.IsAvailable);
        Assert.Null(await provider.SuggestAsync("/x/a.vb", "Dim a", 5));
        Assert.Equal(0, built);
    }

    [AvaloniaFact]
    public async Task TheRequestIsBuiltAndSentOffTheInterfaceThread()
    {
        // Asked from the interface thread, as the editor does: the key lookup,
        // the prompt and the whole request must happen elsewhere.
        Assert.True(Dispatcher.UIThread.CheckAccess());

        var onUi = new List<string>();
        var backend = new ThreadRecordingBackend(ui => { if (ui) onUi.Add("stream"); }, "Console.WriteLine()");

        var provider = new AiInlineSuggestionProvider(
            () => Enabled(),
            _ => { if (Dispatcher.UIThread.CheckAccess()) onUi.Add("key"); return "k"; },
            (_, _, _) => { if (Dispatcher.UIThread.CheckAccess()) onUi.Add("factory"); return backend; });

        var suggestion = await provider.SuggestAsync("/x/Program.vb", "Sub Main()\n    ", 15);

        Assert.Equal("Console.WriteLine()", suggestion?.Text);
        Assert.Empty(onUi);
    }

    private sealed class ThreadRecordingBackend(Action<bool> record, string answer) : IAiChatBackend
    {
        public async IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(
            AiTurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            record(Dispatcher.UIThread.CheckAccess());
            await Task.Yield();
            yield return new AiTextDelta(answer);
            yield return new AiTurnEnd(new AiMessage(AiRole.Assistant, [new AiTextPart(answer)]), AiStopReason.EndTurn);
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task TheInlineModelIsUsedAndTheChatModelIsTheDefault()
    {
        var models = new List<string>();
        var backend = new ScriptedBackend(ScriptedBackend.Say("a"), ScriptedBackend.Say("b"));

        var settings = Enabled(model: "claude-haiku-4-5");

        var provider = new AiInlineSuggestionProvider(
            () => settings, _ => "k", (_, _, model) => { models.Add(model); return backend; });

        await provider.SuggestAsync("/x/a.vb", "x", 1);

        settings = Enabled(model: "  ", chat: "claude-opus-5-5");

        await provider.SuggestAsync("/x/a.vb", "x", 1);

        Assert.Equal(["claude-haiku-4-5", "claude-opus-5-5"], models);

        // A short, brief, low-effort turn with no tools, and a strict instruction.
        var request = backend.Requests[0];

        Assert.Equal(AiEffort.Low, request.Effort);
        Assert.True(request.Brief);
        Assert.Empty(request.Tools);
        Assert.Equal(AiInlineCompletion.MaxOutputTokens, request.MaxTokens);
        Assert.Equal(AiInlineCompletion.SystemPrompt, request.StableSystem);
    }

    // What Claude is sent

    private sealed class Capture : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public List<string> Betas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            if (request.Headers.TryGetValues("anthropic-beta", out var values))
                Betas.AddRange(values.SelectMany(v => v.Split(',')));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"m\",\"type\":\"message\","
                    + "\"role\":\"assistant\",\"model\":\"x\",\"content\":[],\"stop_reason\":null,"
                    + "\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}}\n\n"
                    + "data: {\"type\":\"message_stop\"}\n\n", Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private static async Task<(JsonObject Sent, Capture Http)> InlineRequestAsync(string model)
    {
        var http = new Capture();
        using var backend = new ClaudeBackend("test-key", model, new HttpClient(http), "https://claude.test");

        var request = new AiTurnRequest(
            AiInlineCompletion.SystemPrompt, [],
            [AiMessage.User(AiInlineCompletion.BuildUserMessage("/x/a.vb", "Dim a", ""))],
            AiEffort.Low, AiInlineCompletion.MaxOutputTokens, Brief: true);

        await foreach (var _ in backend.StreamTurnAsync(request, CancellationToken.None)) { }

        return (JsonNode.Parse(http.Body!)!.AsObject(), http);
    }

    [Fact]
    public async Task ClaudeIsAskedLowEffortWithTheInlineModelAndASmallLimit()
    {
        var (sent, http) = await InlineRequestAsync("claude-opus-5-5");
        Assert.DoesNotContain("between_tools", http.Body!);

        Assert.Equal("claude-opus-5-5", sent["model"]!.GetValue<string>());

        // Thinking is always on for this model, so it is not sent at all;
        // effort is the control, and a suggestion wants the lowest.
        Assert.Equal("low", sent["output_config"]!["effort"]!.GetValue<string>());
        Assert.Null(sent["thinking"]);

        // Room for the thinking that cannot be turned off and then the answer
        // (8192), not the chat's 64000, and no rerouting to another model.
        Assert.Equal(8192, sent["max_tokens"]!.GetValue<int>());
        Assert.Null(sent["fallbacks"]);
        Assert.DoesNotContain("server-side-fallback-2026-07-01", http.Betas);
        Assert.Null(sent["tools"]);
    }

    [Fact]
    public async Task ClaudeHaikuIsSentNeitherEffortNorThinking()
    {
        var (sent, _) = await InlineRequestAsync("claude-haiku-4-5");

        Assert.Equal("claude-haiku-4-5", sent["model"]!.GetValue<string>());
        Assert.Null(sent["output_config"]);
        Assert.Null(sent["thinking"]);
    }

    [Fact]
    public async Task ClaudeSonnetGetsLowEffortAndTheLowestThinking()
    {
        var (sent, _) = await InlineRequestAsync("claude-sonnet-5-5");

        Assert.Equal("low", sent["output_config"]!["effort"]!.GetValue<string>());

        // Thinking on this model counts toward max_tokens and is on by
        // default: "between_tools" is its lowest setting ("disabled" is a 400).
        Assert.Equal("between_tools", sent["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal(AiInlineCompletion.MaxOutputTokens, sent["max_tokens"]!.GetValue<int>());
    }

    // The editor

    [AvaloniaFact]
    public async Task ASuggestionIsAskedForOnceTypingPausesAndShownAsGhostText()
    {
        var script = new Script { Answer = (_, _) => Said("Console.WriteLine()", BlankLine + 2) };
        var (window, editor, _) = Open(script);

        editor.TypeForTests("Co");

        // Not on the keystroke: only when typing pauses.
        Assert.Empty(script.Calls);

        Assert.True(await UntilAsync(() => editor.IsSuggesting), "nothing was suggested");

        // One request for the whole burst, made with all of what was typed.
        Assert.Single(script.Calls);
        Assert.Equal("/x/Program.vb", script.Calls[0].File);
        Assert.Contains("Co\n", script.Calls[0].Text);
        Assert.Equal(BlankLine + 2, script.Calls[0].Position);

        // And the document does not have it.
        Assert.DoesNotContain("WriteLine", editor.TextForTests);

        window.Close();
    }

    [AvaloniaFact]
    public void TheProductionPauseIsAboutFourHundredMilliseconds()
    {
        var options = new InlineSuggestionOptions();

        Assert.InRange(options.Pause.TotalMilliseconds, 350, 450);
        Assert.True(options.MinInterval > TimeSpan.Zero);
        Assert.True(options.MaxFileChars > 10_000);
    }

    [AvaloniaFact]
    public async Task NothingIsAskedWhileTheCaretIsInTheMiddleOfAnExpression()
    {
        var script = new Script { Answer = (_, _) => Said("x", 0) };

        var code = "Sub Main()\n    Foo(bar)\n    x + baz\nEnd Sub";
        var (window, editor, _) = Open(script, code: code);

        // Before "+ baz": code follows the caret on the line.
        editor.CaretOffsetForTests = code.IndexOf("+ baz", StringComparison.Ordinal);
        editor.TypeForTests(" ");
        await QuietAsync();

        Assert.Empty(script.Calls);

        // Before ")": a closing token is not code that is being written.
        editor.CaretOffsetForTests = code.IndexOf("bar)", StringComparison.Ordinal) + 3;
        editor.TypeForTests("z");

        Assert.True(await UntilAsync(() => script.Calls.Count == 1), "a closing token should not stop it");

        window.Close();
    }

    [AvaloniaFact]
    public async Task NothingIsAskedForASelectionASecretFileAHugeFileOrAnInactiveWindow()
    {
        var script = new Script { Answer = (_, _) => Said("x", 0) };

        // A selection
        var (window, editor, _) = Open(script);
        editor.TypeForTests("Co");
        editor.SelectForTests(BlankLine, 2);
        await QuietAsync();
        Assert.Empty(script.Calls);
        window.Close();

        // Files that may hold credentials, by the rules the chat's tools use.
        foreach (var path in new[] { "/x/.env", "/x/appsettings.Production.json", "/x/id_rsa", "/x/server.pem", "/x/.git/config" })
        {
            var (w, e, _) = Open(script, path);
            e.TypeForTests("Co");
            await QuietAsync(200);
            Assert.True(script.Calls.Count == 0, $"{path} was sent");
            w.Close();
        }

        // Over the size limit
        var (big, bigEditor, _) = Open(script);
        bigEditor.InlineOptions.MaxFileChars = 20;
        bigEditor.TypeForTests("Co");
        await QuietAsync(200);
        Assert.Empty(script.Calls);
        big.Close();

        // While the window is not the one in use
        var (idle, idleEditor, _) = Open(script);
        idleEditor.WindowActiveOverride = () => false;
        idleEditor.TypeForTests("Co");
        await QuietAsync(200);
        Assert.Empty(script.Calls);
        idle.Close();

        // And an ordinary file is asked, so the guards above are what stopped the others.
        var (ok, okEditor, _) = Open(script);
        okEditor.TypeForTests("Co");
        Assert.True(await UntilAsync(() => script.Calls.Count == 1));
        ok.Close();
    }

    [AvaloniaFact]
    public async Task AKeystrokeCancelsTheRequestAndTheNextOneWaitsForIt()
    {
        var release = new TaskCompletionSource<InlineSuggestion?>();
        CancellationToken first = default;

        var script = new Script
        {
            Answer = (call, ct) =>
            {
                if (call == 1)
                {
                    first = ct;
                    return release.Task;
                }

                return Task.FromResult<InlineSuggestion?>(null);
            }
        };

        var (window, editor, _) = Open(script);

        editor.TypeForTests("Co");
        Assert.True(await UntilAsync(() => script.Calls.Count == 1));

        editor.TypeForTests("n");

        Assert.True(first.IsCancellationRequested, "the keystroke did not cancel the request in flight");

        // The cancelled request is still unwinding: no second one may start yet.
        await QuietAsync(250);
        Assert.Single(script.Calls);

        release.SetResult(null);

        Assert.True(await UntilAsync(() => script.Calls.Count == 2));
        Assert.Equal(1, script.MostInFlight);

        window.Close();
    }

    [AvaloniaFact]
    public async Task AnAnswerAfterAnEditIsDropped()
    {
        var release = new TaskCompletionSource<InlineSuggestion?>();

        var script = new Script
        {
            // The first answer is slow and ignores its token, like a response
            // already on its way; later asks have nothing to say.
            Answer = (call, _) => call == 1 ? release.Task : Task.FromResult<InlineSuggestion?>(null)
        };

        var (window, editor, _) = Open(script);

        editor.TypeForTests("Co");
        Assert.True(await UntilAsync(() => script.Calls.Count == 1));

        // The user goes on typing; then the old answer arrives.
        editor.TypeForTests("n");
        release.SetResult(new InlineSuggestion("sole.WriteLine()", BlankLine + 2));

        await QuietAsync(300);

        Assert.False(editor.IsSuggesting, "an answer for the old text was shown");

        window.Close();
    }

    [AvaloniaFact]
    public async Task AnAnswerAfterTheCaretMovedIsDropped()
    {
        var release = new TaskCompletionSource<InlineSuggestion?>();
        var script = new Script { Answer = (_, _) => release.Task };

        var (window, editor, _) = Open(script);

        editor.TypeForTests("Co");
        Assert.True(await UntilAsync(() => script.Calls.Count == 1));

        editor.CaretOffsetForTests = 0;
        release.SetResult(new InlineSuggestion("nsole", BlankLine + 2));

        await QuietAsync(150);

        Assert.False(editor.IsSuggesting);

        window.Close();
    }

    private static async Task<(Window, CodeEditor, MainWindowViewModel, Script)> SuggestingAsync(string text)
    {
        var script = new Script { Answer = (_, _) => Said(text, BlankLine + 2) };
        var (window, editor, shell) = Open(script);

        editor.TypeForTests("Co");
        Assert.True(await UntilAsync(() => editor.IsSuggesting), "nothing was suggested");

        return (window, editor, shell, script);
    }

    [AvaloniaFact]
    public async Task TabAcceptsAMultiLineSuggestionAsOneUndoStep()
    {
        var (window, editor, _, _) = await SuggestingAsync("nsole.WriteLine(1)\n        Console.WriteLine(2)");

        var before = editor.TextForTests;

        Assert.True(editor.PressForTests(Key.Tab), "Tab did not accept");
        Assert.Contains("Console.WriteLine(1)\n        Console.WriteLine(2)", editor.TextForTests);
        Assert.False(editor.IsSuggesting);

        // One Ctrl+Z puts back everything one Tab wrote.
        editor.DocumentForTests.UndoStack.Undo();

        Assert.Equal(before, editor.TextForTests);

        window.Close();
    }

    [AvaloniaFact]
    public async Task TabIsLeftAloneWhenNothingIsShown()
    {
        // Tab is whatever it was before suggestions existed: compared with an
        // editor that has no provider at all, key for key.
        var (plainWindow, plain, _) = Open(null);
        var (window, editor, _) = Open(new Script());

        var plainHandled = plain.PressForTests(Key.Tab);
        var handled = editor.PressForTests(Key.Tab);

        Assert.Equal(plainHandled, handled);
        Assert.Equal(plain.TextForTests, editor.TextForTests);
        Assert.False(editor.IsSuggesting);

        await QuietAsync(50);
        plainWindow.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task NothingIsAskedAndTabIsNotTakenWhileAPopupOrSnippetIsOpen()
    {
        // The controller alone, with a stand-in for the completion list, a
        // snippet session or the smart-tag menu.
        var editor = new AvaloniaEdit.TextEditor { Document = new AvaloniaEdit.Document.TextDocument("Dim a") };
        var script = new Script { Answer = (_, _) => Said(" = 1", editor.CaretOffset) };
        var popup = true;

        var controller = new InlineSuggestionController(
            editor, new GhostTextGenerator(),
            () => script, () => "/x/a.vb", isBlocked: () => popup, isActive: () => true,
            isSuppressed: () => false, () => null, _ => { });

        controller.Options.Pause = TimeSpan.FromMilliseconds(20);
        controller.Options.MinInterval = TimeSpan.Zero;

        editor.CaretOffset = 5;
        editor.Document.Insert(5, " ");
        await QuietAsync(250);

        Assert.Empty(script.Calls);

        // Closed: the same edit is answered, and Tab is the suggestion's.
        popup = false;
        editor.Document.Insert(6, "=");
        Assert.True(await UntilAsync(() => controller.IsShowing));

        // Opened again with the ghost up: the popup owns Tab, and the ghost goes.
        popup = true;
        var tab = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab };

        Assert.False(controller.TryHandleKey(tab));
        Assert.False(tab.Handled);
        Assert.False(controller.IsShowing);

        controller.Dispose();
    }

    [AvaloniaFact]
    public async Task EscDismissesAndDoesNotAskAgain()
    {
        var (window, editor, _, script) = await SuggestingAsync("nsole.WriteLine(1)");

        Assert.True(editor.PressForTests(Key.Escape));
        Assert.False(editor.IsSuggesting);

        // Nothing was typed, so nothing is asked for.
        await QuietAsync(300);

        Assert.Single(script.Calls);
        Assert.False(editor.IsSuggesting);

        window.Close();
    }

    [AvaloniaFact]
    public async Task CtrlRightAcceptsOneWordAtATimeAndKeepsTheRest()
    {
        var (window, editor, _, _) = await SuggestingAsync("nsole.WriteLine(1) And more");

        Assert.True(editor.PressForTests(Key.Right, KeyModifiers.Control));

        Assert.Contains("Console.WriteLine(1)", editor.TextForTests);
        Assert.DoesNotContain("And", editor.TextForTests);
        Assert.True(editor.IsSuggesting, "the rest of the suggestion should stay");

        Assert.True(editor.PressForTests(Key.Right, KeyModifiers.Control));
        Assert.Contains("Console.WriteLine(1) And", editor.TextForTests);

        window.Close();
    }

    [AvaloniaFact]
    public async Task AnErrorIsShownOnceInTheStatusBarAndRequestsStopForAWhile()
    {
        var script = new Script { Answer = (_, _) => throw new InvalidOperationException("the key was refused") };
        var (window, editor, shell) = Open(script);

        editor.TypeForTests("Co");

        Assert.True(await UntilAsync(() => shell.StatusMessage.Contains("the key was refused")));

        shell.StatusMessage = "other news";

        // More typing: the failure is not asked for again, nor told again.
        editor.TypeForTests("n");
        await QuietAsync(300);
        editor.TypeForTests("s");
        await QuietAsync(300);

        Assert.Single(script.Calls);
        Assert.Equal("other news", shell.StatusMessage);

        window.Close();
    }

    [AvaloniaFact]
    public async Task RequestsAreSpacedByTheMinimumInterval()
    {
        var script = new Script { Answer = (_, _) => Task.FromResult<InlineSuggestion?>(null) };
        var (window, editor, _) = Open(script);

        editor.InlineOptions.MinInterval = TimeSpan.FromMilliseconds(500);

        editor.TypeForTests("C");
        Assert.True(await UntilAsync(() => script.Calls.Count == 1));

        editor.TypeForTests("o");
        Assert.True(await UntilAsync(() => script.Calls.Count == 2));

        // Pause 30 ms alone would have made the second one at once.
        Assert.True(script.Calls[1].At - script.Calls[0].At >= 450,
            $"second request came after {script.Calls[1].At - script.Calls[0].At} ms");

        window.Close();
    }

    // What is drawn

    private static Color Secondary(CodeEditor editor) =>
        editor.TryFindResource("ChromeSecondaryForeground", editor.ActualThemeVariant, out var found)
        && found is Color colour
            ? colour
            : throw new InvalidOperationException("the theme has no ChromeSecondaryForeground");

    private static int Count(Raster raster, Avalonia.PixelRect area, Color colour)
    {
        var count = 0;

        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
                if (Raster.Near(raster.At(x, y), colour, 6)) count++;

        return count;
    }

    [AvaloniaFact]
    public async Task GhostTextIsDrawnInTheThemesSecondaryColourInLightAndDark()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var script = new Script { Answer = (_, _) => Said("nsole.WriteLine(1)\nSystem.Diagnostics.Debug.Write(2)", BlankLine + 2) };
            var (window, editor, _) = Open(script);

            window.RequestedThemeVariant = variant;

            var big = new IdeSettings();
            big.Editor.FontSize = 28;
            editor.ApplySettings(big);
            window.UpdateLayout();

            var colour = Secondary(editor);
            var area = Raster.BoundsIn(editor, window);
            var before = Raster.Of(window);

            editor.TypeForTests("Co");
            Assert.True(await UntilAsync(() => editor.IsSuggesting), variant.ToString());

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var after = Raster.Of(window);

            // Searching the editor's own area, minus the margin, for the
            // colour: the text colours of the code itself are not this one.
            var text = new Avalonia.PixelRect(area.X + 80, area.Y, area.Width - 80, area.Height);

            // Counted, not just found: the line numbers and some code may be
            // near the colour already, and the suggestion is what adds to it.
            var added = Count(after, text, colour) - Count(before, text, colour);

            Assert.True(added >= 20, $"{variant}: only {added} pixels of the suggestion are {colour}");

            window.Close();
        }
    }

    // The text of the answer, continued

    [Fact]
    public void IndentationTheModelRepeatsIsNotDoubled()
    {
        // The caret on an indented, empty line.
        var prefix = "Sub Main()\n        ";

        Assert.Equal("Console.WriteLine()", AiInlineCompletion.Clean("        Console.WriteLine()", prefix, ""));
        Assert.Equal("Console.WriteLine()", AiInlineCompletion.Clean("Console.WriteLine()", prefix, ""));

        // Less than the line has: still not added to it. More: the rest is kept.
        Assert.Equal("Console.WriteLine()", AiInlineCompletion.Clean("    Console.WriteLine()", prefix, ""));
        Assert.Equal("    Console.WriteLine()", AiInlineCompletion.Clean("            Console.WriteLine()", prefix, ""));
    }

    [Fact]
    public void ControlAndBidirectionalCharactersAreRemoved()
    {
        // A right-to-left override reorders what a reader sees from what compiles.
        var hostile = "x = ‮\"admin\"‬​\u0007 + 1";

        Assert.Equal("x = \"admin\" + 1", AiInlineCompletion.Clean(hostile, "", ""));
        Assert.Equal("a\n\tb", AiInlineCompletion.Clean("a\r\n\tb", "", ""));
    }

    [Fact]
    public void ALongAnswerIsCutBetweenLinesNeverInsideOne()
    {
        var line = new string('x', 200);
        var cut = AiInlineCompletion.Clean(string.Join("\n", Enumerable.Repeat(line, 5)), "", "")!;

        Assert.True(cut.Length <= AiInlineCompletion.MaxChars);
        Assert.All(cut.Split('\n'), l => Assert.Equal(line, l));

        // A first line that alone is too long leaves nothing: half of it is not code.
        Assert.Null(AiInlineCompletion.Clean(new string('y', AiInlineCompletion.MaxChars + 50), "", ""));
    }

    [Fact]
    public void OnlyFinishedLinesSurviveACutOffAnswer()
    {
        Assert.Equal("a = 1\nb = 2", AiInlineCompletion.CompleteLinesOnly("a = 1\nb = 2\nc = 3 + fo"));
        Assert.Equal("", AiInlineCompletion.CompleteLinesOnly("Console.WriteL"));
    }

    // The provider, continued

    private static ScriptedBackend Backend(string text, AiStopReason stop) => new(
        [new AiTextDelta(text), new AiTurnEnd(new AiMessage(AiRole.Assistant, [new AiTextPart(text)]), stop)]);

    private static AiInlineSuggestionProvider ProviderOver(IAiChatBackend backend, Func<AiVendor, string?>? keys = null) =>
        new(() => Enabled(), keys ?? (_ => "k"), (_, _, _) => backend);

    [Fact]
    public async Task ARefusedOrUnknownEndingIsNotAnAnswer()
    {
        Assert.Null(await ProviderOver(Backend("x = 1", AiStopReason.Refusal)).SuggestAsync("/x/a.vb", "Dim ", 4));
        Assert.Null(await ProviderOver(Backend("x = 1", AiStopReason.Other)).SuggestAsync("/x/a.vb", "Dim ", 4));
    }

    [Fact]
    public async Task AnAnswerCutByTheLimitKeepsOnlyItsFinishedLines()
    {
        var cut = await ProviderOver(Backend("a = 1\nb = 2\nc = 3 + fo", AiStopReason.MaxTokens))
            .SuggestAsync("/x/a.vb", "Sub M()\n    ", 12);

        Assert.Equal("a = 1\nb = 2", cut?.Text);

        // Nothing finished at all: nothing to show.
        Assert.Null(await ProviderOver(Backend("Console.WriteL", AiStopReason.MaxTokens))
            .SuggestAsync("/x/a.vb", "Sub M()\n    ", 12));
    }

    private sealed class Endless : IAiChatBackend
    {
        public int Pulled { get; private set; }

        public async IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(
            AiTurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < 1000; i++)
            {
                Pulled++;
                await Task.Yield();
                yield return new AiTextDelta($"x{i} = {i}\n");
            }
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task ReadingStopsOnceThereAreEnoughLines()
    {
        var endless = new Endless();

        var suggestion = await ProviderOver(endless).SuggestAsync("/x/a.vb", "Sub M()\n    ", 12);

        Assert.Equal(AiInlineCompletion.MaxLines, suggestion!.Text.Split('\n').Length);
        Assert.True(endless.Pulled <= AiInlineCompletion.MaxLines + 2, $"{endless.Pulled} chunks were read");
    }

    [Fact]
    public async Task TheKeyAndTheConnectionAreKeptAndForgottenWhenSettingsChange()
    {
        var keyReads = 0;
        var built = 0;

        var provider = new AiInlineSuggestionProvider(
            () => Enabled(), _ => { keyReads++; return "k"; },
            (_, _, _) => { built++; return new ScriptedBackend(ScriptedBackend.Say("a"), ScriptedBackend.Say("a"), ScriptedBackend.Say("a")); });

        await provider.SuggestAsync("/x/a.vb", "x", 1);
        await provider.SuggestAsync("/x/a.vb", "x", 1);

        // One keychain lookup and one connection for the session.
        Assert.Equal(1, keyReads);
        Assert.Equal(1, built);

        provider.Invalidate();
        await provider.SuggestAsync("/x/a.vb", "x", 1);

        Assert.Equal(2, keyReads);
        Assert.Equal(2, built);
    }

    [Fact]
    public async Task AKeyRemovedWhileItWasBeingReadIsNotKept()
    {
        var keyReads = 0;
        AiInlineSuggestionProvider? provider = null;

        provider = new AiInlineSuggestionProvider(
            () => Enabled(),
            _ =>
            {
                // The user removes the key while the keychain is being read:
                // the old key comes back from this lookup, after the forget.
                if (++keyReads == 1) provider!.ForgetKey();
                return "old";
            },
            (_, _, _) => new ScriptedBackend(ScriptedBackend.Say("a"), ScriptedBackend.Say("a")));

        await provider.SuggestAsync("/x/a.vb", "x", 1);
        await provider.SuggestAsync("/x/a.vb", "x", 1);

        Assert.Equal(2, keyReads);
    }

    [Fact]
    public async Task ASettingsChangeForgetsTheKeyButKeepsTheConnection()
    {
        var built = 0;
        var disposed = 0;

        var provider = new AiInlineSuggestionProvider(
            () => Enabled(), _ => "k",
            (_, _, _) =>
            {
                built++;
                return new DisposeCounting(() => disposed++);
            });

        await provider.SuggestAsync("/x/a.vb", "x", 1);

        // Every settings load announces itself, opening a file included: the
        // connection a request may be streaming on must survive it.
        provider.ForgetKey();
        await provider.SuggestAsync("/x/a.vb", "x", 1);

        Assert.Equal(1, built);
        Assert.Equal(0, disposed);
    }

    private sealed class DisposeCounting(Action onDispose) : IAiChatBackend
    {
        private readonly ScriptedBackend _inner = new(ScriptedBackend.Say("a"), ScriptedBackend.Say("a"));

        public IAsyncEnumerable<AiTurnEvent> StreamTurnAsync(AiTurnRequest request, CancellationToken ct) =>
            _inner.StreamTurnAsync(request, ct);

        public void Dispose() => onDispose();
    }

    [Fact]
    public async Task ALinkToASecretFileIsNotSentWhateverItIsCalled()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a link needs privileges on Windows.");

        var folder = Directory.CreateTempSubdirectory("basalt-inline-").FullName;

        try
        {
            var secret = Path.Combine(folder, ".env");
            var link = Path.Combine(folder, "Program.vb");

            File.WriteAllText(secret, "KEY=1");
            File.CreateSymbolicLink(link, secret);

            var keyReads = 0;
            var built = 0;

            var provider = new AiInlineSuggestionProvider(
                () => Enabled(), _ => { keyReads++; return "k"; },
                (_, _, _) => { built++; return new ScriptedBackend(ScriptedBackend.Say("a")); });

            Assert.Null(await provider.SuggestAsync(link, "KEY=1\n", 6));
            Assert.Equal(0, keyReads);
            Assert.Equal(0, built);

            // And the same name that is not a link is an ordinary file.
            File.Delete(link);
            File.WriteAllText(link, "Dim a");

            Assert.NotNull(await provider.SuggestAsync(link, "Dim a", 5));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The editor, continued

    [AvaloniaFact]
    public async Task AnAnswerAfterCancellationAloneIsDropped()
    {
        // Nothing was typed and the caret did not move: only the request was
        // cancelled, which is all that tells this answer it is not wanted.
        var release = new TaskCompletionSource<InlineSuggestion?>();
        var script = new Script { Answer = (_, _) => release.Task };

        var (window, editor, _) = Open(script);

        editor.TypeForTests("Co");
        Assert.True(await UntilAsync(() => script.Calls.Count == 1));

        editor.DismissSuggestion();
        release.SetResult(new InlineSuggestion("nsole", BlankLine + 2));

        await QuietAsync(150);

        Assert.False(editor.IsSuggesting);

        window.Close();
    }

    [AvaloniaFact]
    public async Task EscStopsARequestThatHasNotAnsweredAndIsNotConsumedForIt()
    {
        var editor = new AvaloniaEdit.TextEditor { Document = new AvaloniaEdit.Document.TextDocument("Dim a") };
        CancellationToken token = default;

        var script = new Script
        {
            Answer = (_, ct) => { token = ct; return Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => (InlineSuggestion?)null, TaskScheduler.Default); }
        };

        var controller = new InlineSuggestionController(
            editor, new GhostTextGenerator(), () => script, () => "/x/a.vb", () => false, () => true,
            () => false, () => null, _ => { });

        controller.Options.Pause = TimeSpan.FromMilliseconds(20);
        controller.Options.MinInterval = TimeSpan.Zero;

        editor.CaretOffset = 5;
        editor.Document.Insert(5, " ");
        Assert.True(await UntilAsync(() => script.Calls.Count == 1));
        Assert.True(controller.IsPending);

        var esc = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };

        // Not ours to take: the editor's own Escape still happens.
        Assert.False(controller.TryHandleKey(esc));
        Assert.False(esc.Handled);
        Assert.True(token.IsCancellationRequested);

        await QuietAsync(150);
        Assert.Single(script.Calls);

        controller.Dispose();
    }

    [AvaloniaFact]
    public async Task ASelectionKeepsTabForIndentingEvenWithTheCaretWhereItWas()
    {
        var (window, editor, _, _) = await SuggestingAsync("nsole.WriteLine(1)");

        // The selection ends at the caret, so the caret does not move.
        editor.SelectForTests(BlankLine, 2);
        Assert.Equal(BlankLine + 2, editor.CaretOffsetForTests);

        editor.PressForTests(Key.Tab);

        Assert.DoesNotContain("WriteLine", editor.TextForTests);

        window.Close();
    }

    [AvaloniaFact]
    public async Task MoreThanOneLineShowsTheFirstWithANoteAndNeverDrawsOverTheCode()
    {
        var script = new Script { Answer = (_, _) => Said("nsole.WriteLine(1)\n        Console.WriteLine(2)\n        Console.WriteLine(3)", BlankLine + 2) };
        var (window, editor, _) = Open(script);

        var before = Raster.Of(window);

        editor.TypeForTests("Co");
        Assert.True(await UntilAsync(() => editor.IsSuggesting));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // The note says how much more there is.
        Assert.EndsWith(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.InlineMoreLines, 2),
            editor.GhostTextForTests);
        Assert.StartsWith("nsole.WriteLine(1)", editor.GhostTextForTests);

        var after = Raster.Of(window);

        // Only the caret's own line changed (the typed letters and the ghost);
        // the lines below, which a drawn continuation would have covered, did not.
        int? top = null, bottom = null;

        for (var y = 0; y < (int)window.Bounds.Height; y++)
        {
            for (var x = 0; x < (int)window.Bounds.Width; x++)
            {
                if (before.At(x, y) == after.At(x, y)) continue;

                top ??= y;
                bottom = y;
                break;
            }
        }

        Assert.NotNull(top);
        Assert.InRange(bottom!.Value - top!.Value, 0, 24);

        // Tab writes all three lines.
        Assert.True(editor.PressForTests(Key.Tab));
        Assert.Contains("Console.WriteLine(1)\n        Console.WriteLine(2)\n        Console.WriteLine(3)", editor.TextForTests);

        window.Close();
    }

    [AvaloniaFact]
    public async Task AcceptingWritesTheDocumentsOwnLineEndingsNeverAMix()
    {
        foreach (var newline in new[] { "\r\n", "\n" })
        {
            var script = new Script { Answer = (_, _) => Said("nsole.WriteLine(1)\n        Console.WriteLine(2)", Code.Replace("\n", newline).IndexOf(newline + newline, StringComparison.Ordinal) + newline.Length + 2) };
            var code = Code.Replace("\n", newline);
            var (window, editor, _) = Open(script, code: code);

            editor.CaretOffsetForTests = code.IndexOf(newline + newline, StringComparison.Ordinal) + newline.Length;
            editor.TypeForTests("Co");
            Assert.True(await UntilAsync(() => editor.IsSuggesting));

            Assert.True(editor.PressForTests(Key.Tab));

            var text = editor.TextForTests;

            Assert.Contains("Console.WriteLine(1)" + newline + "        Console.WriteLine(2)", text);

            // Every line ending is the same one.
            Assert.Equal(newline == "\r\n" ? 0 : text.Count(c => c == '\n'), Regex.Matches(text, "(?<!\r)\n").Count);
            Assert.Equal(newline == "\r\n" ? text.Count(c => c == '\n') : 0, text.Count(c => c == '\r'));

            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AutoClosedBracketsAndOvertypedClosersStillEarnASuggestion()
    {
        var script = new Script { Answer = (_, _) => Task.FromResult<InlineSuggestion?>(null) };
        var code = "Sub Main()\n    ";
        var (window, editor, _) = Open(script, code: code);

        Assert.True(editor.AutoCloseBrackets);

        // The editor writes the ")" itself and puts the caret back inside.
        editor.TypeForTests("Foo(");
        Assert.EndsWith("Foo()", editor.TextForTests);

        Assert.True(await UntilAsync(() => script.Calls.Count == 1), "no request after an auto-closed bracket");
        Assert.Equal(editor.TextForTests.Length - 1, script.Calls[0].Position);

        // Typing the closer steps over it: no edit, only the caret moves.
        editor.TypeForTests(")");
        Assert.True(await UntilAsync(() => script.Calls.Count == 2), "no request after overtyping a closer");
        Assert.Equal(editor.TextForTests.Length, script.Calls[1].Position);

        window.Close();
    }

    [AvaloniaFact]
    public async Task EnterInVisualBasicEarnsASuggestionOnTheFreshLine()
    {
        var script = new Script { Answer = (_, _) => Task.FromResult<InlineSuggestion?>(null) };
        var code = "Module Program\n    Sub Main()\n        Console.WriteLine(1)\n    End Sub\nEnd Module";
        var (window, editor, _) = Open(script, code: code);

        editor.CaretOffsetForTests = code.IndexOf("(1)", StringComparison.Ordinal) + 3;

        await editor.HandleEnterForTestsAsync();

        Assert.True(await UntilAsync(() => script.Calls.Count >= 1), "no request after Enter");
        Assert.Equal(editor.CaretOffsetForTests, script.Calls[^1].Position);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ABrokenProviderIsQuietForEveryEditorSharingIt()
    {
        var script = new Script { Answer = (_, _) => throw new InvalidOperationException("quota exceeded") };

        var (windowA, a, shellA) = Open(script);
        var (windowB, b, shellB) = Open(script, "/x/Other.vb");

        a.TypeForTests("Co");
        Assert.True(await UntilAsync(() => shellA.StatusMessage.Contains("quota exceeded")));

        // Another file, same provider: not asked, and not told again.
        b.TypeForTests("Co");
        await QuietAsync(300);

        Assert.Single(script.Calls);
        Assert.DoesNotContain("quota exceeded", shellB.StatusMessage);

        windowA.Close();
        windowB.Close();
    }

    [AvaloniaFact]
    public async Task AcceptedMultiLineTextStaysOneUndoStepEvenAfterTheLineIsLeft()
    {
        // Unformatted on purpose: leaving the line formats it, and that used
        // to be a step of its own that one Ctrl+Z did not take back.
        var (window, editor, _, _) = await SuggestingAsync("nsole.WriteLine( 1 )\n        Console.WriteLine( 2 )");

        var before = editor.TextForTests;

        Assert.True(editor.PressForTests(Key.Tab));

        // Long enough for any format-on-leaving-the-line to have run.
        await QuietAsync(1500);

        editor.DocumentForTests.UndoStack.Undo();

        Assert.Equal(before, editor.TextForTests);

        window.Close();
    }
}

/// <summary>The popup check against a real completion list, as the keyboard tests open one.</summary>
public sealed class InlineWithCompletionListTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-inline-completion-").FullName;
    private MainWindowViewModel _shell = null!;

    public async ValueTask InitializeAsync()
    {
        var project = Path.Combine(_root, "Typing.vbproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(_root, "Typing.vb"), Source);
        _shell = new MainWindowViewModel();
        await _shell.OpenSolutionAsync(project);
    }

    private const string Source =
        "Module Program\n    Sub CompleteTarget()\n    End Sub\n    Sub Main()\n        CompleteTar\n    End Sub\nEnd Module\n";

    [AvaloniaFact]
    public async Task NothingIsAskedWhileTheCompletionListIsOpen()
    {
        var calls = 0;

        var script = new InlineAiTestsProvider(() => calls++);
        var code = new CodeEditor(new EditorDocumentViewModel(Path.Combine(_root, "Typing.vb"), Source), _shell)
        {
            AutoFormatWhileTyping = false,
            SuggestionProvider = script,
            WindowActiveOverride = () => true
        };

        code.InlineOptions.Pause = TimeSpan.FromMilliseconds(30);
        code.InlineOptions.MinInterval = TimeSpan.Zero;

        // Completion on, as it is for a person: the list is what blocks.
        var settings = new Basalt.Core.Settings.IdeSettings();
        settings.Editor.CompleteAutomatically = true;
        code.ApplySettings(settings);

        var window = new Window { Content = code, Width = 700, Height = 500 };
        window.Show();
        window.UpdateLayout();

        code.CaretOffsetForTests = Source.IndexOf("CompleteTar\n", StringComparison.Ordinal) + "CompleteTar".Length;

        await code.ShowCompletionForTestsAsync();
        Assert.True(code.CompletionOpenForTests, "the completion list did not open");

        // An edit, with the list open (it filters itself, and stays).
        code.TypeForTests("g");
        await Task.Delay(400);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(code.CompletionOpenForTests);
        Assert.Equal(0, calls);
        Assert.False(code.IsSuggesting);

        window.Close();
    }

    public ValueTask DisposeAsync()
    {
        _shell.Dispose();

        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }

        return ValueTask.CompletedTask;
    }
}

internal sealed class InlineAiTestsProvider(Action onCall) : IInlineSuggestionProvider
{
    public bool IsAvailable => true;

    public Task<InlineSuggestion?> SuggestAsync(
        string filePath, string text, int position, CancellationToken ct = default)
    {
        onCall();
        return Task.FromResult<InlineSuggestion?>(new InlineSuggestion("x", position));
    }
}
