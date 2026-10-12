using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.AI;
using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Shell.Controls;

/// <summary>What the assistant is being asked to do.</summary>
public enum AiAction { Ask, Explain, FixError, GenerateTests, Document }

/// <summary>
/// What the assistant is told about the editor beside the question.
///
/// Gathered by the window and passed in, so the panel needs to know nothing
/// about editors or diagnostics. The file itself is not sent: the assistant
/// reads it (the editor's text, saved or not) with its own tools, which is
/// what replaced cutting every file off at a fixed length.
/// </summary>
public sealed record AiContext
{
    /// <summary>Longest selection sent as it is; a selection beyond it is read through the tools.</summary>
    internal const int MaxSelectionChars = 30000;

    public string? FilePath { get; init; }
    public string? Language { get; init; }
    public string? SelectedText { get; init; }
    public int SelectionStartLine { get; init; }
    public int SelectionEndLine { get; init; }
    public int CaretLine { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public string Describe()
    {
        var parts = new List<string>();

        if (FilePath is { Length: > 0 })
            parts.Add($"Active file: {FilePath}"
                + (Language is { Length: > 0 } ? $" ({Language})" : "")
                + (CaretLine > 0 ? $", caret on line {CaretLine}" : ""));

        if (SelectedText is { Length: > 0 })
        {
            var shown = SelectedText.Length <= MaxSelectionChars
                ? SelectedText
                : SelectedText[..MaxSelectionChars] + "\n… (selection cut here; read the file for the rest)";

            parts.Add($"Selected code (lines {SelectionStartLine}-{SelectionEndLine}):\n```\n{shown}\n```");
        }

        if (Errors.Count > 0)
            parts.Add("Current errors:\n" + string.Join("\n", Errors.Take(10)));

        return string.Join("\n\n", parts);
    }
}

/// <summary>What a line of the conversation is.</summary>
public enum ChatEntryKind { Message, Activity, Notice, Changes }

/// <summary>One turn of the conversation, as the panel shows it.</summary>
public sealed class ChatEntry
{
    public ChatEntry(ChatRole role, string text, ChatEntryKind kind = ChatEntryKind.Message)
    {
        Role = role;
        Text = text;
        Kind = kind;
    }

    public ChatEntryKind Kind { get; }

    /// <summary>The proposal a <see cref="ChatEntryKind.Changes"/> entry shows.</summary>
    public Basalt.Workspace.Ai.AiChangeSet? Changes { get; init; }

    public ChatRole Role { get; }

    public string Text { get; set; }

    public bool IsUser => Role == ChatRole.User;
}

/// <summary>
/// A conversation with an assistant, beside the code.
///
/// The reply is shown as it arrives rather than when it is finished: an
/// assistant takes seconds to answer, and a panel that sits blank throughout
/// looks broken.
/// </summary>
public sealed class AiChatPanel : UserControl
{
    private readonly ItemsControl _history;
    private readonly ScrollViewer _scroller;
    private readonly TextBox _input;
    private readonly Button _send;
    private readonly TextBlock _status;
    private readonly ComboBox _actions;

    private readonly System.Collections.ObjectModel.ObservableCollection<ChatEntry> _entries = [];
    private CancellationTokenSource? _running;
    private bool _stopping;

