using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.AI;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Ai;
using Basalt.Workspace.Refactoring;

namespace Basalt.Shell.Controls;

/// <summary>
/// The assistant's side of the window: asking, running its tools, and putting
/// what it proposes into the editors.
///
/// Kept out of the main window on purpose; the window only builds this and
/// hands it the few things it owns.
/// </summary>
internal sealed class AiAssistantController
{
    private readonly AiChatPanel _panel;
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<AiSettings> _settings;
    private readonly IApiKeyStore _keys;
    private readonly Func<AvaloniaEdit.TextEditor?> _currentEditor;
    private readonly Action<IEnumerable<string>> _showTabs;
    private readonly Func<RefactoringPreview, Task<bool>> _confirm;
    private readonly Func<IAiChatProvider, string, string, IAiChatBackend> _backendFactory;
    private readonly AiConversation _conversation = new();
    private readonly AiWorkspaceTools _workspaceTools;
    private readonly IReadOnlyList<AiTool> _tools;
    private AiAssistantHost? _host;
    private string? _conversationRoot;
    private bool _replying;
    private bool _clearAfterReply;

    public AiAssistantController(
        AiChatPanel panel,
        MainWindowViewModel viewModel,
        Func<AiSettings> settings,
        IApiKeyStore keys,
        Func<AvaloniaEdit.TextEditor?> currentEditor,
        Action<IEnumerable<string>> showTabs,
        Func<RefactoringPreview, Task<bool>> confirm,
        Func<IAiChatProvider, string, string, IAiChatBackend>? backendFactory = null)
    {
        _panel = panel;
        _viewModel = viewModel;
        _settings = settings;
        _keys = keys;
        _currentEditor = currentEditor;
        _showTabs = showTabs;
        _confirm = confirm;
        _backendFactory = backendFactory ?? ((provider, key, model) => provider.CreateBackend(key, model));

        _panel.Asked += (_, request) => Guarded.Run(() => AskAsync(request), viewModel.WriteOutput, "ide");
        _panel.ApplyRequested += (_, code) => Guarded.Run(() => ApplyCodeAsync(code), viewModel.WriteOutput, "ide");
        _panel.ChangesDecided += (_, decision) => Guarded.Run(() => DecideAsync(decision), viewModel.WriteOutput, "ide");
        _panel.Cleared += (_, _) => ClearConversation();

        // One set of tools for the life of the window, with or without a
        // solution: the tool list is part of what a conversation is cached
        // and signed against, so it never changes.
        _workspaceTools = new AiWorkspaceTools(CurrentHost);
        _tools = _workspaceTools.Create();

        ShowStatus();
    }

    /// <summary>Says which assistant is in use, or that none is set up.</summary>
    public void ShowStatus()
    {
        var settings = _settings();

        if (!Enum.TryParse<AiVendor>(settings.Vendor, out var vendor))
        {
            _panel.ShowStatus(Localizer.Get(StringKeys.AiNoAssistant));
            return;
        }

        var provider = AiConversation.ProviderFor(vendor);

        _panel.ShowStatus(_keys.Has(vendor)
            ? Localizer.Get(StringKeys.AiStatusModel, provider.DisplayName, ModelName(provider, settings.Model))
            : Localizer.Get(StringKeys.AiNeedsKey, provider.DisplayName));
    }

    /// <summary>A listed model by its name; a model id the user typed as it is.</summary>
    internal static string ModelName(IAiProvider provider, string modelId) =>
        modelId.Length == 0
            ? provider.Models[0].DisplayName
            : provider.Models.FirstOrDefault(m => m.Id == modelId)?.DisplayName ?? modelId;

