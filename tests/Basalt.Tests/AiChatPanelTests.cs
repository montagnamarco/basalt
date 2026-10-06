using Avalonia.Headless.XUnit;
using Microsoft.Extensions.AI;
using Basalt.Core.Services;
using Basalt.Shell.Controls;
using Basalt.Workspace.Ai;

namespace Basalt.Tests;

/// <summary>Splitting a reply into prose and code.</summary>
public class MarkdownBlockTests
{
    [Fact]
    public void KeepsProseAsProse()
    {
        var blocks = MarkdownBlocks.Split("This declares a variable.");

        Assert.Single(blocks);
        Assert.False(blocks[0].IsCode);
    }

    [Fact]
    public void RecognisesAFencedBlockAsCode()
    {
        const string reply = """
            Use Dim:

            ```vb
            Dim total As Integer = 0
            ```

            That declares it.
            """;

        var blocks = MarkdownBlocks.Split(reply);

        Assert.Equal(3, blocks.Count);
        Assert.True(blocks[1].IsCode);
        Assert.Equal("Dim total As Integer = 0", blocks[1].Text);
    }

    [Fact]
    public void LeavesTheFenceOutOfTheCode()
    {
        // The fence is Markdown, not something to paste into a file.
        var code = MarkdownBlocks.CodeIn("```vb\nDim x = 1\n```");

        Assert.Equal("Dim x = 1", code);
    }

    [Fact]
    public void JoinsSeveralCodeBlocks()
    {
        const string reply = """
            First:
            ```
            Dim a = 1
            ```
            Then:
            ```
            Dim b = 2
            ```
            """;

        var code = MarkdownBlocks.CodeIn(reply);

        Assert.Contains("Dim a = 1", code);
        Assert.Contains("Dim b = 2", code);
    }

    [Fact]
    public void FindsNoCodeInAReplyWithNone()
    {
        Assert.Equal("", MarkdownBlocks.CodeIn("Just an explanation."));
    }

    [Fact]
    public void CopesWithAnUnclosedFence()
    {
        // A streamed reply is unclosed until the last moment.
        var blocks = MarkdownBlocks.Split("Here:\n```vb\nDim x = 1");

        Assert.Contains(blocks, b => b.IsCode && b.Text.Contains("Dim x = 1"));
    }
}

/// <summary>What the assistant is told about the code.</summary>
public class AiContextTests
{
    [Fact]
    public void SaysWhichFileAndLanguage()
    {
        var described = new AiContext
        {
            FilePath = "/src/Program.vb",
            Language = "Visual Basic"
        }.Describe();

        Assert.Contains("Program.vb", described);
        Assert.Contains("Visual Basic", described);
    }

    [Fact]
    public void SendsTheSelectionWithItsLines()
    {
        var described = new AiContext
        {
            SelectedText = "Dim x = 1",
            SelectionStartLine = 4,
            SelectionEndLine = 4
        }.Describe();

        Assert.Contains("Dim x = 1", described);
        Assert.Contains("lines 4-4", described);
    }

    [Fact]
    public void DoesNotSendTheWholeFileOrCutItOff()
    {
        // The assistant reads files with its own tools; the 8000-character
        // cut-off that used to apply to the one file sent is gone with it.
        Assert.Null(typeof(AiContext).GetProperty("FileText"));
    }

    [Fact]
    public void CutsAnEnormousSelectionAndSaysWhere()
    {
        var described = new AiContext { SelectedText = new string('x', 100000) }.Describe();

        Assert.Contains("selection cut here", described);
        Assert.True(described.Length < 40000);
    }

    [Fact]
    public void ListsTheCurrentErrors()
    {
        var described = new AiContext
        {
            Errors = ["BC30002: 'Foo' is not defined."]
        }.Describe();

        Assert.Contains("BC30002", described);
    }

    [Fact]
    public void SaysNothingWhenThereIsNothingToSay()
    {
        Assert.Equal("", new AiContext().Describe());
    }
}

/// <summary>The chat panel.</summary>
public class AiChatPanelTests
{
    [AvaloniaFact]
    public void StartsEmptyAndSaysNoAssistantIsSet()
    {
        var panel = new AiChatPanel();

        Assert.Empty(panel.Entries);
        Assert.Contains("Settings", panel.StatusText);
    }

    [AvaloniaFact]
    public void RecordsWhatWasSaid()
    {
        var panel = new AiChatPanel();

        panel.Append(ChatRole.User, "What does Dim do?");
        panel.Append(ChatRole.Assistant, "It declares a variable.");

        Assert.Equal(2, panel.Entries.Count);
        Assert.True(panel.Entries[0].IsUser);
        Assert.False(panel.Entries[1].IsUser);
    }

