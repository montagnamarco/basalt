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
/// What the assistant is told about the code beside the question.
///
/// Gathered by the window and passed in, so the panel needs to know nothing
/// about editors or diagnostics.
/// </summary>
public sealed record AiContext
{
    public string? FilePath { get; init; }
    public string? Language { get; init; }
    public string? SelectedText { get; init; }
    public string? FileText { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// The context as the assistant is told it.
    ///
    /// The selection is preferred to the whole file: it is what the user is
    /// asking about, and a large file would crowd out the question.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string>();

        if (FilePath is { Length: > 0 })
            parts.Add($"File: {Path.GetFileName(FilePath)}");

        if (Language is { Length: > 0 })
            parts.Add($"Language: {Language}");

        if (SelectedText is { Length: > 0 })
            parts.Add($"Selected code:\n```\n{SelectedText}\n```");
        else if (FileText is { Length: > 0 })
            parts.Add($"File contents:\n```\n{Trim(FileText)}\n```");

        if (Errors.Count > 0)
            parts.Add("Current errors:\n" + string.Join("\n", Errors.Take(10)));

        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Keeps a large file within reason.
    ///
    /// A whole file can exceed what the assistant will read, and the part
    /// nearest the top is the part that names the types.
    /// </summary>
    private static string Trim(string text) =>
        text.Length <= 8000 ? text : text[..8000] + "\n… (truncated)";
}

/// <summary>One turn of the conversation, as the panel shows it.</summary>
public sealed class ChatEntry
{
    public ChatEntry(ChatRole role, string text)
    {
        Role = role;
        Text = text;
    }

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

    private readonly List<ChatEntry> _entries = [];
    private CancellationTokenSource? _running;

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

        _input.KeyDown += (_, e) =>
        {
            // Enter sends; Shift+Enter writes a new line, as every chat does.
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

            e.Handled = true;
            RequestSend();
        };

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
        Refresh();

        return entry;
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

        Refresh();
        _scroller.ScrollToEnd();
    }

    public void Clear()
    {
        _entries.Clear();
        Refresh();
    }

    /// <summary>Stops a reply that is still arriving.</summary>
    public void Cancel()
    {
        _running?.Cancel();
        _running = null;

        ShowSendState(replying: false);
    }

    /// <summary>Says a reply is on its way, so the button can stop it.</summary>
    public void BeginReply(CancellationTokenSource running)
    {
        _running = running;
        ShowSendState(replying: true);
    }

    public void EndReply()
    {
        _running = null;
        ShowSendState(replying: false);
    }

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
            Cancel();
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

    private void Refresh() => _history.ItemsSource = _entries.ToList();

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
