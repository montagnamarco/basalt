using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Workspace.Refactoring;

namespace Basalt.Shell;

/// <summary>
/// What a refactoring will change, shown before it happens.
///
/// A change spread over several files is one the user should see first:
/// undoing it afterwards means undoing each file separately, which is not
/// what anyone expects from a single command.
/// </summary>
public sealed class RefactoringPreviewDialog : Window
{
    /// <summary>Whether the user asked for the change to be made.</summary>
    public bool Accepted { get; private set; }

    public RefactoringPreviewDialog() : this(
        new RefactoringPreview("Preview", [])) { }

    public RefactoringPreviewDialog(RefactoringPreview preview)
    {
        Title = preview.Title;
        Width = 720;
        Height = 520;

        // A floor, so resizing cannot hide the buttons.
        MinWidth = 480; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var apply = new Button
        {
            Content = "Apply",
            IsDefault = true,
            IsEnabled = preview.CanApply,
            Classes = { "primary" }
        };

        var cancel = new Button { Content = "Cancel", IsCancel = true };

        apply.Click += (_, _) => { Accepted = true; Close(); };
        cancel.Click += (_, _) => Close();

        Content = new DockPanel
        {
            Children =
            {
                Header(preview),
                Footer(apply, cancel),
                Body(preview)
            }
        };

        // Focused when the dialog opens: the user is deciding yes or no, and
        // should be able to answer from the keyboard.
        Opened += (_, _) => apply.Focus();
    }

    internal bool CanApply => preview_CanApply;

    private bool preview_CanApply;

    private Control Header(RefactoringPreview preview)
    {
        preview_CanApply = preview.CanApply;

        var text = preview.Problem is { Length: > 0 } problem
            ? problem
            : $"{preview.Changes.Count} file(s) will change, "
            + $"{preview.Changes.Sum(c => c.ChangedLines.Count)} line(s) in all.";

        var header = new TextBlock
        {
            Text = text,
            Margin = new Thickness(16, 12),
            FontSize = 12,
            Foreground = preview.Problem is null ? null : Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap
        };

        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);

        return header;
    }

    private static Control Footer(Button apply, Button cancel)
    {
        var footer = new Border
        {
            Padding = new Thickness(16, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, apply }
            }
        };

        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);

        return footer;
    }

    /// <summary>
    /// The changes, file by file.
    ///
    /// Only the lines that differ: showing two whole files side by side would
    /// ask the user to find the change themselves.
    /// </summary>
    private static Control Body(RefactoringPreview preview)
    {
        var files = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<FileChangePreview>(
                (change, _) => BuildFile(change)),
            ItemsSource = preview.Changes
        };

        return new ScrollViewer { Content = files };
    }

    private static Control BuildFile(FileChangePreview? change)
    {
        if (change is null) return new TextBlock();

        var lines = new StackPanel { Spacing = 1, Margin = new Thickness(12, 4) };

        foreach (var (line, before, after) in change.ChangedLines.Take(200))
        {
            lines.Children.Add(Row(line, before, removed: true));
            lines.Children.Add(Row(line, after, removed: false));
        }

        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 6) };

        stack.Children.Add(new TextBlock
        {
            Text = change.FileName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(16, 0)
        });

        stack.Children.Add(lines);

        return stack;
    }

    /// <summary>One line, coloured as git colours a removal or an addition.</summary>
    private static Control Row(int line, string text, bool removed)
    {
        return new Border
        {
            Background = new SolidColorBrush(
                removed ? Color.FromRgb(0xCF, 0x22, 0x2E) : Color.FromRgb(0x22, 0x86, 0x36),
                0.12),
            Padding = new Thickness(6, 2),
            Child = new TextBlock
            {
                Text = $"{line,4}  {(removed ? "-" : "+")} {text}",
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = 11
            }
        };
    }
}