    /// <summary>
    /// Asks the assistant, showing the reply as it arrives.
    ///
    /// Nothing is awaited before the turns are added: the user should see
    /// their question in the conversation the moment they ask it.
    /// </summary>
    public async Task AskAsync(AiRequest request)
    {
        var settings = _settings();

        if (!Enum.TryParse<AiVendor>(settings.Vendor, out var vendor))
        {
            RefuseQuestion(request, Localizer.Get(StringKeys.AiNoAssistant));
            return;
        }

        var provider = AiConversation.ProviderFor(vendor);

        if (_keys.Get(vendor) is not { Length: > 0 } apiKey)
        {
            RefuseQuestion(request, Localizer.Get(StringKeys.AiNeedsKey, provider.DisplayName));
            return;
        }

        // Any model id works, not only the listed ones.
        var model = settings.Model is { Length: > 0 } chosen ? chosen : provider.Models[0].Id;

        var action = ActionName(request.Action);
        var question = AiConversation.InstructionFor(action, request.Question);

        // The solution is told to the assistant once, in the first message.
        // Another solution is another conversation, said so rather than
        // carried over under a description that is no longer true.
        _panel.Append(ChatRole.User, request.Question.Length > 0 ? request.Question : ActionLabel(request.Action));
        _panel.Append(ChatRole.Assistant, "");

        using var running = new CancellationTokenSource();
        _panel.BeginReply(running);
        _replying = true;

        var activity = new Dictionary<string, ChatEntry>();

        try
        {
            // Resolving the folder lists directories on some systems: off the
            // interface thread.
            var solutionPath = _viewModel.SolutionPath;
            var root = await Task.Run(() => AiAssistantHost.RootOf(solutionPath)).ConfigureAwait(true);

            if (_conversationRoot is not null && !string.Equals(_conversationRoot, root, PathIdentity.Comparison))
            {
                _conversation.Clear();
                _panel.AppendNotice(Localizer.Get(StringKeys.AiNoticeNewConversation));
            }

            _conversationRoot = root;

            // One host per solution, written here on the interface thread
            // and only read by the tools.
            var host = root is null ? null
                : _host is not null && string.Equals(_host.RootDirectory, root, PathIdentity.Comparison)
                    ? _host
                    : new AiAssistantHost(_viewModel, root);

            Volatile.Write(ref _host, host);
            var tools = _tools;
            var projectContext = host is null
                ? ""
                : await Task.Run(() => AiProjectContext.Describe(_viewModel.SolutionPath, host.RootDirectory))
                    .ConfigureAwait(true);

            var context = new AiPromptContext(projectContext, GatherContext(settings, host?.RootDirectory));

            // Medium for conversation; high where the answer is an edit.
            var effort = request.Action is AiAction.FixError or AiAction.GenerateTests or AiAction.Document
                ? AiEffort.High
                : AiEffort.Medium;

            using var backend = _backendFactory(provider, apiKey, model);

            await foreach (var update in _conversation
                .AskAsync(backend, question, context, tools, effort, running.Token)
                .ConfigureAwait(true))
            {
                // A cleared or stopped conversation shows nothing more.
                running.Token.ThrowIfCancellationRequested();

                Show(update, activity);
            }
        }
        catch (OperationCanceledException)
        {
            _panel.AppendToLast("\n\n" + Localizer.Get(StringKeys.AiStopped));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException
                                      or Anthropic.Exceptions.AnthropicException)
        {
            _panel.AppendNotice(ex.Message);

            // The history is never trimmed behind the user's back (that would
            // change what a cached conversation starts with); a conversation
            // that has outgrown the model's window is started again, by them.
            if (AiErrors.IsContextTooLong(ex.Message))
                _panel.AppendNotice(Localizer.Get(StringKeys.AiNoticeContextFull));
        }
        finally
        {
            _replying = false;
            _panel.EndReply(running);

            // A clear that arrived mid-reply waits for the reply to stop: the
            // loop owns the history until it does, and only one thread may
            // change it.
            if (_clearAfterReply) ClearConversation();
        }
    }

    /// <summary>
    /// A question that cannot be sent, because no assistant or key is set up:
    /// the panel emptied its box when the question was sent, so the text goes
    /// back in it, and the status says what to do before it can be asked.
    /// </summary>
    private void RefuseQuestion(AiRequest request, string status)
    {
        _panel.InputText = request.Question;
        _panel.ShowStatus(status);
    }

    private void ClearConversation()
    {
        if (_replying)
        {
            _clearAfterReply = true;
            return;
        }

        _clearAfterReply = false;
        _conversation.Clear();
        _conversationRoot = null;
    }

    /// <summary>The host for the open solution, as the last question set it.</summary>
    private AiAssistantHost? CurrentHost() => Volatile.Read(ref _host);

    /// <summary>Turns what the loop reports into lines of the conversation.</summary>
    internal void Show(AiChatEvent update, Dictionary<string, ChatEntry> activity)
    {
        switch (update)
        {
            case AiTextChunk chunk:
                // After a tool or a notice the reply continues in a new turn.
                if (_panel.Entries.Count == 0 || _panel.Entries[^1] is not { Kind: ChatEntryKind.Message, IsUser: false })
                    _panel.Append(ChatRole.Assistant, "");

                _panel.AppendToLast(chunk.Text);
                break;

            case AiToolStarted started:
                activity[started.Call.Id] = _panel.AppendActivity(Describe(started.Call));
                break;

            case AiToolFinished finished:
                if (activity.TryGetValue(finished.Call.Id, out var entry))
                {
                    entry.Text = finished.Result.IsError
                        ? $"{Localizer.Get(StringKeys.AiToolFailed, Describe(finished.Call))}: {Clip(finished.Result.Content)}"
                        : Localizer.Get(StringKeys.AiToolUsed, Describe(finished.Call));

                    _panel.Update(entry);
                }

                if (finished.Result.Proposal is { } proposal) _panel.AppendChanges(proposal);

                break;

            case AiNotice notice:
                _panel.AppendNotice(notice.Text);
                break;
        }
    }