    [AvaloniaFact]
    public void GrowsTheReplyAsItArrives()
    {
        // A panel that sits blank for seconds looks broken.
        var panel = new AiChatPanel();

        panel.Append(ChatRole.Assistant, "");
        panel.AppendToLast("It ");
        panel.AppendToLast("declares.");

        Assert.Equal("It declares.", panel.Entries[^1].Text);
    }

    [AvaloniaFact]
    public void AsksWhatTheUserTyped()
    {
        var panel = new AiChatPanel();

        AiRequest? asked = null;
        panel.Asked += (_, request) => asked = request;

        panel.InputText = "What does Dim do?";
        panel.RequestSend();

        Assert.Equal("What does Dim do?", asked!.Question);
    }

    [AvaloniaFact]
    public void TheSendButtonSaysStopOnlyWhileAReplyIsArriving()
    {
        var panel = new AiChatPanel();
        var send = panel.SendButton;

        void AssertSend()
        {
            Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.AiSend), send.Content);
            Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.AiSendTip),
                Avalonia.Controls.ToolTip.GetTip(send));
        }

        AssertSend();

        using var running = new CancellationTokenSource();
        panel.BeginReply(running);

        Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.AiStop), send.Content);
        Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.AiStopTip),
            Avalonia.Controls.ToolTip.GetTip(send));
        Assert.NotEqual(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.AiSend), send.Content);

        panel.EndReply(running);
        AssertSend();

        // Stopping from the button goes back to Send once the reply has
        // really stopped, not before.
        panel.BeginReply(running);
        panel.Cancel();
        panel.EndReply(running);
        AssertSend();
    }

    [AvaloniaFact]
    public void KeepsSendUnavailableUntilAStoppedReplyHasReallyStopped()
    {
        // Send came back the moment Stop was pressed, so a new question could
        // start a second loop on the same conversation while the first was
        // still winding down.
        var panel = new AiChatPanel();
        using var old = new CancellationTokenSource();

        panel.BeginReply(old);
        panel.Cancel();

        Assert.True(old.IsCancellationRequested);
        Assert.False(panel.SendButton.IsEnabled);

        var asked = 0;
        panel.Asked += (_, _) => asked++;
        panel.InputText = "Another question";
        panel.RequestSend();

        Assert.Equal(0, asked);
        Assert.True(panel.IsReplying);

        panel.EndReply(old);

        Assert.True(panel.SendButton.IsEnabled);
        Assert.False(panel.IsReplying);
    }

    [AvaloniaFact]
    public void AnOldReplyFinishingLateDoesNotEndANewerOne()
    {
        var panel = new AiChatPanel();
        using var old = new CancellationTokenSource();
        using var current = new CancellationTokenSource();

        panel.BeginReply(old);
        panel.BeginReply(current);
        panel.EndReply(old);

        Assert.True(panel.IsReplying);

        panel.EndReply(current);

        Assert.False(panel.IsReplying);
    }

    [AvaloniaFact]
    public void EmptiesTheBoxOnceTheQuestionIsAsked()
    {
        var panel = new AiChatPanel();

        panel.InputText = "A question";
        panel.RequestSend();

        Assert.Equal("", panel.InputText);
    }

    [AvaloniaFact]
    public void AsksNothingWhenTheBoxIsEmpty()
    {
        var panel = new AiChatPanel();

        var asked = 0;
        panel.Asked += (_, _) => asked++;

        panel.RequestSend();

        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public void ForgetsTheConversationWhenCleared()
    {
        var panel = new AiChatPanel();
        panel.Append(ChatRole.User, "Something");

        panel.Clear();

        Assert.Empty(panel.Entries);
    }

    [AvaloniaFact]
    public void OffersToApplyACodeBlock()
    {
        var panel = new AiChatPanel();

        string? applied = null;
        panel.ApplyRequested += (_, code) => applied = code;

        panel.RequestApply("Dim x = 1");

        Assert.Equal("Dim x = 1", applied);
    }
}

/// <summary>The conversation, and what the assistant is asked.</summary>
public class AiConversationTests
{
    [Fact]
    public void OffersTheThreeAssistants()
    {
        Assert.Equal(3, AiConversation.Providers.Count);

        Assert.NotNull(AiConversation.ProviderFor(AiVendor.Claude));
        Assert.NotNull(AiConversation.ProviderFor(AiVendor.Gemini));
        Assert.NotNull(AiConversation.ProviderFor(AiVendor.ChatGpt));
    }

    [Fact]
    public void TellsTheAssistantThisIsAVisualBasicIde()
    {
        // Without it the answers come back in C#, which is what almost every
        // example on the internet is written in.
        Assert.Contains("Visual Basic", AiConversation.SystemPrompt);
    }

