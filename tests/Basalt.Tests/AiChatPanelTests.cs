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
    public void PrefersTheSelectionToTheWholeFile()
    {
        // The selection is what the user is asking about, and a whole file
        // would crowd out the question.
        var described = new AiContext
        {
            SelectedText = "Dim x = 1",
            FileText = "the entire file"
        }.Describe();

        Assert.Contains("Dim x = 1", described);
        Assert.DoesNotContain("the entire file", described);
    }

    [Fact]
    public void UsesTheFileWhenNothingIsSelected()
    {
        var described = new AiContext { FileText = "Module A\nEnd Module" }.Describe();

        Assert.Contains("Module A", described);
    }

    [Fact]
    public void ShortensAVeryLongFile()
    {
        var described = new AiContext { FileText = new string('x', 20000) }.Describe();

        Assert.Contains("truncated", described);
        Assert.True(described.Length < 12000);
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
        var client = new StubChatClient("It declares a variable.");

        await DrainAsync(conversation.AskAsync(client, "What does Dim do?"));

        Assert.Equal(2, conversation.History.Count);
        Assert.Equal(ChatRole.User, conversation.History[0].Role);
        Assert.Equal(ChatRole.Assistant, conversation.History[1].Role);
    }

    [Fact]
    public async Task SendsTheContextWithTheQuestionButDoesNotRememberIt()
    {
        // The file changes as the user edits; a copy from three turns ago
        // would mislead.
        var conversation = new AiConversation();
        var client = new StubChatClient("Fine.");

        await DrainAsync(conversation.AskAsync(client, "Why?", context: "File: Program.vb"));

        Assert.Contains("File: Program.vb", client.LastPrompt);
        Assert.DoesNotContain("File: Program.vb", conversation.History[0].Text);
    }

    [Fact]
    public async Task TellsTheAssistantItsJobEveryTime()
    {
        var conversation = new AiConversation();
        var client = new StubChatClient("Fine.");

        await DrainAsync(conversation.AskAsync(client, "A question"));

        Assert.Equal(ChatRole.System, client.LastMessages[0].Role);
    }

    [Fact]
    public async Task YieldsTheReplyAsItArrives()
    {
        var conversation = new AiConversation();
        var client = new StubChatClient("It ", "declares ", "a variable.");

        var pieces = new List<string>();

        await foreach (var piece in conversation.AskAsync(client, "What does Dim do?"))
            pieces.Add(piece);

        Assert.Equal(3, pieces.Count);
        Assert.Equal("It declares a variable.", string.Concat(pieces));
    }

    [Fact]
    public async Task ForgetsTheConversationWhenCleared()
    {
        var conversation = new AiConversation();

        await DrainAsync(conversation.AskAsync(new StubChatClient("Fine."), "A question"));

        conversation.Clear();

        Assert.Empty(conversation.History);
    }

    private static async Task DrainAsync(IAsyncEnumerable<string> stream)
    {
        await foreach (var _ in stream) { }
    }

    /// <summary>An assistant that answers with what it was given.</summary>
    private sealed class StubChatClient : IChatClient
    {
        private readonly string[] _pieces;

        public StubChatClient(params string[] pieces) => _pieces = pieces;

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public string LastPrompt => LastMessages.LastOrDefault()?.Text ?? "";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = [.. messages];

            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(_pieces))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            LastMessages = [.. messages];

            foreach (var piece in _pieces)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, piece);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