    /// <summary>A call as one line: the tool and its first argument, such as the path.</summary>
    internal static string Describe(AiToolCallPart call)
    {
        var first = call.Input.Select(p => p.Value).OfType<System.Text.Json.Nodes.JsonValue>()
            .Select(v => v.TryGetValue<string>(out var s) ? s : null).FirstOrDefault(s => s is { Length: > 0 });

        return first is null ? call.Name : $"{call.Name}: {Clip(first)}";
    }

    private static string Clip(string text)
    {
        var line = text.ReplaceLineEndings(" ");

        return line.Length <= 80 ? line : line[..80] + "…";
    }

    private static string ActionLabel(AiAction action) => Localizer.Get(action switch
    {
        AiAction.Explain => StringKeys.AiActionExplain,
        AiAction.FixError => StringKeys.AiActionFix,
        AiAction.GenerateTests => StringKeys.AiActionTests,
        AiAction.Document => StringKeys.AiActionDocument,
        _ => StringKeys.AiActionAsk
    });

    private static string ActionName(AiAction action) => action switch
    {
        AiAction.Explain => "Explain",
        AiAction.FixError => "Fix the error",
        AiAction.GenerateTests => "Generate tests",
        AiAction.Document => "Document",
        _ => "Ask"
    };

    /// <summary>
    /// What the assistant is told about the editor: the active file, the
    /// selection and the errors, each only if the user left that setting on.
    /// </summary>
    private string GatherContext(AiSettings settings, string? root)
    {
        if (_viewModel.ActiveDocument is not { } document) return "";

        var editor = _currentEditor();

        // The selection is code from the open file, so the switch that keeps
        // the file out of the question keeps the selection out too.
        var selected = settings.SendFileContext && editor is { SelectionLength: > 0 } ? editor.SelectedText : null;

        var path = root is not null && document.FilePath.StartsWith(root, StringComparison.Ordinal)
            ? Path.GetRelativePath(root, document.FilePath).Replace('\\', '/')
            : document.FilePath;

        var context = new AiContext
        {
            FilePath = settings.SendFileContext ? path : null,
            Language = settings.SendFileContext ? document.Language.ToString() : null,
            SelectedText = selected,
            SelectionStartLine = editor is { SelectionLength: > 0 } e1
                ? e1.Document.GetLineByOffset(e1.SelectionStart).LineNumber : 0,
            SelectionEndLine = editor is { SelectionLength: > 0 } e2
                ? e2.Document.GetLineByOffset(e2.SelectionStart + e2.SelectionLength).LineNumber : 0,
            CaretLine = settings.SendFileContext && editor is not null
                ? editor.Document.GetLineByOffset(Math.Min(editor.CaretOffset, editor.Document.TextLength)).LineNumber
                : 0,
            Errors = settings.SendErrors
                ? [.. _viewModel.Diagnostics
                    .Where(d => d.Severity == Basalt.Core.Model.DiagnosticSeverity.Error)
                    .Take(10)
                    .Select(d => $"{d.Id}: {d.Message}")]
                : []
        };

        return context.Describe();
    }

    // Accepting and rejecting

    /// <summary>
    /// Applies the files the user accepted, each as its own transaction.
    ///
    /// The same path a smart-tag fix takes: a file that is not open becomes a
    /// modified tab, nothing is saved, each buffer changes once so one undo
    /// takes it back, and a file that is not what the assistant saw is
    /// refused instead of overwritten. One stale file does not hold back the
    /// others.
    /// </summary>
    internal async Task DecideAsync(AiChangeDecision decision)
    {
        var applied = new List<string>();

        foreach (var file in decision.Files.ToList())
        {
            if (!decision.Accept)
            {
                decision.Set.MarkRejected(file);
                continue;
            }

            // Claimed before anything is awaited: a second click, or Accept
            // all after Accept, finds the file taken and leaves it alone.
            if (!decision.Set.TryBeginApply(file)) continue;

            try
            {
                var preview = new RefactoringPreview(Localizer.Get(StringKeys.AiChangesTitlePlain),
                    [new FileChangePreview(file.FilePath, file.OriginalText, file.NewText)]);

                var transaction = await EditorSmartTagTransaction.CaptureAsync(preview, _viewModel.OpenDocuments);

                await transaction.ApplyAsync();

                applied.Add(file.FilePath);
                decision.Set.MarkAccepted(file);
            }
            catch (Exception ex)
            {
                // Whatever went wrong: a file left Applying could never be
                // decided again and would keep the card's buttons off.
                decision.Set.MarkFailed(file, ex is AggregateException aggregate
                    ? aggregate.InnerExceptions[0].Message
                    : ex.Message);
            }
        }

        if (applied.Count > 0) _showTabs(applied);
    }

