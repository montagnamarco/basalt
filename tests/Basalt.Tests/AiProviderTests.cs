using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Basalt.Core.Services;
using Basalt.Workspace.Ai;

namespace Basalt.Tests;

/// <summary>
/// Talking to the three assistants.
///
/// The requests and the reading of replies are checked without a network:
/// what differs between the three is the shape of the JSON, and that is
/// exactly what these describe.
/// </summary>
public class AiProviderTests
{
    private static IReadOnlyList<ChatMessage> Conversation =>
    [
        new(ChatRole.System, "You help with Visual Basic."),
        new(ChatRole.User, "What does Dim do?"),
        new(ChatRole.Assistant, "It declares a variable."),
        new(ChatRole.User, "And Redim?")
    ];

    // Claude

    [Fact]
    public void ClaudeSendsTheSystemPromptAsItsOwnField()
    {
        // Anthropic takes the system prompt outside the messages; sending it
        // as a message is rejected.
        var request = new ClaudeChatClient("k", "claude-opus-4-5")
            .BuildRequest(Conversation, null);

        Assert.Equal("You help with Visual Basic.", request["system"]!.GetValue<string>());

        var messages = (JsonArray)request["messages"]!;

        Assert.Equal(3, messages.Count);
        Assert.DoesNotContain(messages, m => m!["role"]!.GetValue<string>() == "system");
    }

    [Fact]
    public void ClaudeAlwaysAsksForAnAnswerLength()
    {
        // The API refuses a request without max_tokens.
        var request = new ClaudeChatClient("k", "claude-opus-4-5")
            .BuildRequest(Conversation, null);

        Assert.NotNull(request["max_tokens"]);
    }

    [Fact]
    public void ClaudeAsksForAStream()
    {
        var request = new ClaudeChatClient("k", "claude-opus-4-5")
            .BuildRequest(Conversation, null);

        Assert.True(request["stream"]!.GetValue<bool>());
    }

    [Fact]
    public void ClaudeReadsTheTextOutOfADelta()
    {
        const string payload = """
            {"type":"content_block_delta","delta":{"type":"text_delta","text":"Hello"}}
            """;

        Assert.Equal("Hello", ClaudeChatClient.TextOf(payload));
    }

    [Fact]
    public void ClaudeIgnoresEventsThatCarryNoText()
    {
        Assert.Equal("", ClaudeChatClient.TextOf("""{"type":"message_start"}"""));
        Assert.Equal("", ClaudeChatClient.TextOf("not json at all"));
    }

    [Fact]
    public async Task ClaudeReadsAWholeStream()
    {
        const string stream = """
            event: message_start
            data: {"type":"message_start"}

            data: {"delta":{"text":"Hello, "}}

            data: {"delta":{"text":"world"}}

            data: [DONE]
            """;

        var pieces = new List<string>();

        await foreach (var text in ClaudeChatClient.ReadEventsAsync(new StringReader(stream)))
            pieces.Add(text);

        Assert.Equal("Hello, world", string.Concat(pieces));
    }

    // ChatGPT

    [Fact]
    public void OpenAiSendsTheSystemPromptAsAMessage()
    {
        // Unlike Anthropic: the same idea, a different shape.
        var request = new OpenAiChatClient("k", "gpt-5").BuildRequest(Conversation, null);

        var messages = (JsonArray)request["messages"]!;

        Assert.Equal(4, messages.Count);
        Assert.Contains(messages, m => m!["role"]!.GetValue<string>() == "system");
    }

    [Fact]
    public void OpenAiReadsTheTextOutOfADelta()
    {
        const string payload = """
            {"choices":[{"delta":{"content":"Hello"}}]}
            """;

        Assert.Equal("Hello", OpenAiChatClient.TextOf(payload));
    }

    [Fact]
    public void OpenAiIgnoresAnEmptyDelta()
    {
        // The first event of a stream carries the role and no content.
        Assert.Equal("", OpenAiChatClient.TextOf("""{"choices":[{"delta":{"role":"assistant"}}]}"""));
    }

    // Gemini

    [Fact]
    public void GeminiCallsTheAssistantModelRatherThanAssistant()
    {
        var request = new GeminiChatClient("k", "gemini-2.5-pro").BuildRequest(Conversation, null);

        var contents = (JsonArray)request["contents"]!;

        Assert.Contains(contents, c => c!["role"]!.GetValue<string>() == "model");
        Assert.DoesNotContain(contents, c => c!["role"]!.GetValue<string>() == "assistant");
    }

    [Fact]
    public void GeminiSendsTheSystemPromptAsAnInstruction()
    {
        var request = new GeminiChatClient("k", "gemini-2.5-pro").BuildRequest(Conversation, null);

        Assert.NotNull(request["systemInstruction"]);
    }

    [Fact]
    public void GeminiPutsTheModelAndTheKeyInTheAddress()
    {
        // Not in the body and not in a header, unlike the other two.
        var endpoint = new GeminiChatClient("secret", "gemini-2.5-pro")
            .EndpointFor("gemini-2.5-flash");

        Assert.Contains("models/gemini-2.5-flash", endpoint);
        Assert.Contains("key=secret", endpoint);
        Assert.Contains("alt=sse", endpoint);
    }

