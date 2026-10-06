using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Basalt.Core.Services;
using Basalt.Workspace.Ai;

namespace Basalt.Tests;

/// <summary>
/// Talking to the three assistants.
///
/// The requests and the reading of replies are checked without a network:
/// what differs between the three is the shape of the JSON, and that is
/// exactly what these describe. Claude goes through the SDK, with a fake HTTP
/// handler standing where the network would be.
/// </summary>
public class AiProviderTests
{
    private static AiTool Tool(string name = "read_file") => new(
        name, "Reads a file.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("path")
        },
        (_, _) => Task.FromResult(new AiToolResult("ok")));

    private static AiTurnRequest Request(string model = "x", params AiMessage[] messages) => new(
        "You help with Visual Basic.",
        [Tool("search_text"), Tool("read_file")],
        messages.Length > 0 ? messages : [AiMessage.User("What does Dim do?")],
        AiEffort.Medium, 4000);

    private static AiMessage[] ToolConversation() =>
    [
        AiMessage.User("Read A.vb"),
        new(AiRole.Assistant, [new AiTextPart("Reading."),
            new AiToolCallPart("call_1", "read_file", new JsonObject { ["path"] = "A.vb" })]),
        new(AiRole.User, [new AiToolResultPart("call_1", "read_file", "Module A", false)])
    ];

    /// <summary>Answers every request with a prepared body and keeps what it was sent.</summary>
    private sealed class FakeHttp(string responseBody, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public List<string> Bodies { get; } = [];
        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (Body is not null) Bodies.Add(Body);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8,
                    status == HttpStatusCode.OK ? "text/event-stream" : "application/json")
            };
        }
    }

    private static async Task<List<AiTurnEvent>> RunAsync(IAiChatBackend backend, AiTurnRequest request)
    {
        var events = new List<AiTurnEvent>();

        await foreach (var update in backend.StreamTurnAsync(request, CancellationToken.None))
            events.Add(update);

        return events;
    }

    // Claude

    private static string ClaudeStream(string model, params string[] events) =>
        $"event: message_start\ndata: {{\"type\":\"message_start\",\"message\":{{\"id\":\"m1\",\"type\":\"message\","
        + $"\"role\":\"assistant\",\"model\":\"{model}\",\"content\":[],\"stop_reason\":null,"
        + "\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}}\n\n"
        + string.Concat(events.Select(e => $"data: {e}\n\n"))
        + "data: {\"type\":\"message_stop\"}\n\n";

    private static string ClaudeText(string text) =>
        "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n"
        + $"data: {{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{{\"type\":\"text_delta\",\"text\":\"{text}\"}}}}\n\n"
        + "data: {\"type\":\"content_block_stop\",\"index\":0}";

    private static string ClaudeStop(string reason, string? details = null) =>
        $"{{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"{reason}\",\"stop_sequence\":null"
        + (details is null ? "" : $",\"stop_details\":{details}") + "},\"usage\":{\"output_tokens\":5}}";

    private static async Task<(JsonObject Sent, FakeHttp Http, List<AiTurnEvent> Events)> ClaudeRoundTripAsync(
        string model, string stream, AiTurnRequest? request = null)
    {
        var http = new FakeHttp(stream);
        using var backend = new ClaudeBackend("test-key", model, new HttpClient(http), "https://claude.test");

        var events = await RunAsync(backend, request ?? Request(model));

        return (JsonNode.Parse(http.Body!)!.AsObject(), http, events);
    }

    [Fact]
    public async Task ClaudeRequestCarriesTheModelEffortCachingAndEagerTools()
    {
        var (sent, http, _) = await ClaudeRoundTripAsync("claude-opus-5-5",
            ClaudeStream("claude-opus-5-5", ClaudeText("Hi"), ClaudeStop("end_turn")));

        Assert.Equal("claude-opus-5-5", sent["model"]!.GetValue<string>());
        Assert.True(sent["stream"]!.GetValue<bool>());
        Assert.Equal("medium", sent["output_config"]!["effort"]!.GetValue<string>());

        // Thinking is always on for this model; sending it disabled is a 400.
        Assert.Null(sent["thinking"]);

        // One frozen system block, cached; nothing that varies is in it.
        var system = sent["system"]!.AsArray();
        Assert.Single(system);
        Assert.NotNull(system[0]!["cache_control"]);
        Assert.Equal(64000, sent["max_tokens"]!.GetValue<int>());

        // Tools sorted by name, each streamed eagerly; never a forced choice.
        var tools = sent["tools"]!.AsArray();
        Assert.Equal(["read_file", "search_text"], tools.Select(t => t!["name"]!.GetValue<string>()));
        Assert.All(tools, t => Assert.True(t!["eager_input_streaming"]!.GetValue<bool>()));
        Assert.Equal("auto", sent["tool_choice"]!["type"]!.GetValue<string>());

        // Server-side fallbacks in their "default" form, behind the beta header.
        Assert.Equal("default", sent["fallbacks"]!.GetValue<string>());
        Assert.Contains("server-side-fallback-2026-07-01",
            http.Request!.Headers.GetValues("anthropic-beta").SelectMany(v => v.Split(',')));
        Assert.Equal("test-key", http.Request.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task EachClaudeRequestBeginsWithExactlyWhatTheLastOneSent()
    {
        // Opus 5.5 ties its thinking to the conversation as it was: the system
        // text, the tools and every earlier message must be byte-identical
        // from one request to the next, across questions and tool rounds.
        var call = "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\","
            + "\"id\":\"toolu_1\",\"name\":\"read_file\",\"input\":{}}}\n\n"
            + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"path\\\": \\\"A.vb\\\"}\"}}\n\n"
            + "data: {\"type\":\"content_block_stop\",\"index\":0}";

        var bodies = new List<string>();
        var responses = new Queue<string>(
        [
            ClaudeStream("claude-opus-5-5", call, ClaudeStop("tool_use")),
            ClaudeStream("claude-opus-5-5", ClaudeText("Done."), ClaudeStop("end_turn")),
            ClaudeStream("claude-opus-5-5", ClaudeText("Again."), ClaudeStop("end_turn"))
        ]);

        var handler = new SequencedHttp(responses, bodies);
        using var backend = new ClaudeBackend("k", "claude-opus-5-5", new HttpClient(handler), "https://claude.test");

        var conversation = new AiConversation();
        var tools = new[] { Tool("read_file") };

        await foreach (var _ in conversation.AskAsync(backend, "Read A", new AiPromptContext("Solution: S", "Active file: A.vb"), tools)) { }
        await foreach (var _ in conversation.AskAsync(backend, "Again", new AiPromptContext("Solution: T", "Active file: B.vb"), tools)) { }

        Assert.Equal(3, bodies.Count);

        for (var i = 1; i < bodies.Count; i++)
        {
            var before = JsonNode.Parse(bodies[i - 1])!.AsObject();
            var after = JsonNode.Parse(bodies[i])!.AsObject();

            Assert.Equal(before["system"]!.ToJsonString(), after["system"]!.ToJsonString());
            Assert.Equal(before["tools"]!.ToJsonString(), after["tools"]!.ToJsonString());

            var earlier = before["messages"]!.AsArray();
            var later = after["messages"]!.AsArray();

            for (var m = 0; m < earlier.Count; m++)
                Assert.Equal(earlier[m]!.ToJsonString(), later[m]!.ToJsonString());

            Assert.True(later.Count > earlier.Count);
        }
    }

    private sealed class SequencedHttp(Queue<string> responses, List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "text/event-stream")
            };
        }
    }

    [Fact]
    public async Task ClaudeSendsNoFallbackOrEffortToModelsThatCannotTakeThem()
    {
        var (sent, _, _) = await ClaudeRoundTripAsync("claude-haiku-4-5",
            ClaudeStream("claude-haiku-4-5", ClaudeText("Hi"), ClaudeStop("end_turn")));

        Assert.Null(sent["fallbacks"]);
        Assert.Null(sent["output_config"]);
    }

    [Theory]
    [InlineData("claude-opus-5-5", 64000)]
    [InlineData("claude-haiku-4-5", 64000)]
    [InlineData("my-private-model", 16000)]
    public void ClaudeAsksForAsMuchAsTheModelAllowsAndLessForAnUnknownId(string model, int expected) =>
        Assert.Equal(expected, ClaudeBackend.MaxTokensFor(model));

    [Theory]
    [InlineData("prompt is too long: 250000 tokens > 200000 maximum", true)]
    [InlineData("This model's maximum context length is 128000 tokens", true)]
    [InlineData("The input token count (2000000) exceeds the maximum number of tokens allowed", true)]
    [InlineData("The assistant returned 400: model not found", false)]
    public void RecognisesAConversationThatNoLongerFits(string message, bool expected) =>
        Assert.Equal(expected, AiErrors.IsContextTooLong(message));

    [Fact]
    public async Task ClaudeAcceptsAnyModelIdTheUserTypes()
    {
        var (sent, _, _) = await ClaudeRoundTripAsync("my-private-model",
            ClaudeStream("my-private-model", ClaudeText("Hi"), ClaudeStop("end_turn")));

        Assert.Equal("my-private-model", sent["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClaudeReadsTextAndAToolCallOutOfAStream()
    {
        const string call = "{\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\","
            + "\"id\":\"toolu_1\",\"name\":\"read_file\",\"input\":{}}}\n\n"
            + "data: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"pa\"}}\n\n"
            + "data: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"th\\\": \\\"A.vb\\\"}\"}}\n\n"
            + "data: {\"type\":\"content_block_stop\",\"index\":1}";

        var (_, _, events) = await ClaudeRoundTripAsync("claude-opus-5-5",
            ClaudeStream("claude-opus-5-5", ClaudeText("Reading."), call, ClaudeStop("tool_use")));

        Assert.Equal("Reading.", Assert.IsType<AiTextDelta>(events[0]).Text);

        var end = Assert.IsType<AiTurnEnd>(events[^1]);
        var toolCall = Assert.Single(end.Message.ToolCalls);

        Assert.Equal(AiStopReason.ToolUse, end.Stop);
        Assert.Equal("toolu_1", toolCall.Id);
        Assert.Equal("A.vb", toolCall.Input["path"]!.GetValue<string>());
        Assert.Null(toolCall.InputError);
        Assert.False(end.FellBack);
    }

    [Fact]
    public async Task ClaudeFlagsArgumentsThatAreNotWholeJson()
    {
        // With eager input streaming the API has not validated the input.
        const string call = "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\","
            + "\"id\":\"toolu_1\",\"name\":\"read_file\",\"input\":{}}}\n\n"
            + "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"path\\\": \\\"A\"}}\n\n"
            + "data: {\"type\":\"content_block_stop\",\"index\":0}";

        var (_, _, events) = await ClaudeRoundTripAsync("claude-opus-5-5",
            ClaudeStream("claude-opus-5-5", call, ClaudeStop("tool_use")));

        var toolCall = Assert.Single(Assert.IsType<AiTurnEnd>(events[^1]).Message.ToolCalls);

        Assert.NotNull(toolCall.InputError);
    }

    [Fact]
    public async Task ClaudeReportsARefusalWithItsCategory()
    {
        var (_, _, events) = await ClaudeRoundTripAsync("claude-opus-5-5",
            ClaudeStream("claude-opus-5-5",
                ClaudeStop("refusal", "{\"type\":\"refusal\",\"category\":\"cyber\",\"explanation\":null}")));

        var end = Assert.IsType<AiTurnEnd>(events[^1]);

        Assert.Equal(AiStopReason.Refusal, end.Stop);
        Assert.Equal("cyber", end.StopCategory);
    }

    [Fact]
    public async Task ClaudeSaysWhenAnotherModelAnswered()
    {
        var (_, _, events) = await ClaudeRoundTripAsync("claude-opus-5-5",
            ClaudeStream("claude-opus-4-8", ClaudeText("Hi"), ClaudeStop("end_turn")));

        var end = Assert.IsType<AiTurnEnd>(events[^1]);

        Assert.True(end.FellBack);
        Assert.Equal("claude-opus-4-8", end.ModelUsed);
    }

    [Fact]
    public async Task ClaudeDoesNotMistakeADatedIdForAFallback()
    {
        var (_, _, events) = await ClaudeRoundTripAsync("claude-haiku-4-5",
            ClaudeStream("claude-haiku-4-5-20251001", ClaudeText("Hi"), ClaudeStop("end_turn")));

        Assert.False(Assert.IsType<AiTurnEnd>(events[^1]).FellBack);
    }

    [Fact]
    public async Task ClaudeSendsToolCallsAndResultsBackInTheirOwnBlocks()
    {
        var (sent, _, _) = await ClaudeRoundTripAsync("claude-opus-5-5",
            ClaudeStream("claude-opus-5-5", ClaudeText("Hi"), ClaudeStop("end_turn")),
            Request("claude-opus-5-5", ToolConversation()));

        var messages = sent["messages"]!.AsArray();

        Assert.Equal("tool_use", messages[1]!["content"]![1]!["type"]!.GetValue<string>());
        Assert.Equal("call_1", messages[1]!["content"]![1]!["id"]!.GetValue<string>());
        Assert.Equal("tool_result", messages[2]!["content"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("call_1", messages[2]!["content"]![0]!["tool_use_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClaudeExplainsAnExpiredKey()
    {
        var http = new FakeHttp("""{"type":"error","error":{"type":"authentication_error","message":"bad key"}}""",
            HttpStatusCode.Unauthorized);
        using var backend = new ClaudeBackend("k", "claude-opus-5-5", new HttpClient(http), "https://claude.test");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(backend, Request()));

        Assert.Contains("API key", failure.Message);
    }

    [Fact]
    public void ClaudeOffersTheCurrentModels()
    {
        var ids = new ClaudeProvider().Models.Select(m => m.Id).ToList();

        Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-4-5"], ids);
    }

    // ChatGPT

    [Fact]
    public void OpenAiSendsTheSystemPromptAndToolsAsFunctions()
    {
        var body = new OpenAiBackend("k", "gpt-5").BuildRequest(Request());

        var messages = body["messages"]!.AsArray();

        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("You help with Visual Basic.", messages[0]!["content"]!.GetValue<string>());

        var tools = body["tools"]!.AsArray();

        Assert.Equal("function", tools[0]!["type"]!.GetValue<string>());
        Assert.Equal("read_file", tools[0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("object", tools[0]!["function"]!["parameters"]!["type"]!.GetValue<string>());
        Assert.Equal("medium", body["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public void OpenAiAnswersEachToolCallWithAToolMessage()
    {
        var body = new OpenAiBackend("k", "gpt-5").BuildRequest(Request("gpt-5", ToolConversation()));

        var messages = body["messages"]!.AsArray();

        var call = messages[2]!["tool_calls"]![0]!;
        Assert.Equal("call_1", call["id"]!.GetValue<string>());
        Assert.Equal("""{"path":"A.vb"}""", call["function"]!["arguments"]!.GetValue<string>());

        Assert.Equal("tool", messages[3]!["role"]!.GetValue<string>());
        Assert.Equal("call_1", messages[3]!["tool_call_id"]!.GetValue<string>());
    }

    [Fact]
    public void OpenAiLeavesReasoningEffortOutForOlderModels()
    {
        Assert.Null(new OpenAiBackend("k", "gpt-4o").BuildRequest(Request())["reasoning_effort"]);
    }

    [Fact]
    public async Task OpenAiReadsTextAndAFunctionCallOutOfAStream()
    {
        const string stream = """
            data: {"model":"gpt-5","choices":[{"delta":{"role":"assistant","content":"Reading."}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_9","type":"function","function":{"name":"read_file","arguments":""}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\":"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"A.vb\"}"}}]}}]}

            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}

            data: [DONE]

            """;

        var http = new FakeHttp(stream);
        using var backend = new OpenAiBackend("secret", "gpt-5", new HttpClient(http), "https://openai.test/v1/chat");

        var events = await RunAsync(backend, Request());

        Assert.Equal("Reading.", Assert.IsType<AiTextDelta>(events[0]).Text);

        var end = Assert.IsType<AiTurnEnd>(events[^1]);
        var call = Assert.Single(end.Message.ToolCalls);

        Assert.Equal(AiStopReason.ToolUse, end.Stop);
        Assert.Equal("call_9", call.Id);
        Assert.Null(call.InputError);
        Assert.Equal("A.vb", call.Input["path"]!.GetValue<string>());
        Assert.Equal("Bearer secret", http.Request!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task OpenAiTreatsAContentFilterAsARefusal()
    {
        var http = new FakeHttp("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"content_filter\"}]}\n\ndata: [DONE]\n\n");
        using var backend = new OpenAiBackend("k", "gpt-5", new HttpClient(http), "https://openai.test/v1/chat");

        var end = Assert.IsType<AiTurnEnd>((await RunAsync(backend, Request()))[^1]);

        Assert.Equal(AiStopReason.Refusal, end.Stop);
    }

    // Gemini

    [Fact]
    public void GeminiCallsTheAssistantModelRatherThanAssistant()
    {
        var body = new GeminiBackend("k", "gemini-2.5-pro").BuildRequest(Request("x", ToolConversation()));

        var contents = body["contents"]!.AsArray();

        Assert.Contains(contents, c => c!["role"]!.GetValue<string>() == "model");
        Assert.DoesNotContain(contents, c => c!["role"]!.GetValue<string>() == "assistant");
        Assert.NotNull(body["systemInstruction"]);
    }

    [Fact]
    public void GeminiDeclaresToolsAsFunctionsAndAnswersCallsWithFunctionResponses()
    {
        var body = new GeminiBackend("k", "gemini-2.5-pro").BuildRequest(Request("x", ToolConversation()));

        var declaration = body["tools"]![0]!["functionDeclarations"]![0]!;

        Assert.Equal("read_file", declaration["name"]!.GetValue<string>());
        Assert.Equal("OBJECT", declaration["parameters"]!["type"]!.GetValue<string>());

        var contents = body["contents"]!.AsArray();

        Assert.Equal("read_file", contents[1]!["parts"]![1]!["functionCall"]!["name"]!.GetValue<string>());
        Assert.Equal("Module A",
            contents[2]!["parts"]![0]!["functionResponse"]!["response"]!["result"]!.GetValue<string>());
    }

    [Fact]
    public async Task GeminiPutsTheModelInTheAddressAndTheKeyInAHeader()
    {
        var http = new FakeHttp("data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi\"}]},\"finishReason\":\"STOP\"}]}\n\n");
        using var backend = new GeminiBackend("secret", "gemini-2.5-flash", new HttpClient(http), "https://gemini.test");

        await RunAsync(backend, Request());

        Assert.Contains("models/gemini-2.5-flash:streamGenerateContent", http.Request!.RequestUri!.ToString());
        Assert.DoesNotContain("secret", http.Request.RequestUri.ToString());
        Assert.Equal("secret", http.Request.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task GeminiReadsTextAndAFunctionCallOutOfAStream()
    {
        const string stream = """
            data: {"candidates":[{"content":{"parts":[{"text":"Reading."}]}}]}

            data: {"candidates":[{"content":{"parts":[{"functionCall":{"name":"read_file","args":{"path":"A.vb"}}}]},"finishReason":"STOP"}]}

            """;

        using var backend = new GeminiBackend("k", "gemini-2.5-pro", new HttpClient(new FakeHttp(stream)), "https://gemini.test");

        var events = await RunAsync(backend, Request());

        var end = Assert.IsType<AiTurnEnd>(events[^1]);
        var call = Assert.Single(end.Message.ToolCalls);

        Assert.Equal("Reading.", Assert.IsType<AiTextDelta>(events[0]).Text);
        Assert.Equal(AiStopReason.ToolUse, end.Stop);
        Assert.Equal("read_file", call.Name);
        Assert.Equal("A.vb", call.Input["path"]!.GetValue<string>());
    }

    [Fact]
    public async Task GeminiTreatsASafetyStopAsARefusal()
    {
        var http = new FakeHttp("data: {\"candidates\":[{\"finishReason\":\"SAFETY\"}]}\n\n");
        using var backend = new GeminiBackend("k", "gemini-2.5-pro", new HttpClient(http), "https://gemini.test");

        Assert.Equal(AiStopReason.Refusal, Assert.IsType<AiTurnEnd>((await RunAsync(backend, Request()))[^1]).Stop);
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
    public void EveryProviderMakesABackendForAnyModelId(Type providerType)
    {
        // The panel talks to the backend and does not know which assistant it
        // is talking to; the model may be one the user typed.
        var provider = (IAiChatProvider)Activator.CreateInstance(providerType)!;

        using var backend = provider.CreateBackend("key", "a-model-not-in-the-list");

        Assert.NotNull(backend);
    }

    [Fact]
    public void ExplainsWhyARequestWasRefused()
    {
        // "401" alone tells the user nothing they can act on.
        var message = AiErrors.Describe(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}""");

        Assert.Contains("API key", message);
    }

    [Fact]
    public void ExplainsRateLimiting()
    {
        Assert.Contains("rate limiting", AiErrors.Describe(HttpStatusCode.TooManyRequests, "{}"));
    }

    [Fact]
    public void PassesOnWhatTheAssistantSaidAboutOtherFailures()
    {
        var message = AiErrors.Describe(HttpStatusCode.BadRequest, """{"error":{"message":"model not found"}}""");

        Assert.Contains("model not found", message);
    }

    [Fact]
    public void CopesWithAFailureThatIsNotJson()
    {
        Assert.Contains("500", AiErrors.Describe(HttpStatusCode.InternalServerError, "<html>Gateway error</html>"));
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