    /// <summary>
    /// Offers a code block from a reply as a change to the active file, with a
    /// diff to approve first. It replaces the selection, or is inserted at the
    /// caret.
    /// </summary>
    internal async Task ApplyCodeAsync(string code)
    {
        if (_currentEditor() is not { } editor || _viewModel.ActiveDocument is not { } document)
        {
            _viewModel.WriteOutput(Localizer.Get(StringKeys.AiApplyNoEditor));
            return;
        }

        var text = document.Text;
        var start = Math.Clamp(editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretOffset, 0, text.Length);
        var length = editor.SelectionLength > 0 ? Math.Min(editor.SelectionLength, text.Length - start) : 0;

        var updated = string.Concat(text.AsSpan(0, start), code, text.AsSpan(start + length));

        var preview = new RefactoringPreview(Localizer.Get(StringKeys.AiApplyTitle),
            [new FileChangePreview(document.FilePath, text, updated)]);

        if (!await _confirm(preview)) return;

        var transaction = await EditorSmartTagTransaction.CaptureAsync(preview, _viewModel.OpenDocuments);

        await transaction.ApplyAsync();
    }
}

/// <summary>
/// What the assistant talks through: where its key is kept and how a backend
/// is made for a conversation. The window uses the real ones; a test hands in
/// an in-memory key store and a fake backend, so no keychain is touched and no
/// model provider is called.
/// </summary>
internal sealed record AiAssistantServices(
    IApiKeyStore Keys,
    Func<IAiChatProvider, string, string, IAiChatBackend> BackendFactory);

/// <summary>What the assistant's tools get from the running IDE.</summary>
internal sealed class AiAssistantHost(MainWindowViewModel viewModel, string root) : IAiWorkspaceHost
{
    public string RootDirectory { get; } = root;

    /// <summary>The folder of the open solution or project, or the folder itself.</summary>
    internal static string? RootOf(string? solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath)) return null;

        // The one canonical spelling, so what the editors hold, what the
        // sandbox allows and what a change names are the same strings.
        var full = Path.GetFullPath(solutionPath);
        var folder = Directory.Exists(full) ? full : Path.GetDirectoryName(full);

        return folder is null ? null : PathIdentity.Canonical(folder);
    }

    public async Task<IReadOnlyDictionary<string, string>> SnapshotOpenBuffersAsync(CancellationToken ct) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var buffers = new Dictionary<string, string>(StringComparer.Ordinal);

            // A designer tab and a text tab can share a path; the text is the one that is read.
            foreach (var document in viewModel.OpenDocuments.Where(d => !d.OpenInDesigner))
                buffers[document.FilePath] = document.Text;

            return (IReadOnlyDictionary<string, string>)buffers;
        });

    public async Task<IReadOnlyList<AiDiagnosticInfo>> GetDiagnosticsAsync(string? fullPath, CancellationToken ct)
    {
        var found = await viewModel.GetDiagnosticsForAssistantAsync(fullPath, ct).ConfigureAwait(false);

        return [.. found
            .Where(d => fullPath is null || d.FilePath is not null && PathIdentity.Same(d.FilePath, fullPath))
            .Where(d => d.Severity is Basalt.Core.Model.DiagnosticSeverity.Error
                or Basalt.Core.Model.DiagnosticSeverity.Warning)
            .Select(d => new AiDiagnosticInfo(d.Id, d.Message, d.Severity.ToString(), d.FilePath, d.Line, d.Column))];
    }

    public async Task<IReadOnlyList<AiSymbolInfo>> FindSymbolsAsync(string name, CancellationToken ct)
    {
        var found = await viewModel.SearchSymbolDetailsForAssistantAsync(name, ct).ConfigureAwait(false);

        return [.. found.Select(s => new AiSymbolInfo(
            s.Name, s.Kind.ToString(), s.Location.FilePath, s.Location.Range.Start.Line, s.Location.Range.Start.Column))];
    }
}
