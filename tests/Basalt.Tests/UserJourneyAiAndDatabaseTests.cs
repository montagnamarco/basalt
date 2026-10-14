using System.Net;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Data;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Ai;
using Dock.Model.Core;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The assistant panel and the Database panel driven the way a person drives
/// them, through a real <see cref="MainWindow"/>: menus, the Enter key, the
/// Send, Stop, Clear and Apply buttons, the tree and the grid.
/// </summary>
/// <remarks>
/// No model provider is called and no keychain is touched. The assistant's
/// key store is an in-memory double, and its HTTP goes to a scripted handler
/// standing where the network would be, so the real OpenAI backend reads the
/// fake stream and turns the fake 401 and 500 into messages. Settings go to a
/// file of this test's own through BASALT_SETTINGS, so the class shares
/// <see cref="SettingsFileCollection"/> with the other settings journeys.
/// </remarks>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyAiAndDatabaseTests : IDisposable
{
    private const string FakeKey = "not-a-real-key";

    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-ai-db-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-ai-db-journey-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyAiAndDatabaseTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);
        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string L(string key, params object[] args) => Localizer.Get(key, args);

    private void WriteSettings(Action<IdeSettings> change)
    {
        var store = new SettingsStore(_settings);
        var settings = store.Load();

        change(settings);
        store.Save(settings);
    }

    /// <summary>A main window that closes itself, without the save prompt, when the test ends.</summary>
    private sealed class JourneyShell : IDisposable
    {
        public JourneyShell(string? solution, AiAssistantServices assistant)
        {
            Window = new MainWindow(solution, assistant);
            Window.Show();
        }

        public MainWindow Window { get; }

        /// <summary>Several layout passes with short pauses, so panels built over more than one pass exist.</summary>
        public async Task SettleAsync(int passes = 5)
        {
            for (var i = 0; i < passes; i++)
            {
                Window.UpdateLayout();
                await Task.Delay(60);
            }

            Window.UpdateLayout();
        }

        public void Dispose()
        {
            Window.SkipSaveChangesPromptForTests = true;
            Window.Close();
        }
    }

    /// <summary>An in-memory key store: nothing reaches the keychain or the key file.</summary>
    private sealed class MemoryKeyStore : IApiKeyStore
    {
        private readonly Dictionary<AiVendor, string> _keys = [];

        public bool UsesPlatformKeychain => false;

        public string? Get(AiVendor vendor) => _keys.GetValueOrDefault(vendor);

        public bool Set(AiVendor vendor, string apiKey)
        {
            if (apiKey.Length == 0) return Remove(vendor);

            _keys[vendor] = apiKey;
            return true;
        }

        public bool Remove(AiVendor vendor) => _keys.Remove(vendor);

        public bool Has(AiVendor vendor) => _keys.ContainsKey(vendor);
    }

    /// <summary>One answer the fake provider gives: a status, a body, or a body that then stops arriving.</summary>
    private sealed record Reply(HttpStatusCode Status, string Body, bool StallAfterBody = false);

    /// <summary>
    /// Stands where the provider's network would be. Answers each request with the
    /// next scripted reply and keeps the bodies the provider sent, so the test can
    /// read what the assistant was told.
    /// </summary>
    private sealed class ScriptedHttp : HttpMessageHandler
    {
        private readonly Queue<Reply> _replies = new();

        /// <summary>The request bodies, in the order they were sent.</summary>
        public List<string> Requests { get; } = [];

        public void Enqueue(Reply reply) => _replies.Enqueue(reply);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));

            if (_replies.Count == 0)
                throw new InvalidOperationException("The test sent a request it had no reply for.");

            var reply = _replies.Dequeue();

            if (reply.StallAfterBody)
            {
                return new HttpResponseMessage(reply.Status)
                {
                    Content = new StreamContent(new StallingStream(reply.Body))
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") }
                    }
                };
            }

            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8,
                    reply.Status == HttpStatusCode.OK ? "text/event-stream" : "application/json")
            };
        }
    }

    /// <summary>
    /// A reply that sends its first part and then waits, as a model does while it
    /// is still writing. Only a cancellation ends the wait, which is what Stop does.
    /// </summary>
    private sealed class StallingStream(string firstPart) : Stream
    {
        private byte[] _pending = Encoding.UTF8.GetBytes(firstPart);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pending.Length > 0)
            {
                var count = Math.Min(buffer.Length, _pending.Length);

                _pending.AsMemory(0, count).CopyTo(buffer);
                _pending = _pending[count..];

                return count;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        }
    }

    /// <summary>The events of one OpenAI-style answer: the text, then the end of the stream.</summary>
    private static string Answer(string text) =>
        $"data: {{\"choices\":[{{\"delta\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}}}}}]}}\n\n"
        + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"
        + "data: [DONE]\n\n";

    /// <summary>The first part of an answer, with no end to it.</summary>
    private static string PartialAnswer(string text) =>
        $"data: {{\"choices\":[{{\"delta\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}}}}}]}}\n\n";

    /// <summary>The assistant panel's services: the fake key store and the real OpenAI backend over the scripted HTTP.</summary>
    private static AiAssistantServices FakeProviderServices(MemoryKeyStore keys, ScriptedHttp http) =>
        new(keys, (provider, key, model) => provider.Vendor switch
        {
            AiVendor.ChatGpt => new OpenAiBackend(key, model, new HttpClient(http), "https://openai.test/v1/chat"),
            _ => throw new InvalidOperationException("The journey only scripts the ChatGPT assistant."),
        });

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        if (hit!.Action is { } action) action();
        else hit.Command!.Execute(null);
    }

    private static MainWindowViewModel ViewModelOf(JourneyShell shell) =>
        (MainWindowViewModel)shell.Window.DataContext!;

    private static async Task<bool> WaitUntilAsync(JourneyShell shell, Func<bool> condition, int attempts = 200)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition()) return true;

            await shell.SettleAsync(1);
        }

        return condition();
    }

    /// <summary>The first window the main window owns, once it has opened.</summary>
    private static async Task<Window?> WaitForOwnedWindowAsync(JourneyShell shell)
    {
        Window? dialog = null;

        for (var i = 0; i < 80 && dialog is null; i++)
        {
            await shell.SettleAsync(1);
            dialog = shell.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>The Enter key pressed in the box where the question is typed.</summary>
    private static void PressEnter(TextBox box, KeyModifiers modifiers = KeyModifiers.None) =>
        PressKey(box, Key.Enter, modifiers);

    /// <summary>A key pressed in the box where the question is typed.</summary>
    private static void PressKey(TextBox box, Key key, KeyModifiers modifiers) =>
        box.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });

    /// <summary>The last button with this label: the newest answer's Apply, for one.</summary>
    private static Button ButtonLabelled(Visual scope, string label) =>
        scope.GetVisualDescendants().OfType<Button>().Last(b => b.Content as string == label);

    /// <summary>The box a question is typed into: the one editable multi-line box in the panel.</summary>
    private static TextBox QuestionBox(AiChatPanel panel) =>
        panel.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn && !t.IsReadOnly);

    private static AvaloniaEdit.TextEditor TextEditorFor(MainWindow window, string filePath)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);

        return code!.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
    }

    private static IdeDockFactory FactoryOf(MainWindow window) =>
        (IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static Dictionary<IDockable, Control> ContentsOf(MainWindow window) =>
        (Dictionary<IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    /// <summary>A solution with one console program, the editor's file for the selection journeys.</summary>
    private static string WriteConsoleProject(string root)
    {
        var projectPath = Path.Combine(root, "App.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.vb"), """
            Module Program
                Sub Main()
                    Console.WriteLine("hello")
                End Sub
            End Module
            """);

        return projectPath;
    }

    /// <summary>Opens the assistant panel from Tools > Assistant, as a person would.</summary>
    private static async Task<AiChatPanel> OpenAssistantAsync(JourneyShell shell)
    {
        InvokeMenu(shell.Window, L(StringKeys.MenuTools), L(StringKeys.MenuToolsAssistant));
        await shell.SettleAsync(5);

        return Assert.IsType<AiChatPanel>(shell.Window.AssistantForTests);
    }

    /// <summary>Asks a question the way a person does: typed, then Enter.</summary>
    private static void TypeAndSend(AiChatPanel panel, string question)
    {
        panel.InputText = question;
        PressEnter(QuestionBox(panel));
    }

    /// <summary>What the panel and the fake provider hold, for a failure message.</summary>
    private static string State(AiChatPanel panel, ScriptedHttp http) =>
        $"input=[{panel.InputText.Replace("\n", "\\n")}] status=[{panel.StatusText}] replying={panel.IsReplying} requests={http.Requests.Count} "
        + $"entries=[{string.Join(" | ", panel.Entries.Select(e => (e.IsUser ? "user:" : "assistant:") + e.Text))}]";

    private static bool AnswerShows(AiChatPanel panel, string text) =>
        panel.Entries.Any(e => !e.IsUser && e.Text.Contains(text, StringComparison.Ordinal));

    /// <summary>A database file with two tables, made before the panel opens it.</summary>
    private string CreateSampleDatabase(string name = "sample.db")
    {
        var path = Path.Combine(_root, name);

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT, City TEXT);
            INSERT INTO Customers (Name, City) VALUES ('Rossi', 'Torino'), ('Bianchi', 'Milano');
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total REAL);
            """;
        command.ExecuteNonQuery();

        return path;
    }

    // ------------------------------------------------------------------
    // Journey A: the assistant panel, with no key set up
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task WithNoAssistantChosenThePanelSaysWhereToChooseOne()
    {
        var http = new ScriptedHttp();
        using var shell = new JourneyShell(null, FakeProviderServices(new MemoryKeyStore(), http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);

        Assert.Equal(L(StringKeys.AiNoAssistant), panel.StatusText);
        Assert.Contains("Settings", panel.StatusText);
        Assert.Empty(http.Requests);

        TypeAndSend(panel, "Which assistant?");
        await shell.SettleAsync(3);

        Assert.Equal("Which assistant?", panel.InputText);
        Assert.Empty(http.Requests);
    }

    [AvaloniaFact]
    public async Task WithAProviderButNoKeyThePanelSaysWhatToAddAndCallsNothing()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var http = new ScriptedHttp();
        using var shell = new JourneyShell(null, FakeProviderServices(new MemoryKeyStore(), http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);

        var chatGpt = AiConversation.ProviderFor(AiVendor.ChatGpt);
        Assert.Equal(L(StringKeys.AiNeedsKey, chatGpt.DisplayName), panel.StatusText);
        Assert.Contains("API key", panel.StatusText);

        TypeAndSend(panel, "What does Dim do?");
        await shell.SettleAsync(3);

        // The question is kept in the box, so it is not lost while the key is missing.
        Assert.Equal("What does Dim do?", panel.InputText);
        // The question is not sent anywhere while there is no key to send it with.
        Assert.Empty(http.Requests);
        Assert.Equal(L(StringKeys.AiNeedsKey, chatGpt.DisplayName), panel.StatusText);
        Assert.False(panel.IsReplying);
    }

    // ------------------------------------------------------------------
    // Journey B: a configured provider answers
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TypingAQuestionAndPressingEnterStreamsTheAnswerIntoTheConversation()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.OK, Answer("Dim declares a variable.")));

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);
        var chatGpt = AiConversation.ProviderFor(AiVendor.ChatGpt);

        Assert.StartsWith(chatGpt.DisplayName, panel.StatusText);

        TypeAndSend(panel, "What does Dim do?");

        var answered = await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "Dim declares"));

        Assert.True(answered, "The fake answer did not arrive. " + State(panel, http));
        string[] conversation = ["What does Dim do?", "Dim declares a variable."];
        Assert.Equal(conversation, panel.Entries.Where(e => e.Kind == ChatEntryKind.Message).Select(e => e.Text).ToArray());
        Assert.True(panel.Entries[0].IsUser);

        // The question was typed into the provider's request, not just shown.
        Assert.Contains("What does Dim do?", Assert.Single(http.Requests));

        // The box is cleared once the question is sent, and Send is back to Send.
        Assert.Equal("", panel.InputText);
        Assert.Equal(L(StringKeys.AiSend), panel.SendButton.Content);
    }

    [AvaloniaFact]
    public async Task WithCodeSelectedTheRequestTheProviderGetsCarriesTheSelection()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.OK, Answer("It counts.")));

        var project = WriteConsoleProject(_root);
        using var shell = new JourneyShell(project, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var programFile = Path.Combine(_root, "Program.vb");
        await ViewModelOf(shell).OpenFileAsync(programFile);
        await shell.SettleAsync(5);

        var editor = TextEditorFor(shell.Window, programFile);
        var selected = "Console.WriteLine(\"hello\")";
        var start = editor.Text.IndexOf(selected, StringComparison.Ordinal);
        Assert.True(start >= 0);
        editor.Select(start, selected.Length);

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "What does this line print?");

        var answered = await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "It counts."));
        Assert.True(answered);

        // The selection is in what the provider was sent, as the user selected it.
        var sent = Assert.Single(http.Requests);
        Assert.Contains("Console.WriteLine", sent);
        Assert.Contains("What does this line print?", sent);
    }

    [AvaloniaFact]
    public async Task WithSendingTheOpenFileSwitchedOffTheSelectionStaysAtHome()
    {
        // Settings > Assistant > "Send the open file with the question" is off:
        // the question still goes, but none of the code the user selected does.
        WriteSettings(s =>
        {
            s.Ai.Vendor = AiVendor.ChatGpt.ToString();
            s.Ai.SendFileContext = false;
        });

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.OK, Answer("Noted.")));

        var project = WriteConsoleProject(_root);
        using var shell = new JourneyShell(project, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var programFile = Path.Combine(_root, "Program.vb");
        await ViewModelOf(shell).OpenFileAsync(programFile);
        await shell.SettleAsync(5);

        var editor = TextEditorFor(shell.Window, programFile);
        var selected = "Console.WriteLine(\"hello\")";
        var start = editor.Text.IndexOf(selected, StringComparison.Ordinal);
        Assert.True(start >= 0, "The program's line is not in the editor. " + editor.Text);
        editor.Select(start, selected.Length);

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "What does this line print?");

        var answered = await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "Noted."));
        Assert.True(answered, "The question did not get an answer. " + State(panel, http));

        var sent = Assert.Single(http.Requests);
        Assert.Contains("What does this line print?", sent);
        Assert.DoesNotContain("Console.WriteLine", sent);
    }

    [AvaloniaFact]
    public async Task StopEndsAReplyThatIsStillStreamingAndTheNextQuestionWorks()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.OK, PartialAnswer("A long answer that"), StallAfterBody: true));
        http.Enqueue(new Reply(HttpStatusCode.OK, Answer("Second answer.")));

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "Tell me at length");

        var streaming = await WaitUntilAsync(shell, () => panel.IsReplying && AnswerShows(panel, "A long answer that"));
        Assert.True(streaming, "The first part of the reply did not stream in.");

        // While the answer is arriving, the same button says Stop.
        Assert.Equal(L(StringKeys.AiStop), panel.SendButton.Content);

        Click(panel.SendButton);

        var stopped = await WaitUntilAsync(shell, () => !panel.IsReplying && panel.SendButton.IsEnabled);
        Assert.True(stopped, "The reply did not stop when Stop was pressed.");

        var reply = panel.Entries.Last(e => !e.IsUser);
        Assert.Contains("A long answer that", reply.Text);
        Assert.Contains(L(StringKeys.AiStopped), reply.Text);
        Assert.Equal(L(StringKeys.AiSend), panel.SendButton.Content);

        // The panel is usable again at once.
        TypeAndSend(panel, "Second question");
        var again = await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "Second answer."));

        Assert.True(again);
    }

    // ------------------------------------------------------------------
    // Journey C: the provider refuses or fails
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AnExpiredKeyShowsAReadableMessageAndNoException()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error"}}"""));

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "Hello?");

        var shown = await WaitUntilAsync(shell, () => !panel.IsReplying
            && panel.Entries.Any(e => e.Text == L(StringKeys.AiErrorKeyRefused)));

        Assert.True(shown, "The refused key was not explained in the conversation.");
        Assert.Equal(L(StringKeys.AiSend), panel.SendButton.Content);
        Assert.True(panel.SendButton.IsEnabled);
    }

    [AvaloniaFact]
    public async Task AProviderFailingWithA500ShowsItsStatusAndBodyNotAnException()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.InternalServerError, "<html>Gateway error</html>"));

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "Hello?");

        var expected = L(StringKeys.AiErrorStatus, 500, "<html>Gateway error</html>");
        var shown = await WaitUntilAsync(shell, () => !panel.IsReplying
            && panel.Entries.Any(e => e.Text == expected));

        Assert.True(shown, "The 500 was not shown with its status and body.");
        Assert.Equal(L(StringKeys.AiSend), panel.SendButton.Content);
    }

    // ------------------------------------------------------------------
    // Journey D: Clear, and an answer's code block
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ClearEmptiesTheConversationAndTheNextQuestionStartsFresh()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.OK, Answer("First answer.")));
        http.Enqueue(new Reply(HttpStatusCode.OK, Answer("Second answer.")));

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "First question");
        await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "First answer."));

        Assert.True(panel.Entries.Count > 0, "The first question was not put in the conversation. " + State(panel, http));

        var clear = ButtonLabelled(panel, L(StringKeys.AiClear));
        Click(clear);
        await shell.SettleAsync(2);

        Assert.Empty(panel.Entries);

        TypeAndSend(panel, "Second question");
        var answered = await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "Second answer."));
        Assert.True(answered);

        // The assistant was not told about the cleared conversation.
        Assert.Contains("First question", http.Requests[0]);
        Assert.DoesNotContain("First question", http.Requests[1]);
        Assert.Contains("Second question", http.Requests[1]);
    }

    [AvaloniaFact]
    public async Task AnAnswerWithACodeBlockOffersApplyAndApplyPutsTheCodeAtTheCaret()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.OK,
            Answer("Declare it first:\n```vb\nDim total As Integer = 0\n```")));

        var project = WriteConsoleProject(_root);
        using var shell = new JourneyShell(project, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var programFile = Path.Combine(_root, "Program.vb");
        await ViewModelOf(shell).OpenFileAsync(programFile);
        await shell.SettleAsync(5);

        var editor = TextEditorFor(shell.Window, programFile);
        var caret = editor.Text.IndexOf("Console.WriteLine", StringComparison.Ordinal);
        Assert.True(caret >= 0);
        editor.CaretOffset = caret;

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "Where do I declare the total?");
        await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "Declare it first"));

        // The code block is offered with its own button, which is not Send.
        var apply = ButtonLabelled(panel, L(StringKeys.AiApply));
        Click(apply);

        // Applying goes through the same diff preview as every other change.
        var dialog = Assert.IsType<RefactoringPreviewDialog>(await WaitForOwnedWindowAsync(shell));
        Click(ButtonLabelled(dialog, L(StringKeys.RefactoringPreviewApply)));

        var inserted = await WaitUntilAsync(shell, () => editor.Text.Contains("Dim total As Integer = 0", StringComparison.Ordinal));

        Assert.True(inserted, "The code block did not reach the editor.");
        Assert.Equal(caret, editor.Text.IndexOf("Dim total As Integer = 0", StringComparison.Ordinal));
        Assert.Contains("Console.WriteLine", editor.Text);
    }

    // ------------------------------------------------------------------
    // The engine's own contracts (DatabaseClient). The Database panel's journeys
    // moved to UserJourneyDatabaseClientTests when the Database Explorer replaced it.
    // ------------------------------------------------------------------

    [Fact]
    public async Task AQueryWithNoDatabaseOpenSaysSoInTheLocalizedWords()
    {
        await using var client = new DatabaseClient();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExecuteAsync("SELECT 1"));

        Assert.Equal(Localizer.Get(StringKeys.DatabaseNotOpen), failure.Message);
    }
    [AvaloniaFact]
    public async Task ShiftEnterWritesANewLineAndSendsNothing()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);
        var box = QuestionBox(panel);

        panel.InputText = "First line";
        box.CaretIndex = box.Text!.Length;
        PressEnter(box, KeyModifiers.Shift);
        await shell.SettleAsync(2);

        Assert.Equal("First line\n", panel.InputText);
        Assert.Empty(http.Requests);
    }

    [AvaloniaFact]
    public async Task EnterWhileAnAnswerStreamsSendsNothingAndLeavesTheAnswerRunning()
    {
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();
        http.Enqueue(new Reply(HttpStatusCode.OK, PartialAnswer("Streaming "), StallAfterBody: true));
        http.Enqueue(new Reply(HttpStatusCode.OK, Answer("Follow-up answer.")));

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);
        TypeAndSend(panel, "Explain this");

        var streaming = await WaitUntilAsync(shell, () => panel.IsReplying && AnswerShows(panel, "Streaming"));
        Assert.True(streaming, "The first part of the reply did not stream in.");

        // A follow-up typed while the answer streams, and Enter pressed on it.
        panel.InputText = "Follow-up";
        PressEnter(QuestionBox(panel));
        await shell.SettleAsync(3);

        // Enter neither sent it nor stopped the answer: the question waits in the box.
        Assert.True(panel.IsReplying, "Enter stopped the answer that was streaming.");
        Assert.Single(http.Requests);
        Assert.Equal("Follow-up", panel.InputText);

        Click(panel.SendButton);
        var stopped = await WaitUntilAsync(shell, () => !panel.IsReplying && panel.SendButton.IsEnabled);
        Assert.True(stopped, "Stop did not end the answer.");

        // Once the answer is over, the same Enter sends the waiting question.
        PressEnter(QuestionBox(panel));
        var answered = await WaitUntilAsync(shell, () => !panel.IsReplying && AnswerShows(panel, "Follow-up answer."));

        Assert.True(answered, "The question kept in the box was not sent after the answer ended.");
        Assert.Equal(2, http.Requests.Count);
        Assert.Contains("Follow-up", http.Requests[1]);
    }

    [AvaloniaFact]
    public async Task AnInputMethodKeyIsNotTheEnterThatSends()
    {
        // Headless runs cannot drive a real input method. What can be checked is
        // that the composition keys an input method reports are not Enter, so the
        // Enter that confirms a composition does not send the question.
        WriteSettings(s => s.Ai.Vendor = AiVendor.ChatGpt.ToString());

        var keys = new MemoryKeyStore();
        keys.Set(AiVendor.ChatGpt, FakeKey);

        var http = new ScriptedHttp();

        using var shell = new JourneyShell(null, FakeProviderServices(keys, http));
        await shell.SettleAsync(5);

        var panel = await OpenAssistantAsync(shell);

        panel.InputText = "Composing";
        PressKey(QuestionBox(panel), Key.ImeProcessed, KeyModifiers.None);
        await shell.SettleAsync(2);

        Assert.Empty(http.Requests);
        Assert.Equal("Composing", panel.InputText);
    }

    [Fact]
    public async Task SchemaReadsDoNotCompleteOnTheCallersThread()
    {
        var database = CreateSampleDatabase();

        await using var client = new DatabaseClient();
        await client.OpenAsync(database);

        // A read done on the calling thread would have finished before the call returned.
        var pending = client.GetTablesAsync();

        Assert.False(pending.IsCompleted, "The schema was read on the caller's thread.");
        Assert.Equal(["Customers", "Orders"], (await pending).Select(t => t.Name).ToArray());
    }
}