    [Theory]
    [InlineData("Explain", "Explain what this code does")]
    [InlineData("Fix the error", "Fix the errors")]
    [InlineData("Generate tests", "xunit tests in Visual Basic")]
    [InlineData("Document", "XML documentation")]
    public void TurnsAChosenActionIntoAnInstruction(string action, string expected)
    {
        // The user picks an action and types nothing; the assistant needs a
        // sentence.
        Assert.Contains(expected, AiConversation.InstructionFor(action, ""));
    }

    [Fact]
    public void KeepsWhatTheUserTypedBesideTheInstruction()
    {
        var instruction = AiConversation.InstructionFor("Explain", "especially the loop");

        Assert.Contains("Explain what this code does", instruction);
        Assert.Contains("especially the loop", instruction);
    }

    [Fact]
    public void AsksPlainlyWhenNoActionIsChosen()
    {
        Assert.Equal("What does Dim do?", AiConversation.InstructionFor("Ask", "What does Dim do?"));
    }

    [Fact]
    public async Task RemembersWhatWasSaidBefore()
    {
        // An assistant answers better for having seen the conversation.
        var conversation = new AiConversation();
        var backend = new ScriptedBackend(ScriptedBackend.Say("It declares a variable."));

        await DrainAsync(conversation.AskAsync(backend, "What does Dim do?"));

        Assert.Equal(2, conversation.History.Count);
        Assert.Equal(AiRole.User, conversation.History[0].Role);
        Assert.Equal(AiRole.Assistant, conversation.History[1].Role);
    }

    [Fact]
    public async Task PutsTheContextInTheNewestUserMessageNotInTheSystemText()
    {
        // The system text and the tools never vary; what does (the solution,
        // the active file) rides in the message, and stays there unchanged,
        // so every later request begins with exactly what the last one sent.
        var conversation = new AiConversation();
        var backend = new ScriptedBackend(ScriptedBackend.Say("Fine."), ScriptedBackend.Say("Again."));

        await DrainAsync(conversation.AskAsync(backend, "Why?",
            new AiPromptContext("Solution: App.sln", "Active file: Program.vb")));
        await DrainAsync(conversation.AskAsync(backend, "And?",
            new AiPromptContext("Solution: Changed.sln", "Active file: Other.vb")));

        Assert.Equal(AiConversation.SystemPrompt, backend.Requests[0].StableSystem);
        Assert.DoesNotContain("Program.vb", backend.Requests[0].StableSystem);

        var first = backend.Requests[0].Messages[0];
        Assert.Contains("Solution: App.sln", first.Text);
        Assert.Contains("Active file: Program.vb", first.Text);
        Assert.True(first.Parts.OfType<AiTextPart>().First().CacheBreakpoint);

        // The second request repeats the first message untouched, and the
        // solution is not described again.
        Assert.Same(first, backend.Requests[1].Messages[0]);
        Assert.DoesNotContain("Changed.sln", backend.Requests[1].Messages[2].Text);
        Assert.Contains("Active file: Other.vb", backend.Requests[1].Messages[2].Text);
    }

    [Fact]
    public async Task TellsTheAssistantItsJobEveryTime()
    {
        var conversation = new AiConversation();
        var backend = new ScriptedBackend(ScriptedBackend.Say("Fine."));

        await DrainAsync(conversation.AskAsync(backend, "A question"));

        Assert.Equal(AiConversation.SystemPrompt, backend.Requests[0].StableSystem);
    }

    [Fact]
    public async Task YieldsTheReplyAsItArrives()
    {
        var conversation = new AiConversation();
        var backend = new ScriptedBackend(
        [
            new AiTextDelta("It "), new AiTextDelta("declares "), new AiTextDelta("a variable."),
            new AiTurnEnd(new AiMessage(AiRole.Assistant, [new AiTextPart("It declares a variable.")]),
                AiStopReason.EndTurn)
        ]);

        var pieces = new List<string>();

        await foreach (var update in conversation.AskAsync(backend, "What does Dim do?"))
            if (update is AiTextChunk chunk) pieces.Add(chunk.Text);

        Assert.Equal(3, pieces.Count);
        Assert.Equal("It declares a variable.", string.Concat(pieces));
    }

    [Fact]
    public async Task ForgetsTheConversationWhenCleared()
    {
        var conversation = new AiConversation();

        await DrainAsync(conversation.AskAsync(new ScriptedBackend(ScriptedBackend.Say("Fine.")), "A question"));

        conversation.Clear();

        Assert.Empty(conversation.History);
    }

    private static async Task DrainAsync(IAsyncEnumerable<AiChatEvent> stream)
    {
        await foreach (var _ in stream) { }
    }
}
