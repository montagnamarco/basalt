using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;

namespace Basalt.Shell;

/// <summary>A file offered in Go to File.</summary>
public sealed record FileChoice(string Name, string FilePath, string Folder)
{
    /// <summary>Where the file sits, relative to the solution folder; empty at the top.</summary>
    public string Detail => Folder;
}

/// <summary>
/// Opening a file by typing part of its name.
///
/// The file names are gathered once, when the dialog is asked for, and the
/// list narrows as the user types. Built in code, like the symbol dialog it
/// sits beside, so the list and its filtering stay in one place.
/// </summary>
public sealed class GoToFileDialog : Window
{
    /// <summary>Folders never offered: build output and tool state, not source.</summary>
    private static readonly string[] ExcludedFolders = ["bin", "obj", ".git", ".vs", "node_modules"];

    /// <summary>A solution folder with more files than this is cut off rather than listed in full.</summary>
    private const int MaxFiles = 5000;

    private readonly TextBox _query;
    private readonly ListBox _list;
    private readonly TextBlock _summary;

    private readonly IReadOnlyList<FileChoice> _all;

    public GoToFileDialog(IReadOnlyList<FileChoice> files)
    {
        _all = files;

        Title = Localizer.Get(StringKeys.MenuNavigateGoToFile);
        Width = 620;
        Height = 420;

        // A floor, so resizing cannot hide what matters.
        MinWidth = 420; MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _query = new TextBox { PlaceholderText = Localizer.Get(StringKeys.GoToFilePlaceholder) };
        _query.TextChanged += (_, _) => ApplyFilter(_query.Text ?? "");
        _query.KeyDown += OnQueryKeyDown;

        _summary = new TextBlock { FontSize = 11, Opacity = 0.7 };

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<FileChoice>((choice, _) => BuildRow(choice))
        };
        _list.DoubleTapped += (_, _) => Accept();

        var top = new StackPanel
        {
            Margin = new Thickness(16, 16, 16, 8),
            Spacing = 6,
            Children = { _query, _summary }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(_list);

        Content = layout;

        ApplyFilter("");
        Opened += (_, _) => _query.Focus();
    }

    /// <summary>What the list currently offers, exposed for tests.</summary>
    internal IReadOnlyList<FileChoice> Choices =>
        _list.ItemsSource?.Cast<FileChoice>().ToList() ?? [];

    /// <summary>The search box, so a test can raise a real key event on it.</summary>
    internal TextBox QueryBoxForTests => _query;

    /// <summary>
    /// The files under a solution folder, as Go to File offers them.
    ///
    /// Walks the folder without the build output or tool folders, sorted by
    /// name so that the list reads the same every time it is opened.
    /// </summary>
    public static IReadOnlyList<FileChoice> ListFiles(string solutionFolder)
    {
        var files = new List<FileChoice>();
        if (!Directory.Exists(solutionFolder)) return files;

        var pending = new Stack<string>();
        pending.Push(solutionFolder);

        while (pending.Count > 0 && files.Count < MaxFiles)
        {
            var folder = pending.Pop();

            foreach (var subfolder in SafeEntries(() => Directory.EnumerateDirectories(folder)))
            {
                if (!ExcludedFolders.Contains(Path.GetFileName(subfolder), StringComparer.OrdinalIgnoreCase))
                    pending.Push(subfolder);
            }

            foreach (var file in SafeEntries(() => Directory.EnumerateFiles(folder)))
            {
                var relative = Path.GetRelativePath(solutionFolder, Path.GetDirectoryName(file) ?? solutionFolder);
                var folderName = relative == "." ? "" : relative;

                files.Add(new FileChoice(Path.GetFileName(file), file, folderName));
            }
        }

        return files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> SafeEntries(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder out of reach is skipped rather than failing the whole list.
            return [];
        }
    }

    private static Control BuildRow(FileChoice? choice)
    {
        if (choice is null) return new TextBlock();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        row.Children.Add(new TextBlock
        {
            Text = choice.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (choice.Detail.Length > 0)
            row.Children.Add(new TextBlock
            {
                Text = choice.Detail,
                FontSize = 11,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center
            });

        return row;
    }

    /// <summary>
    /// Narrows the list to file names containing the text typed.
    ///
    /// Names starting with the text sort first, so "util" offers Utilities.vb
    /// ahead of Notes-about-utilities.txt.
    /// </summary>
    private void ApplyFilter(string query)
    {
        var matches = string.IsNullOrWhiteSpace(query)
            ? _all
            : _all
                .Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(f => f.Name.Length)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var shown = matches.Take(200).ToList();

        _list.ItemsSource = shown;
        if (shown.Count > 0) _list.SelectedIndex = 0;

        _summary.Text = matches.Count > shown.Count
            ? Localizer.Get(StringKeys.GoToFileShownOf, shown.Count, matches.Count)
            : Localizer.Get(StringKeys.GoToFileShown, shown.Count);
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        // Arrow keys move through the list while the caret stays in the box.
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Accept();
                e.Handled = true;
                break;

            case Key.Escape:
                Close(null);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        var count = Choices.Count;
        if (count == 0) return;

        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    private void Accept() => Close(_list.SelectedItem as FileChoice);
}