    public AiChatPanel()
    {
        _status = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = TextWrapping.Wrap
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _history = new ItemsControl
        {
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ChatEntry>(
                (entry, _) => BuildTurn(entry))
        };

        // Bound once: only the entry that changed is rebuilt, which matters
        // while a reply streams in and a proposal's diffs sit above it.
        _history.ItemsSource = _entries;

        _scroller = new ScrollViewer { Content = _history };

        _actions = new ComboBox
        {
            ItemsSource = new[]
            {
                Localizer.Get(StringKeys.AiActionAsk), Localizer.Get(StringKeys.AiActionExplain),
                Localizer.Get(StringKeys.AiActionFix), Localizer.Get(StringKeys.AiActionTests),
                Localizer.Get(StringKeys.AiActionDocument)
            },
            SelectedIndex = 0,
            FontSize = PanelMetrics.FontSize,
            Width = 130
        };

        _input = new TextBox
        {
            PlaceholderText = Localizer.Get(StringKeys.AiAskPlaceholder),
            AcceptsReturn = true,
            MinHeight = 60,
            MaxHeight = 140,
            FontSize = PanelMetrics.FontSize,
            TextWrapping = TextWrapping.Wrap
        };

        // Tunnelling, so this sees Enter before the box does. A multi-line
        // TextBox takes Enter as a new line in its own bubbling handler, which
        // marks the key handled first: a bubbling handler here never ran, and
        // Enter wrote a line break instead of sending the question.
        _input.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            // Shift+Enter writes a new line, as every chat does, so it is left
            // to the box. An input method's keys are reported as
            // Key.ImeProcessed rather than Enter, so the Enter that confirms a
            // composition is not Key.Enter here and is not sent either.
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

            e.Handled = true;

            // Enter while an answer streams does nothing: the question stays in
            // the box to be sent once the answer is finished. Stopping is the
            // button's job, so a stray Enter cannot end an answer.
            if (_running is not null) return;

            RequestSend();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _send = new Button { Content = Localizer.Get(StringKeys.AiSend), FontSize = PanelMetrics.FontSize };
        _send.Click += (_, _) => RequestSend();

        var clear = new Button { Content = Localizer.Get(StringKeys.AiClear), FontSize = PanelMetrics.FontSize };
        ToolTip.SetTip(clear, Localizer.Get(StringKeys.AiClearTip));
        ToolTip.SetTip(_send, Localizer.Get(StringKeys.AiSendTip));
        clear.Click += (_, _) => Clear();

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 4)
        };

        controls.Children.Add(_actions);
        controls.Children.Add(_send);
        controls.Children.Add(clear);

        var bottom = new StackPanel { Spacing = 2 };
        bottom.Children.Add(_input);
        bottom.Children.Add(controls);

        var layout = new DockPanel();

        DockPanel.SetDock(_status, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom);

        layout.Children.Add(_status);
        layout.Children.Add(bottom);
        layout.Children.Add(_scroller);

        Content = layout;

