using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Workspace.Debugging;

namespace Basalt.Shell;

/// <summary>
/// The running .NET processes the debugger can attach to: name, process id and title, refreshed on demand.
///
/// The processes are found by the loader the caller gives, awaited here so that the dialog opens at once and the
/// list fills in when the search ends: finding them reads the system's process table and, on macOS, runs "ps",
/// none of which belongs on the interface thread.
/// </summary>
public sealed class AttachToProcessDialog : Window
{
    private readonly Func<Task<IReadOnlyList<DotnetProcessInfo>>> _load;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private readonly Button _attach;
    private readonly Button _refresh;

    /// <summary>Counts searches, so that an older one finishing late does not overwrite a newer list.</summary>
    private int _search;

    /// <summary>The process the user chose, or null if they cancelled.</summary>
    public DotnetProcessInfo? Result { get; private set; }

    public AttachToProcessDialog(Func<Task<IReadOnlyList<DotnetProcessInfo>>> load)
    {
        _load = load;

        Title = Localizer.Get(StringKeys.AttachProcessTitle);
        Width = 720;
        Height = 460;

        // A floor, so resizing cannot hide what matters.
        MinWidth = 520; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _status = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<DotnetProcessInfo>((process, _) => BuildRow(process))
        };
        _list.SelectionChanged += (_, _) => _attach!.IsEnabled = _list.SelectedItem is not null;
        _list.DoubleTapped += (_, _) => Accept();

        _attach = new Button { Content = Localizer.Get(StringKeys.AttachProcessAttach), IsDefault = true, IsEnabled = false };
        _attach.Click += (_, _) => Accept();

        _refresh = new Button { Content = Localizer.Get(StringKeys.AttachProcessRefresh) };
        _refresh.Click += (_, _) => _ = RefreshAsync();

        var cancel = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), IsCancel = true };
        cancel.Click += (_, _) => Close();

        var top = new StackPanel
        {
            Margin = new Thickness(16, 16, 16, 8),
            Spacing = 6,
            Children = { Header(), _status }
        };

        var bottom = new StackPanel
        {
            Margin = new Thickness(16, 8, 16, 16),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.Get(StringKeys.AttachProcessNote),
                    FontSize = 11,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap
                },
                DialogButtons.Row(_attach, _refresh, cancel)
            }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        layout.Children.Add(_list);

        Content = layout;

        Opened += (_, _) => _ = RefreshAsync();
    }

    /// <summary>What the list offers, for tests.</summary>
    internal IReadOnlyList<DotnetProcessInfo> Choices =>
        _list.ItemsSource?.Cast<DotnetProcessInfo>().ToList() ?? [];

    /// <summary>The note under the list: what is being done, or why it is empty.</summary>
    internal string StatusTextForTests => _status.Text ?? "";

    /// <summary>Chooses a row as a click would.</summary>
    internal void SelectForTests(int processId) =>
        _list.SelectedItem = Choices.FirstOrDefault(p => p.ProcessId == processId);

    /// <summary>The Attach button's action, for tests that cannot double-click.</summary>
    internal void AcceptForTests() => Accept();

    /// <summary>Looks for the processes again; resolved once the list shows the result.</summary>
    internal async Task RefreshAsync()
    {
        var search = ++_search;

        _status.Text = Localizer.Get(StringKeys.AttachProcessLooking);
        _refresh.IsEnabled = false;

        IReadOnlyList<DotnetProcessInfo> found;

        try
        {
            found = await _load().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (search == _search)
            {
                _status.Text = ex.Message;
                _refresh.IsEnabled = true;
            }

            return;
        }

        // A newer search was asked for while this one ran: its answer is the one to show.
        if (search != _search) return;

        var chosen = (_list.SelectedItem as DotnetProcessInfo)?.ProcessId;

        _list.ItemsSource = found;
        _list.SelectedItem = found.FirstOrDefault(p => p.ProcessId == chosen);
        _status.Text = found.Count == 0 ? Localizer.Get(StringKeys.AttachProcessNone) : "";
        _refresh.IsEnabled = true;
    }

    private void Accept()
    {
        if (_list.SelectedItem is not DotnetProcessInfo process) return;

        Result = process;
        Close();
    }

    private static Grid Columns()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,70,*") };
        return grid;
    }

    private static Control Header()
    {
        var grid = Columns();

        foreach (var (text, column) in new[]
        {
            (Localizer.Get(StringKeys.AttachProcessColumnName), 0),
            (Localizer.Get(StringKeys.AttachProcessColumnId), 1),
            (Localizer.Get(StringKeys.AttachProcessColumnTitle), 2)
        })
        {
            var cell = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 12 };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static Control BuildRow(DotnetProcessInfo? process)
    {
        if (process is null) return new TextBlock();

        var grid = Columns();

        foreach (var (text, column) in new[]
        {
            (process.Name, 0),
            (process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), 1),
            (process.Title, 2)
        })
        {
            var cell = new TextBlock
            {
                Text = text,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        return grid;
    }
}