    [Fact]
    public void GeminiReadsTheTextOutOfACandidate()
    {
        const string payload = """
            {"candidates":[{"content":{"parts":[{"text":"Hello"}]}}]}
            """;

        Assert.Equal("Hello", GeminiChatClient.TextOf(payload));
    }

    // What every provider must do

    [Theory]
    [InlineData(typeof(ClaudeProvider))]
    [InlineData(typeof(OpenAiProvider))]
    [InlineData(typeof(GeminiProvider))]
    public void EveryProviderOffersModelsAndSaysWhereToGetAKey(Type providerType)
    {
        var provider = (IAiProvider)Activator.CreateInstance(providerType)!;

        Assert.NotEmpty(provider.DisplayName);
        Assert.NotEmpty(provider.Models);
        Assert.StartsWith("https://", provider.ApiKeyUrl);
    }

    [Theory]
    [InlineData(typeof(ClaudeProvider))]
    [InlineData(typeof(OpenAiProvider))]
    [InlineData(typeof(GeminiProvider))]
    public void EveryProviderMakesAClientForTheSharedInterface(Type providerType)
    {
        // The panel talks to IChatClient and does not know which assistant it
        // is talking to.
        var provider = (IAiProvider)Activator.CreateInstance(providerType)!;

        using var client = provider.CreateClient("key", provider.Models[0].Id);

        Assert.NotNull(client);
    }

    [Fact]
    public void ExplainsWhyARequestWasRefused()
    {
        // "401" alone tells the user nothing they can act on.
        var message = ClaudeChatClient.Describe(
            System.Net.HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}""");

        Assert.Contains("API key", message);
    }

    [Fact]
    public void ExplainsRateLimiting()
    {
        var message = ClaudeChatClient.Describe(
            System.Net.HttpStatusCode.TooManyRequests, "{}");

        Assert.Contains("rate limiting", message);
    }

    [Fact]
    public void PassesOnWhatTheAssistantSaidAboutOtherFailures()
    {
        var message = ClaudeChatClient.Describe(
            System.Net.HttpStatusCode.BadRequest,
            """{"error":{"message":"model not found"}}""");

        Assert.Contains("model not found", message);
    }

    [Fact]
    public void CopesWithAFailureThatIsNotJson()
    {
        var message = ClaudeChatClient.Describe(
            System.Net.HttpStatusCode.InternalServerError, "<html>Gateway error</html>");

        Assert.Contains("500", message);
    }
}

/// <summary>Where the API keys are kept.</summary>
public sealed class ApiKeyStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-keys", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A store that always uses the file, never the keychain.
    ///
    /// The keychain is the machine's, shared between test runs and the user's
    /// own entries; writing to it from a test would be rude.
    /// </summary>
    private ApiKeyStore FileStore => new(_root);

    [Fact]
    public void KeepsAKeyItWasGiven()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(),
            "On macOS the store prefers the keychain, which tests must not touch.");

        var store = FileStore;

        Assert.True(store.Set(AiVendor.Claude, "sk-secret"));
        Assert.Equal("sk-secret", store.Get(AiVendor.Claude));
    }

    [Fact]
    public void HasNoKeyBeforeOneIsSet()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "The keychain may hold a real key.");

        Assert.Null(FileStore.Get(AiVendor.Gemini));
        Assert.False(FileStore.Has(AiVendor.Gemini));
    }

    [Fact]
    public void KeepsTheAssistantsKeysApart()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "The keychain may hold a real key.");

        var store = FileStore;

        store.Set(AiVendor.Claude, "claude-key");
        store.Set(AiVendor.ChatGpt, "openai-key");

        Assert.Equal("claude-key", store.Get(AiVendor.Claude));
        Assert.Equal("openai-key", store.Get(AiVendor.ChatGpt));
    }

    [Fact]
    public void ForgetsAKeyWhenAskedTo()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "The keychain may hold a real key.");

        var store = FileStore;

        store.Set(AiVendor.Claude, "sk-secret");
        store.Remove(AiVendor.Claude);

        Assert.Null(store.Get(AiVendor.Claude));
    }

    [Fact]
    public void TreatsAnEmptyKeyAsRemovingIt()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "The keychain may hold a real key.");

        var store = FileStore;

        store.Set(AiVendor.Claude, "sk-secret");
        store.Set(AiVendor.Claude, "");

        Assert.False(store.Has(AiVendor.Claude));
    }

    [Fact]
    public void DoesNotWriteTheKeyWhereSettingsAreKept()
    {
        // The settings file is plain JSON a user may copy or paste into a bug
        // report; an API key bills its owner.
        Assert.DoesNotContain("settings", ApiKeyStore.DefaultFallbackPath);
        Assert.EndsWith("keys", ApiKeyStore.DefaultFallbackPath);
    }

    [Fact]
    public void LeavesTheKeyFileReadableByItsOwnerAlone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix permissions are the subject here.");
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "The keychain is used instead.");

        var store = FileStore;
        store.Set(AiVendor.Claude, "sk-secret");

        var file = Path.Combine(_root, "claude.key");

        // The analyser cannot see that Assert.SkipWhen above rules Windows
        // out, so the guard is repeated where it can.
        if (OperatingSystem.IsWindows()) return;

        var mode = File.GetUnixFileMode(file);

        Assert.False(mode.HasFlag(UnixFileMode.GroupRead));
        Assert.False(mode.HasFlag(UnixFileMode.OtherRead));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
