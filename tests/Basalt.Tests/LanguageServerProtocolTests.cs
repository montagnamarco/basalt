using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Basalt.Tests;

/// <summary>
/// The language server driven over the real JSON-RPC protocol.
///
/// Every other test in this project calls a handler class directly, which
/// proves the handler answers correctly but nothing about the wire around
/// it: the framing, the capability exchange, or whether a position survives
/// the trip in the units the protocol promises rather than the ones the
/// parser happens to count in. This starts the actual server — through
/// <see cref="ServerSetup.Configure"/>, the very configuration Program.cs
/// hands standard input and output — over a pair of in-memory pipes, and
/// holds the same conversation an editor would.
/// </summary>
public sealed class LanguageServerProtocolTests : IDisposable
{
    // One empty folder per test, removed afterwards: xUnit makes an instance
    // for each test.
    private readonly string _root = EmptyWorkspace();

    public void Dispose() => ScratchFolder.Delete(_root);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>An empty folder, so the server finds no solution to open.</summary>
    /// <remarks>
    /// Loading a real solution is exercised by
    /// <see cref="LanguageServerCompilationTests"/>; here the interest is the
    /// protocol conversation itself, and an empty folder keeps every request
    /// answered from the markup alone, without a wait for Roslyn.
    /// </remarks>
    private static string EmptyWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "basalt-lsp-protocol-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        return path;
    }

    private static string FileUri(string root, string name) =>
        new Uri(Path.Combine(root, name)).AbsoluteUri;

    [Fact]
    public async Task InitializeAnswersWithTheCapabilitiesTheEditorNeeds()
    {
        var root = _root;

        await using var client = new LspHarness();

        var result = await client.InitializeAsync(root);
        var capabilities = result.GetProperty("capabilities");

        // Each of these is a feature an editor turns on only when the server
        // advertises it: missing one here is missing it for every editor that
        // speaks to this process, not just the one being tested by hand.
        foreach (var expected in new[]
        {
            "completionProvider", "hoverProvider", "semanticTokensProvider",
            "definitionProvider", "referencesProvider", "documentFormattingProvider"
        })
        {
            Assert.True(
                capabilities.TryGetProperty(expected, out _),
                $"The initialize response did not advertise \"{expected}\".");
        }
    }

    [Fact]
    public async Task OffersDirectivesAfterAnAtSignOnceOpened()
    {
        var root = _root;
        var uri = FileUri(root, "Test.vbhtml");

        await using var client = new LspHarness();

        await client.InitializeAsync(root);
        await client.InitializedAsync();
        await client.DidOpenAsync(uri, "@");

        // The caret sits right after the "@", where only a directive makes
        // sense: the completion handler treats a bare "@" at the start of a
        // line as the directive list rather than markup or a model member.
        var completions = await client.RequestAsync(
            "textDocument/completion",
            new { textDocument = new { uri }, position = new { line = 0, character = 1 } });

        // A CompletionList that is not incomplete serialises as a bare array
        // of items rather than as {"items": [...]}: the wrapper only appears
        // when there is something else — "isIncomplete" — worth saying.
        var items = completions.ValueKind == JsonValueKind.Array
            ? completions
            : completions.GetProperty("items");
        var labels = items.EnumerateArray()
            .Select(item => item.GetProperty("label").GetString())
            .ToList();

        Assert.Contains("Code", labels);
    }

    [Fact]
    public async Task DescribesTheStructureOfACodeBlock()
    {
        var root = _root;
        var uri = FileUri(root, "Test.vbhtml");
        const string text = "@Code\n Dim x = 1\nEnd Code";

        await using var client = new LspHarness();

        await client.InitializeAsync(root);
        await client.InitializedAsync();
        await client.DidOpenAsync(uri, text);

        var symbols = await client.RequestAsync(
            "textDocument/documentSymbol", new { textDocument = new { uri } });

        Assert.NotEmpty(symbols.EnumerateArray());
        Assert.Contains(
            symbols.EnumerateArray(),
            symbol => symbol.GetProperty("name").GetString() == "@Code");

        var folds = await client.RequestAsync(
            "textDocument/foldingRange", new { textDocument = new { uri } });

        // One range, from the "@Code" line to the "End Code" line: the block
        // opens on line 0 and closes on line 2, both zero-based.
        var fold = Assert.Single(folds.EnumerateArray());
        Assert.Equal(0, fold.GetProperty("startLine").GetInt32());
        Assert.Equal(2, fold.GetProperty("endLine").GetInt32());
    }

    /// <summary>
    /// A character outside the Basic Multilingual Plane, ahead of a name on
    /// the same line, does not shift where the name is reported.
    /// </summary>
    /// <remarks>
    /// LSP counts a position in UTF-16 code units, and 😀 (U+1F600) is a
    /// surrogate pair — two of them — in the UTF-16 a .NET string already is.
    /// <see cref="VbHtmlSemanticTokensHandler.LineAndCharacterOf"/> turns an
    /// offset into a line and character by counting <c>text[i]</c>, which is
    /// to say by counting code units already, so no conversion — and no
    /// off-by-one from treating the emoji as one "character" — should be
    /// needed. This proves it by computing the expected column with plain
    /// string arithmetic rather than by calling the function under test.
    /// </remarks>
    [Fact]
    public async Task ReportsPositionsAfterAnAstralCharacterInUtf16Units()
    {
        var root = _root;
        var uri = FileUri(root, "Test.vbhtml");

        const string secondLine = "    Dim result = 😀total + total";
        var text = "@Code\n"
                 + "    Dim total = 1\n"
                 + secondLine + "\n"
                 + "End Code";

        await using var client = new LspHarness();

        await client.InitializeAsync(root);
        await client.InitializedAsync();
        await client.DidOpenAsync(uri, text);

        // The caret is put on the plain declaration, on line 1 — the request
        // side of the conversion, which involves no astral character and is
        // not what this test is about.
        var declarationLine = "    Dim total = 1";
        var caretCharacter = declarationLine.IndexOf("total", StringComparison.Ordinal);

        var highlights = await client.RequestAsync(
            "textDocument/documentHighlight",
            new
            {
                textDocument = new { uri },
                position = new { line = 1, character = caretCharacter }
            });

        // Independently, in UTF-16 code units: an emoji is two of them in the
        // .NET string this line already is, so this is exactly what the
        // protocol is supposed to report — not what counting the emoji as one
        // "character" would give.
        var expectedAfterEmoji =
            secondLine.IndexOf("😀total", StringComparison.Ordinal) + "😀".Length;
        var expectedTrailing = secondLine.LastIndexOf("total", StringComparison.Ordinal);

        Assert.NotEqual(expectedAfterEmoji, expectedTrailing);

        var reported = highlights.EnumerateArray()
            .Select(highlight => highlight.GetProperty("range").GetProperty("start"))
            .Where(start => start.GetProperty("line").GetInt32() == 2)
            .Select(start => start.GetProperty("character").GetInt32())
            .OrderBy(character => character)
            .ToList();

        Assert.Equal(new List<int> { expectedAfterEmoji, expectedTrailing }, reported);
    }

    [Fact]
    public async Task ShutdownAndExitEndTheServer()
    {
        var root = _root;

        await using var client = new LspHarness();

        await client.InitializeAsync(root);
        await client.InitializedAsync();

        var shutdown = await client.RequestAsync("shutdown", null);
        Assert.Equal(JsonValueKind.Null, shutdown.ValueKind);

        await client.ExitAsync();

        Assert.True(
            await client.ExitedWithinAsync(Timeout),
            "The server's listening task did not end after shutdown and exit.");
    }

    /// <summary>
    /// Speaks JSON-RPC to a server started over a pair of in-memory pipes.
    /// </summary>
    /// <remarks>
    /// No OmniSharp client library is referenced by this repository — only
    /// the server package is — so this is the small amount of framing the
    /// protocol needs, written by hand: a Content-Length header, a JSON body,
    /// and a way to match a response back to the request that asked for it.
    /// </remarks>
    internal sealed class LspHarness : IAsyncDisposable
    {
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly Stream _writeToServer;
        private readonly Stream _readFromServer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task<OmniSharp.Extensions.LanguageServer.Server.LanguageServer> _serverStarted;
        private readonly Task _readLoop;
        private long _nextId;

        public LspHarness()
        {
            _writeToServer = _clientToServer.Writer.AsStream();
            _readFromServer = _serverToClient.Reader.AsStream();

            // The very configuration Program.cs hands standard input and
            // output, handed a pair of pipes instead: what is under test is
            // that configuration, not a second copy of it.
            _serverStarted = OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options =>
                ServerSetup.Configure(
                    options,
                    _clientToServer.Reader.AsStream(),
                    _serverToClient.Writer.AsStream()));

            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        /// <summary>Whether the server's own listening task ends within a timeout.</summary>
        public async Task<bool> ExitedWithinAsync(TimeSpan timeout)
        {
            // Already running since the constructor: awaiting it here waits
            // for the initialize handshake that is already under way, rather
            // than starting anything new.
#pragma warning disable VSTHRD003
            var server = await _serverStarted.ConfigureAwait(false);
#pragma warning restore VSTHRD003

            // Read once: WaitForExit is a property, and comparing a second
            // read of it against the first would compare two different Task
            // instances even when both describe the same completion.
            var waitForExit = server.WaitForExit;
            var completed = await Task.WhenAny(waitForExit, Task.Delay(timeout))
                .ConfigureAwait(false);

            return completed == waitForExit;
        }

        public async Task<JsonElement> InitializeAsync(string rootPath)
        {
            var rootUri = new Uri(rootPath).AbsoluteUri;

            // Declaring no capability at all reads to OmniSharp as a client
            // that supports nothing, and it answers by registering nothing:
            // every provider came back missing, not merely unadvertised. A
            // real editor declares what it supports; this states the same
            // thing, with dynamicRegistration left false so the server
            // answers statically, in the initialize result itself, rather
            // than through a client/registerCapability request this harness
            // would then have to answer.
            var result = await RequestAsync(
                "initialize",
                new
                {
                    processId = Environment.ProcessId,
                    rootUri,
                    capabilities = new
                    {
                        textDocument = new
                        {
                            synchronization = new { dynamicRegistration = false },
                            completion = new { dynamicRegistration = false },
                            hover = new { dynamicRegistration = false },
                            signatureHelp = new { dynamicRegistration = false },
                            definition = new { dynamicRegistration = false },
                            references = new { dynamicRegistration = false },
                            documentHighlight = new { dynamicRegistration = false },
                            documentSymbol = new { dynamicRegistration = false },
                            formatting = new { dynamicRegistration = false },
                            rangeFormatting = new { dynamicRegistration = false },
                            onTypeFormatting = new { dynamicRegistration = false },
                            foldingRange = new { dynamicRegistration = false },
                            linkedEditingRange = new { dynamicRegistration = false },
                            semanticTokens = new
                            {
                                dynamicRegistration = false,
                                requests = new { full = true },
                                tokenTypes = Array.Empty<string>(),
                                tokenModifiers = Array.Empty<string>(),
                                formats = new[] { "relative" }
                            }
                        },
                        workspace = new
                        {
                            workspaceFolders = true,
                            didChangeWatchedFiles = new { dynamicRegistration = false }
                        }
                    }
                });

            return result;
        }

        public Task InitializedAsync() => NotifyAsync("initialized", new { });

        public Task DidOpenAsync(string uri, string text) => NotifyAsync(
            "textDocument/didOpen",
            new
            {
                textDocument = new
                {
                    uri,
                    languageId = "vbhtml",
                    version = 1,
                    text
                }
            });

        public Task ExitAsync() => NotifyAsync("exit", null);

        public async Task<JsonElement> RequestAsync(string method, object? @params)
        {
            var id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<JsonElement>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_pending) _pending[id] = completion;

            await WriteMessageAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = @params
            }).ConfigureAwait(false);

            return await completion.Task.WaitAsync(Timeout).ConfigureAwait(false);
        }

        private Task NotifyAsync(string method, object? @params) => WriteMessageAsync(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = @params
            });

        private async Task WriteMessageAsync(object message)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

            await _writeLock.WaitAsync().ConfigureAwait(false);

            try
            {
                await _writeToServer.WriteAsync(header).ConfigureAwait(false);
                await _writeToServer.WriteAsync(body).ConfigureAwait(false);
                await _writeToServer.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    var message = await ReadOneMessageAsync(ct).ConfigureAwait(false);

                    if (message is null) return;

                    Dispatch(message.Value);
                }
            }
            catch (OperationCanceledException)
            {
                // Torn down by DisposeAsync; nothing left worth reporting.
            }
        }

        /// <summary>Reads one Content-Length-framed message, or null at end of stream.</summary>
        private async Task<JsonElement?> ReadOneMessageAsync(CancellationToken ct)
        {
            var contentLength = -1;

            while (true)
            {
                var line = await ReadHeaderLineAsync(ct).ConfigureAwait(false);

                if (line is null) return null;
                if (line.Length == 0) break;

                const string prefix = "Content-Length:";

                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(line[prefix.Length..].Trim());
            }

            if (contentLength < 0)
                throw new InvalidOperationException("A message with no Content-Length header arrived.");

            var buffer = new byte[contentLength];
            var read = 0;

            while (read < contentLength)
            {
                var n = await _readFromServer
                    .ReadAsync(buffer.AsMemory(read, contentLength - read), ct)
                    .ConfigureAwait(false);

                if (n == 0) return null;

                read += n;
            }

            using var document = JsonDocument.Parse(buffer);

            return document.RootElement.Clone();
        }

        /// <summary>
        /// One header line, without its trailing CRLF, or null at end of stream.
        /// </summary>
        /// <remarks>
        /// Read a byte at a time rather than through a <see cref="StreamReader"/>:
        /// that would buffer ahead of the blank line separating the headers from
        /// the body, and the bytes it took would be the start of the JSON this
        /// method's caller reads next.
        /// </remarks>
        private async Task<string?> ReadHeaderLineAsync(CancellationToken ct)
        {
            var bytes = new List<byte>();
            var single = new byte[1];

            while (true)
            {
                var n = await _readFromServer.ReadAsync(single.AsMemory(0, 1), ct).ConfigureAwait(false);

                if (n == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);

                if (single[0] == (byte)'\n')
                {
                    if (bytes.Count > 0 && bytes[^1] == (byte)'\r') bytes.RemoveAt(bytes.Count - 1);

                    return Encoding.ASCII.GetString([.. bytes]);
                }

                bytes.Add(single[0]);
            }
        }

        /// <summary>Delivers a response to whoever is waiting for its id; drops the rest.</summary>
        /// <remarks>
        /// "The rest" is a notification — a published diagnostic, a log line —
        /// or a request the server sends the client, neither of which any test
        /// here needs to answer.
        /// </remarks>
        private void Dispatch(JsonElement message)
        {
            if (message.TryGetProperty("method", out _)) return;
            if (!message.TryGetProperty("id", out var idProperty)) return;

            var id = idProperty.GetInt64();
            TaskCompletionSource<JsonElement>? completion;

            lock (_pending)
            {
                if (!_pending.Remove(id, out completion)) return;
            }

            if (message.TryGetProperty("error", out var error))
                completion.TrySetException(new InvalidOperationException(
                    $"The language server answered {id} with an error: {error}"));
            else
                completion.TrySetResult(
                    message.TryGetProperty("result", out var result) ? result : default);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);

            // Signals end of input the way closing an editor's pipe to the
            // process would: without it, a test that never sends "exit"
            // leaves the server's own read loop blocked forever, waiting for
            // bytes that are not coming.
            await _clientToServer.Writer.CompleteAsync().ConfigureAwait(false);

            try
            {
                // Already running since the constructor; there is nothing to
                // start here, only its end to wait for, which is exactly what
                // tearing the harness down needs.
#pragma warning disable VSTHRD003
                await _readLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (Exception)
            {
                // Torn down mid-read; the test that cared about the outcome
                // has already made its assertions.
            }

            _cts.Dispose();
            _writeLock.Dispose();
        }
    }
}
