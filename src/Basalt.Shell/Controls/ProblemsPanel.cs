using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>A diagnostic as the list shows it: the path relative to the solution, the full one for the tooltip.</summary>
public sealed record ProblemRow(IdeDiagnostic Diagnostic, string File, string FullPath)
{
    public string Id => Diagnostic.Id;
    public string Message => Diagnostic.Message;
    public DiagnosticSeverity Severity => Diagnostic.Severity;

    /// <summary>The line, or empty for a diagnostic that has no location.</summary>
    public string Line => Diagnostic.Line > 0 ? Diagnostic.Line.ToString() : "";
}

/// <summary>
/// List of errors and warnings, with jump-to-source support.
///
/// Laid out as Visual Studio's Error List: toggles for errors, warnings and
/// messages that carry their counts and filter the list, a text filter, and
/// file paths relative to the solution so two files with the same name in
/// different folders can be told apart.
/// </summary>
public sealed class ProblemsPanel : UserControl
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DataGrid _grid;
    private readonly EmptyState _empty;
    private readonly EmptyState _noMatch;
    private readonly ContentControl _body = new();
    private readonly ToggleButton _errors;
    private readonly ToggleButton _warnings;
    private readonly ToggleButton _messages;
    private readonly TextBox _filter;
    private readonly Dictionary<string, string> _relative = new(StringComparer.Ordinal);
    private string? _cachedFolder;
    private List<ProblemRow> _rows = [];
    private bool _rebuildPending;

    public ProblemsPanel(MainWindowViewModel viewModel, Action<IdeDiagnostic> open)
    {
        _viewModel = viewModel;

        _grid = BuildGrid(open);

        // An empty grid reads as a panel that failed to load. Saying there
        // is nothing wrong is information; a blank box is not.
        _empty = new EmptyState(Localizer.Get(StringKeys.ProblemsEmpty), IconKind.Problems);
        _noMatch = new EmptyState(Localizer.Get(StringKeys.ProblemsNoMatch), IconKind.Find);

        _errors = PanelToolbar.Toggle(IconKind.Error, "", Localizer.Get(StringKeys.ProblemsErrors),
            DiagnosticAppearance.BrushKey(DiagnosticSeverity.Error));
        _warnings = PanelToolbar.Toggle(IconKind.Warning, "", Localizer.Get(StringKeys.ProblemsWarnings),
            DiagnosticAppearance.BrushKey(DiagnosticSeverity.Warning));
        _messages = PanelToolbar.Toggle(IconKind.Information, "", Localizer.Get(StringKeys.ProblemsMessages),
            DiagnosticAppearance.BrushKey(DiagnosticSeverity.Info));

        _filter = PanelToolbar.Filter(Localizer.Get(StringKeys.ProblemsFilter));
        _filter.MinWidth = 96;
        _filter.MaxWidth = 260;

        foreach (var toggle in new[] { _errors, _warnings, _messages })
            toggle.IsCheckedChanged += (_, _) => Rebuild();

        // The property, not TextChanged: that event is raised from the text
        // presenter and does not fire for text set from code.
        _filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Rebuild();
        };

        var toggles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Hairline * 2,
            Children = { _errors, _warnings, _messages }
        };

        var toolbar = PanelToolbar.Row(toggles, _filter);

        DataContext = viewModel;
        Content = new DockPanel { Children = { toolbar, _body } };

        viewModel.Diagnostics.CollectionChanged += (_, _) => DiagnosticsChanged();
        viewModel.PropertyChanged += (_, e) =>
        {
            // Paths are shown relative to the solution, so a different one
            // changes every row's text.
            if (e.PropertyName == nameof(MainWindowViewModel.SolutionPath)) Rebuild();
        };

        Rebuild();
    }

    private DataGrid BuildGrid(Action<IdeDiagnostic> open)
    {
        var grid = new DataGrid
        {
            IsReadOnly = true,
            CanUserSortColumns = false,
            CanUserResizeColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            FontSize = PanelMetrics.FontSize,
            RowHeight = 24,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = Localizer.Get(StringKeys.ColumnSeverity),
            Width = new DataGridLength(40),
            CanUserResize = false,
            CellTemplate = new FuncDataTemplate<ProblemRow>((row, _) =>
                DiagnosticAppearance.CreateCell(row?.Severity ?? DiagnosticSeverity.Info))
        });

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Localizer.Get(StringKeys.ColumnCode),
            Binding = new Avalonia.Data.Binding(nameof(ProblemRow.Id)),
            Width = new DataGridLength(96),
            MinWidth = 60
        });

        grid.Columns.Add(TextWithTooltip(Localizer.Get(StringKeys.ColumnDescription),
            nameof(ProblemRow.Message), nameof(ProblemRow.Message),
            new DataGridLength(2, DataGridLengthUnitType.Star), minWidth: 160));

        grid.Columns.Add(TextWithTooltip(Localizer.Get(StringKeys.ColumnFile),
            nameof(ProblemRow.File), nameof(ProblemRow.FullPath),
            new DataGridLength(1, DataGridLengthUnitType.Star), minWidth: 100));

        // Wide enough for the header and five digits; Line was the column
        // that scrolled out of view when the others took their widths first.
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Localizer.Get(StringKeys.ColumnLine),
            Binding = new Avalonia.Data.Binding(nameof(ProblemRow.Line)),
            Width = DataGridLength.Auto,
            MinWidth = 48
        });

        grid.LoadingRow += (_, args) =>
        {
            if (args.Row.DataContext is ProblemRow row)
                DiagnosticAppearance.Tint(args.Row, row.Severity);
        };

        grid.DoubleTapped += (_, _) => OpenSelected(open);

        // Tunnelling: the grid itself answers Enter by moving to the next
        // row, so by the time a bubbling handler ran the selection would
        // already be the wrong problem.
        grid.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            OpenSelected(open);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        return grid;
    }

    private static DataGridTemplateColumn TextWithTooltip(
        string header, string textProperty, string tipProperty, DataGridLength width, double minWidth) => new()
    {
        Header = header,
        Width = width,
        MinWidth = minWidth,
        CellTemplate = new FuncDataTemplate<ProblemRow>((_, _) =>
        {
            var text = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Spacing.Tight, 0)
            };

            text.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(textProperty));
            text.Bind(ToolTip.TipProperty, new Avalonia.Data.Binding(tipProperty));

            return text;
        })
    };

    private void OpenSelected(Action<IdeDiagnostic> open)
    {
        if (_grid.SelectedItem is ProblemRow row) open(row.Diagnostic);
    }

    /// <summary>What the panel is showing, for the tests.</summary>
    internal bool IsShowingEmptyState => ReferenceEquals(_body.Content, _empty);

    /// <summary>Whether the list is replaced by "nothing matches the filter".</summary>
    internal bool IsShowingNoMatch => ReferenceEquals(_body.Content, _noMatch);

    internal DataGrid Grid => _grid;

    internal EmptyState EmptyState => _empty;

    /// <summary>The rows the filters let through.</summary>
    internal IReadOnlyList<ProblemRow> VisibleRows
    {
        get
        {
            if (_rebuildPending) Rebuild();
            return _rows;
        }
    }

    // The counts are refreshed with the rows, once per burst; reading them
    // flushes a pending refresh so they are never stale to the caller.
    internal string ErrorsText => ToggleText(_errors);
    internal string WarningsText => ToggleText(_warnings);
    internal string MessagesText => ToggleText(_messages);

    /// <summary>How many rows were built, for the test that keeps refreshes linear.</summary>
    internal int RowsMapped { get; private set; }

    /// <summary>How many relative paths were computed (one per distinct file, not per row).</summary>
    internal int RelativePathsComputed { get; private set; }

    internal ToggleButton ErrorsToggle => _errors;
    internal ToggleButton WarningsToggle => _warnings;
    internal ToggleButton MessagesToggle => _messages;
    internal TextBox FilterBox => _filter;

    private string ToggleText(ToggleButton toggle)
    {
        if (_rebuildPending) Rebuild();

        return ((TextBlock)((StackPanel)toggle.Content!).Children[1]).Text ?? "";
    }

    /// <summary>
    /// Counts and the empty state follow every change at once; the grid's rows
    /// are rebuilt once after a burst, since a build adds hundreds of
    /// diagnostics one by one and rebuilding per diagnostic is quadratic.
    /// </summary>
    private void DiagnosticsChanged()
    {
        // No count here: each event used to walk every diagnostic, which made
        // a burst of N additions N passes over N items. Rebuild counts once.
        ShowWhicheverFits();

        if (_rebuildPending) return;

        _rebuildPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_rebuildPending) Rebuild();
        }, DispatcherPriority.Background);
    }

    private void UpdateCounts()
    {
        var errors = 0;
        var warnings = 0;
        var messages = 0;

        foreach (var diagnostic in _viewModel.Diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Error: errors++; break;
                case DiagnosticSeverity.Warning: warnings++; break;
                default: messages++; break;
            }
        }

        SetText(_errors, Count(errors, StringKeys.ProblemsErrorsCountOne, StringKeys.ProblemsErrorsCount));
        SetText(_warnings, Count(warnings, StringKeys.ProblemsWarningsCountOne, StringKeys.ProblemsWarningsCount));
        SetText(_messages, Count(messages, StringKeys.ProblemsMessagesCountOne, StringKeys.ProblemsMessagesCount));
    }

    private static string Count(int count, string one, string many) =>
        count == 1 ? Localizer.Get(one) : Localizer.Get(many, count);

    private static void SetText(ToggleButton toggle, string text) =>
        ((TextBlock)((StackPanel)toggle.Content!).Children[1]).Text = text;

    private void Rebuild()
    {
        _rebuildPending = false;

        UpdateCounts();

        var solutionFolder = SolutionFolder(_viewModel.SolutionPath);
        var needle = _filter.Text?.Trim() ?? "";
        var selected = (_grid.SelectedItem as ProblemRow)?.Diagnostic;

        if (!string.Equals(solutionFolder, _cachedFolder, StringComparison.Ordinal))
        {
            _relative.Clear();
            _cachedFolder = solutionFolder;
        }

        // Severity first: the rows the toggles hide are never mapped. The path
        // is made relative once per distinct file, not once per diagnostic.
        var rows = new List<ProblemRow>();

        foreach (var diagnostic in _viewModel.Diagnostics)
        {
            if (!IsShownBySeverity(diagnostic)) continue;

            var row = ToRow(diagnostic, solutionFolder);
            if (needle.Length == 0 || Matches(row, needle)) rows.Add(row);
        }

        _rows = rows;

        _grid.ItemsSource = _rows;

        if (selected is not null)
            _grid.SelectedItem = _rows.FirstOrDefault(row => ReferenceEquals(row.Diagnostic, selected));

        ShowWhicheverFits();
    }

    private bool IsShownBySeverity(IdeDiagnostic diagnostic) => diagnostic.Severity switch
    {
        DiagnosticSeverity.Error => _errors.IsChecked == true,
        DiagnosticSeverity.Warning => _warnings.IsChecked == true,
        _ => _messages.IsChecked == true
    };

    private static bool Matches(ProblemRow row, string needle) =>
        row.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || row.Message.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || row.File.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? SolutionFolder(string? solutionPath)
    {
        if (string.IsNullOrEmpty(solutionPath)) return null;

        return Directory.Exists(solutionPath)
            ? solutionPath
            : Path.GetDirectoryName(Path.GetFullPath(solutionPath));
    }

    private ProblemRow ToRow(IdeDiagnostic diagnostic, string? solutionFolder)
    {
        RowsMapped++;

        var path = diagnostic.FilePath ?? "";
        if (!_relative.TryGetValue(path, out var relative))
        {
            RelativePathsComputed++;
            relative = RelativePath(diagnostic.FilePath, solutionFolder);
            _relative[path] = relative;
        }

        return new(diagnostic, relative, path);
    }

    /// <summary>
    /// The path relative to the solution folder when the file is inside it;
    /// the path as reported otherwise, since "..\..\x" says less than the
    /// whole path does.
    /// </summary>
    internal static string RelativePath(string? path, string? solutionFolder)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (solutionFolder is null || !Path.IsPathRooted(path)) return path;

        var relative = Path.GetRelativePath(solutionFolder, path);

        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative;
    }

    /// <summary>The list when there is something to show, a message when not.</summary>
    private void ShowWhicheverFits() =>
        _body.Content = _viewModel.Diagnostics.Count == 0
            ? _empty
            : _rows.Count == 0 && !_rebuildPending ? _noMatch : _grid;
}
