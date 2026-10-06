using System.Diagnostics;
using System.Text;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Extensibility;

namespace Basalt.Workspace.Ai;

/// <summary>
/// Inline suggestions from the assistant the user chose for the chat.
///
/// The same providers and the same key store as the chat (the window passes
/// <c>ApiKeyStore.Get</c>): no second place to put a key, and no second idea
/// of which vendor is in use. Only the model may differ (a suggestion wants a
/// fast one), and the request is a single short turn with no tools.
/// </summary>
/// <remarks>
/// Everything that costs anything happens on a worker thread: the key store
/// may start a process (the macOS keychain), the file's real path is resolved
/// through links, the prompt is built from a 5 KB slice, and the request
/// streams for as long as the model takes. The editor's thread only hands over
/// the text and receives the answer.
///
/// The key is looked up once and kept for a few minutes, and the backend (so
/// its connection) is kept with it: a suggestion is asked for at every pause
/// in typing, and a keychain process and a new TLS handshake each time would
/// cost more than the suggestion is worth.
/// </remarks>
public sealed class AiInlineSuggestionProvider(
    Func<AiSettings> settings,
    Func<AiVendor, string?> keyFor,
    Func<IAiChatProvider, string, string, IAiChatBackend>? backendFactory = null)
    : IInlineSuggestionProvider, IDisposable
{
    /// <summary>
    /// How long a key lookup is trusted. A key replaced or removed in the
    /// settings window takes effect when settings change, or after this.
    /// </summary>
    internal static readonly TimeSpan KeyLifetime = TimeSpan.FromMinutes(5);

    private readonly Func<IAiChatProvider, string, string, IAiChatBackend> _backendFactory =
        backendFactory ?? ((provider, key, model) => provider.CreateBackend(key, model));

    private readonly object _cacheLock = new();
    private (AiVendor Vendor, string? Key, long At)? _key;
    private long _keyGeneration;
    private (AiVendor Vendor, string Model, string Key, IAiChatBackend Backend)? _backend;

    /// <summary>
    /// Whether the user turned suggestions on and chose an assistant. The key
    /// is not looked at here: this is asked on the editor's thread on every
    /// pause, and reading a key can start a process. A missing key is found
    /// on the worker thread and costs no request.
    /// </summary>
    public bool IsAvailable =>
        settings() is { InlineEnabled: true } current
        && Enum.TryParse<AiVendor>(current.Vendor, out _);

    /// <summary>The model a suggestion is asked of, for the settings that are in force.</summary>
    public static string ModelFor(AiSettings current, IAiProvider provider) =>
        current.InlineModel.Trim() is { Length: > 0 } inline ? inline
        : current.Model.Trim() is { Length: > 0 } chat ? chat
        : provider.Models[0].Id;

    /// <summary>
    /// Forgets the key, so the next request reads it again: a key was saved or
    /// removed, or settings changed. The connection is kept: a request may be
    /// streaming on it, and a different key or model builds a new one anyway.
    /// </summary>
    public void ForgetKey()
    {
        lock (_cacheLock)
        {
            _key = null;
            _keyGeneration++;
        }
    }

    /// <summary>Forgets the key and the connection: a request failed.</summary>
    public void Invalidate()
    {
        IAiChatBackend? old;

        lock (_cacheLock)
        {
            _key = null;
            _keyGeneration++;
            old = _backend?.Backend;
            _backend = null;
        }

        old?.Dispose();
    }

    public void Dispose() => Invalidate();

    public Task<InlineSuggestion?> SuggestAsync(
        string filePath, string text, int position, CancellationToken ct = default) =>
        // Task.Run: whatever the caller's thread, nothing below runs on it.
        Task.Run(() => AskAsync(filePath, text, position, ct), ct);

    /// <summary>
    /// Whether the file may hold credentials, by its real location: a link
    /// named like any source file may point at a secret, and the chat's tools
    /// refuse that, so this does.
    /// </summary>
    internal static bool IsSecret(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        try
        {
            return AiWorkspaceSandbox.IsSecret(Path.GetFullPath(filePath))
                   || AiWorkspaceSandbox.IsSecret(PathIdentity.Canonical(filePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException
                                      or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            // A path that cannot be resolved cannot be shown to be safe.
            return true;
        }
    }

    private string? KeyOf(AiVendor vendor)
    {
        long generation;

        lock (_cacheLock)
        {
            if (_key is { } cached && cached.Vendor == vendor
                && Stopwatch.GetElapsedTime(cached.At) < KeyLifetime) return cached.Key;

            generation = _keyGeneration;
        }

        var key = keyFor(vendor);

        // A key forgotten while this lookup ran (saved or removed meanwhile)
        // may be the one just read: it is used once but not kept.
        lock (_cacheLock)
        {
            if (generation == _keyGeneration) _key = (vendor, key, Stopwatch.GetTimestamp());
        }

        return key;
    }

    private IAiChatBackend BackendFor(AiVendor vendor, IAiChatProvider provider, string model, string key)
    {
        IAiChatBackend? stale = null;

        lock (_cacheLock)
        {
            if (_backend is { } held && held.Vendor == vendor && held.Model == model && held.Key == key)
                return held.Backend;

            stale = _backend?.Backend;

            var fresh = _backendFactory(provider, key, model);

            _backend = (vendor, model, key, fresh);

            stale?.Dispose();

            return fresh;
        }
    }

    private async Task<InlineSuggestion?> AskAsync(
        string filePath, string text, int position, CancellationToken ct)
    {
        var current = settings();

        if (!current.InlineEnabled || !Enum.TryParse<AiVendor>(current.Vendor, out var vendor)) return null;

        // Before the key is read and before anything is built: a file that
        // may hold credentials is never the subject of a request.
        if (IsSecret(filePath)) return null;

        // Nothing is sent, and no backend is built, without a key.
        if (KeyOf(vendor) is not { Length: > 0 } apiKey) return null;

        ct.ThrowIfCancellationRequested();

        var provider = AiConversation.ProviderFor(vendor);

        var prefix = AiInlineCompletion.PrefixOf(text, position);
        var suffix = AiInlineCompletion.SuffixOf(text, position);

        var request = new AiTurnRequest(
            AiInlineCompletion.SystemPrompt,
            [],
            [AiMessage.User(AiInlineCompletion.BuildUserMessage(filePath, prefix, suffix))],
            // Low: a suggestion that takes long is one nobody waits for.
            AiEffort.Low,
            AiInlineCompletion.MaxOutputTokens,
            Brief: true);

        var answer = new StringBuilder();
        var stop = AiStopReason.EndTurn;
        var cutShort = false;

        try
        {
            var backend = BackendFor(vendor, provider, ModelFor(current, provider), apiKey);

            await foreach (var update in backend.StreamTurnAsync(request, ct).ConfigureAwait(false))
            {
                if (update is AiTextDelta delta)
                {
                    answer.Append(delta.Text);

                    // Enough lines to choose from: reading on only spends tokens.
                    if (answer.ToString().Count(c => c == '\n') > AiInlineCompletion.MaxLines)
                    {
                        cutShort = true;
                        break;
                    }
                }
                else if (update is AiTurnEnd end)
                {
                    stop = end.Stop;
                }
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A rejected key must be looked up again, and a failed connection
            // is not worth keeping.
            Invalidate();
            throw;
        }

        ct.ThrowIfCancellationRequested();

        var reply = answer.ToString();

        // A refusal or an unknown ending is not an answer. One cut off by the
        // limit stops mid-line: only the lines that were finished are kept.
        if (stop is AiStopReason.Refusal or AiStopReason.Other or AiStopReason.ToolUse) return null;

        if (stop == AiStopReason.MaxTokens || cutShort) reply = AiInlineCompletion.CompleteLinesOnly(reply);

        return AiInlineCompletion.Clean(reply, prefix, suffix) is { } clean
            ? new InlineSuggestion(clean, position)
            : null;
    }
}
