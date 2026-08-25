using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Services;

namespace Basalt.Shell;

/// <summary>
/// Where a breakpoint's condition is set.
///
/// Two ways to narrow when a breakpoint stops: an expression that must hold,
/// and a number of passes to wait for. Both are what the debugger evaluates,
/// so what is typed here is what it sees.
/// </summary>
public sealed class BreakpointConditionDialog : Window
{
    private readonly TextBox _condition;
    private readonly TextBox _hitCount;
    private readonly TextBlock _preview;

    /// <summary>The breakpoint as the user left it, or null if they cancelled.</summary>
    public Breakpoint? Result { get; private set; }

    public BreakpointConditionDialog() : this(new Breakpoint("", 1)) { }

    public BreakpointConditionDialog(Breakpoint breakpoint)
    {
        _filePath = breakpoint.FilePath;

        Title = $"Breakpoint — {Path.GetFileName(breakpoint.FilePath)}:{breakpoint.Line}";
        Width = 480;
        Height = 300;

        // A floor, so resizing cannot hide what matters.
        MinWidth = 380; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _condition = new TextBox
        {
            Text = breakpoint.Condition ?? "",
            PlaceholderText = "i = 4"
        };

        _preview = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.7,
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            TextWrapping = TextWrapping.Wrap
        };

        _condition.TextChanged += (_, _) => UpdatePreview(breakpoint.FilePath);

        _hitCount = new TextBox
        {
            Text = breakpoint.HitCount?.ToString() ?? "",
            PlaceholderText = "Every time"
        };

        UpdatePreview(breakpoint.FilePath);

        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Accept(breakpoint);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, ok }
        };

        var layout = new StackPanel { Spacing = 12, Margin = new Thickness(20) };

        layout.Children.Add(Labelled(
            "Stop only when this is true",
            _condition,
            "Written in Visual Basic: \"i = 4\", \"name <> \"\"\"\" AndAlso total > 10\"."));

        layout.Children.Add(_preview);

        layout.Children.Add(Labelled(
            "Skip this many passes first",
            _hitCount,
            "Leave empty to stop every time."));

        layout.Children.Add(buttons);

        Content = layout;

        // Focused when the dialog opens, so it can be used without
        // reaching for the mouse first.
        Opened += (_, _) => _condition.Focus();
    }

    /// <summary>The file this breakpoint is in, which decides the translation.</summary>
    private readonly string _filePath;

    internal string ConditionText
    {
        get => _condition.Text ?? "";
        set
        {
            _condition.Text = value;

            // TextChanged does not fire for a programmatic change, so the
            // preview would otherwise show the previous condition.
            UpdatePreview(_filePath);
        }
    }

    internal string HitCountText
    {
        get => _hitCount.Text ?? "";
        set => _hitCount.Text = value;
    }

    internal string PreviewText => _preview.Text ?? "";

    /// <summary>
    /// Shows what the debugger will actually evaluate.
    ///
    /// Its expression evaluator reads C# whatever the program is written in,
    /// so "i = 4" becomes "i == 4". Showing the translation means a surprising
    /// result is explainable rather than mysterious.
    /// </summary>
    private void UpdatePreview(string filePath)
    {
        var condition = (_condition.Text ?? "").Trim();

        if (condition.Length == 0)
        {
            _preview.Text = "";
            return;
        }

        if (!IsBasic(filePath))
        {
            _preview.Text = "";
            return;
        }

        var translated = Basalt.Workspace.Debugging.VbConditionTranslator
            .ToEvaluatorSyntax(condition);

        _preview.Text = translated == condition
            ? ""
            : $"The debugger evaluates:  {translated}";
    }

    private static bool IsBasic(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() is ".vb" or ".vbhtml" or ".bas";

    internal void Accept(Breakpoint breakpoint)
    {
        var condition = (_condition.Text ?? "").Trim();

        // A hit count that is not a number is treated as none rather than
        // refused: the user meant "every time" and typed something odd.
        var hits = int.TryParse((_hitCount.Text ?? "").Trim(), out var parsed) && parsed > 0
            ? parsed
            : (int?)null;

        Result = breakpoint with
        {
            Condition = condition.Length == 0 ? null : condition,
            HitCount = hits
        };

        Close();
    }

    private static Control Labelled(string label, Control editor, string hint)
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(editor);
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        });

        return panel;
    }
}