        ShowStatus(Localizer.Get(StringKeys.AiNoAssistant));
    }

    /// <summary>Raised when the user asks something.</summary>
    public event EventHandler<AiRequest>? Asked;

    /// <summary>Raised when the user accepts or rejects proposed changes.</summary>
    public event EventHandler<AiChangeDecision>? ChangesDecided;

    /// <summary>Raised when the user asks to apply a code block from a reply.</summary>
    public event EventHandler<string>? ApplyRequested;

    internal IReadOnlyList<ChatEntry> Entries => _entries;

    internal string StatusText => _status.Text ?? "";

    /// <summary>The button that sends, and stops while a reply is arriving.</summary>
    internal Button SendButton => _send;

    internal string InputText
    {
        get => _input.Text ?? "";
        set => _input.Text = value;
    }

    /// <summary>Which action the user picked beside the question.</summary>
    public AiAction Action => (AiAction)Math.Max(0, _actions.SelectedIndex);

    public void ShowStatus(string message) => _status.Text = message;

    /// <summary>Adds a turn to the conversation and shows it.</summary>
    public ChatEntry Append(ChatRole role, string text)
    {
        var entry = new ChatEntry(role, text);

        _entries.Add(entry);
        _scroller.ScrollToEnd();

        return entry;
    }

    /// <summary>
    /// Shows a line of what the assistant is doing, such as a file it read.
    ///
    /// Replaces an empty reply waiting at the end, so the activity is not
    /// preceded by a blank turn.
    /// </summary>
    public ChatEntry AppendActivity(string text)
    {
        RemoveEmptyReply();
        return AppendEntry(new ChatEntry(ChatRole.Assistant, text, ChatEntryKind.Activity));
    }

    /// <summary>Shows something the IDE says, as distinct from the assistant.</summary>
    public ChatEntry AppendNotice(string text)
    {
        RemoveEmptyReply();
        return AppendEntry(new ChatEntry(ChatRole.Assistant, text, ChatEntryKind.Notice));
    }

    /// <summary>Shows proposed changes for the user to decide on.</summary>
    public ChatEntry AppendChanges(Basalt.Workspace.Ai.AiChangeSet set)
    {
        RemoveEmptyReply();

        var entry = AppendEntry(new ChatEntry(ChatRole.Assistant, set.Summary, ChatEntryKind.Changes) { Changes = set });

        // A decision changes the card; only this entry is rebuilt.
        set.Changed += (_, _) => Update(entry);

        return entry;
    }

    /// <summary>Redraws one entry after its text or state changed.</summary>
    public void Update(ChatEntry entry)
    {
        var index = _entries.IndexOf(entry);

        if (index >= 0) _entries[index] = entry;
    }

    private ChatEntry AppendEntry(ChatEntry entry)
    {
        _entries.Add(entry);
        _scroller.ScrollToEnd();

        return entry;
    }

    private void RemoveEmptyReply()
    {
        if (_entries.Count > 0 && _entries[^1] is { Kind: ChatEntryKind.Message, IsUser: false, Text.Length: 0 } empty)
            _entries.Remove(empty);
    }

    /// <summary>
    /// Adds text to the last turn, as a streamed reply arrives.
    ///
    /// The whole list is rebuilt rather than the one item updated: a chat is
    /// short, and a per-item binding would be more machinery than the saving
    /// is worth.
    /// </summary>
    public void AppendToLast(string text)
    {
        if (_entries.Count == 0) return;

        _entries[^1].Text += text;

        Update(_entries[^1]);
        _scroller.ScrollToEnd();
    }

    /// <summary>Raised when the conversation is cleared, so the history behind it can go too.</summary>
    public event EventHandler? Cleared;

    public void Clear()
    {
        // A reply still arriving belongs to the conversation being cleared.
        Cancel();

        _entries.Clear();
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops a reply that is still arriving.</summary>
    public void Cancel()
    {
        if (_running is null) return;

        _running.Cancel();

        // The reply has been told to stop, not stopped: until it has, a new
        // question would start a second loop on the same conversation. The
        // button stays unavailable until EndReply says it is over.
        _stopping = true;
        _send.IsEnabled = false;
    }

    /// <summary>Says a reply is on its way, so the button can stop it.</summary>
    public void BeginReply(CancellationTokenSource running)
    {
        _running = running;
        ShowSendState(replying: true);
    }

    /// <summary>
    /// Says the reply is over. Only the reply that is current can end it: an
    /// older one finishing late must not end (or clear) a newer one.
    /// </summary>
    public void EndReply(CancellationTokenSource running)
    {
        if (!ReferenceEquals(_running, running)) return;

        _running = null;
        _stopping = false;
        _send.IsEnabled = true;
        ShowSendState(replying: false);
    }

    /// <summary>Whether a reply is still arriving or stopping.</summary>
    internal bool IsReplying => _running is not null;

    // The label and its tooltip change together: a "Stop" button that still
    // says "Send the question" in its tooltip is worse than none.
    private void ShowSendState(bool replying)
    {
        _send.Content = Localizer.Get(replying ? StringKeys.AiStop : StringKeys.AiSend);
        ToolTip.SetTip(_send, Localizer.Get(replying ? StringKeys.AiStopTip : StringKeys.AiSendTip));
    }

    internal void RequestSend()
    {
        // While a reply is arriving the button stops it instead.
        if (_running is not null)
        {
            if (!_stopping) Cancel();
            return;
        }

        var question = (_input.Text ?? "").Trim();

        // An action other than Ask carries its own instruction, so the box
        // may be empty.
        if (question.Length == 0 && Action == AiAction.Ask) return;

        _input.Text = "";

        Asked?.Invoke(this, new AiRequest(question, Action));
    }

    internal void RequestApply(string code) => ApplyRequested?.Invoke(this, code);

    /// <summary>
    /// Draws one turn.
    ///
    /// Code blocks are separated from prose so they can be read as code and
    /// applied to the file with a button, which is most of what an assistant
    /// is useful for here.
    /// </summary>
    private Control BuildTurn(ChatEntry? entry)
    {
        if (entry is null) return new TextBlock();

        if (entry.Kind == ChatEntryKind.Changes && entry.Changes is { } set)
            return new Border
            {
                Margin = new Thickness(Spacing.Normal, Spacing.Tight),
                Child = new AiChangeSetCard(set, decision => ChangesDecided?.Invoke(this, decision))
            };

        if (entry.Kind is ChatEntryKind.Activity or ChatEntryKind.Notice)
            return new TextBlock
            {
                Text = entry.Text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = PanelMetrics.SecondaryFontSize,
                FontStyle = entry.Kind == ChatEntryKind.Activity ? FontStyle.Italic : FontStyle.Normal,
                Margin = new Thickness(Spacing.Normal, 2)
            }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(8, 6) };

        stack.Children.Add(new TextBlock
        {
            Text = Localizer.Get(entry.IsUser ? StringKeys.AiYou : StringKeys.AiAssistant),
            FontWeight = FontWeight.SemiBold,
            FontSize = PanelMetrics.SecondaryFontSize
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush"));

        foreach (var part in MarkdownBlocks.Split(entry.Text))
        {
            stack.Children.Add(part.IsCode
                ? BuildCodeBlock(part.Text)
                : new TextBlock
                {
                    Text = part.Text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = PanelMetrics.FontSize
                });
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(4),
            Child = stack
        };

        // The user's turns sit on a wash of the editor's own text colour: a
        // fixed grey at 8 percent was a pale tint on white and invisible on a
        // dark surface, and does not follow the theme when it changes.
        if (entry.IsUser) card.ThemedTint(Border.BackgroundProperty, "EditorForeground", 0.08);

        return card;
    }

    private Control BuildCodeBlock(string code)
    {
        var text = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = PanelMetrics.FontSize,
            BorderThickness = new Thickness(0)
        };

        var apply = new Button
        {
            Content = Localizer.Get(StringKeys.AiApply),
            FontSize = PanelMetrics.FontSize,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 0)
        };

        apply.Click += (_, _) => RequestApply(code);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(text);
        stack.Children.Add(apply);

        var block = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(Spacing.Small, Spacing.Tight),
            Child = stack
        };

        block.ThemedTint(Border.BackgroundProperty, "EditorForeground", 0.10);

        return block;
    }
}

/// <summary>Something the user asked the assistant.</summary>
public sealed record AiRequest(string Question, AiAction Action);

/// <summary>A piece of a reply: prose, or a block of code.</summary>
public readonly record struct MarkdownBlock(string Text, bool IsCode);

/// <summary>
/// Splits a reply into prose and code.
///
/// Assistants answer in Markdown, and a fenced block is the part worth
/// showing as code and offering to apply. Only the fences matter here; the
/// rest of Markdown is left as it is written.
/// </summary>
public static class MarkdownBlocks
{
    public static IReadOnlyList<MarkdownBlock> Split(string text)
    {
        var blocks = new List<MarkdownBlock>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var current = new System.Text.StringBuilder();
        var inCode = false;

        void Flush()
        {
            var content = current.ToString().Trim('\n');

            if (content.Length > 0) blocks.Add(new MarkdownBlock(content, inCode));

            current.Clear();
        }

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                inCode = !inCode;
                continue;
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(line);
        }

        Flush();

        return blocks;
    }

    /// <summary>The code in a reply, joined; empty when there is none.</summary>
    public static string CodeIn(string text) =>
        string.Join("\n\n", Split(text).Where(b => b.IsCode).Select(b => b.Text));
}
