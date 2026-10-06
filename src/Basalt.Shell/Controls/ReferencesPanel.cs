using Basalt.Core.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Basalt.Extensibility;

namespace Basalt.Shell.Controls;

/// <summary>A reference as the list shows it.</summary>
public sealed record ReferenceRow(SourceLocation Location, string Preview)
{
    public string Display =>
        $"{Path.GetFileName(Location.FilePath)}({Location.Range.Start.Line})  {Preview}";
}

/// <summary>
/// Everywhere a symbol is used, listed so each can be opened.
/// </summary>
public sealed class ReferencesPanel : UserControl
{
    private readonly ListBox _list;
    private readonly TextBlock _summary;
    private readonly List<ReferenceRow> _rows = [];
    private readonly EmptyState _empty;

    private void UpdateEmpty()
    {
        _empty.IsVisible = _rows.Count == 0;
        _list.IsVisible = _rows.Count > 0;
    }

    public ReferencesPanel()
    {
        _summary = new TextBlock
        {
            Text = Localizer.Get(StringKeys.ReferencesNone),
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Small)
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<ReferenceRow>((row, _) => new TextBlock
            {
                Text = row?.Display,
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = PanelMetrics.FontSize
            })
        };

        _list.DoubleTapped += (_, _) =>
        {
            if (_list.SelectedItem is ReferenceRow row)
                ReferenceActivated?.Invoke(this, row.Location);
        };

        var layout = new DockPanel();
        DockPanel.SetDock(_summary, Avalonia.Controls.Dock.Top);
        layout.Children.Add(_summary);

        // Nothing to list: the sentence above says what was searched for, and
        // the panel says it has no results instead of showing a blank list.
        _empty = new EmptyState(Localizer.Get(StringKeys.ReferencesEmpty), IconKind.References);
        layout.Children.Add(_empty);
        layout.Children.Add(_list);
        UpdateEmpty();

        Content = layout;
    }

    public event EventHandler<SourceLocation>? ReferenceActivated;

    internal IReadOnlyList<ReferenceRow> Rows => _rows.ToList();

    internal string Summary => _summary.Text ?? "";

    /// <summary>
    /// Lists references, reading a line of context for each.
    ///
    /// Files are read once and shared across their references, since a symbol
    /// is usually used several times in the same file.
    /// </summary>
    public async Task ShowAsync(
        string symbolName, IReadOnlyList<SourceLocation> locations, CancellationToken ct = default)
    {
        _rows.Clear();

        var lines = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var location in locations)
        {
            ct.ThrowIfCancellationRequested();

            if (!lines.TryGetValue(location.FilePath, out var fileLines))
            {
                try
                {
                    // ConfigureAwait(true): what follows updates controls, and
                    // resuming on a pool thread would throw.
                    fileLines = await File.ReadAllLinesAsync(location.FilePath, ct)
                        .ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    fileLines = [];
                }

                lines[location.FilePath] = fileLines;
            }

            var index = location.Range.Start.Line - 1;
            var preview = index >= 0 && index < fileLines.Length
                ? fileLines[index].Trim()
                : "";

            _rows.Add(new ReferenceRow(location, preview));
        }

        _list.ItemsSource = _rows.ToList();

        _summary.Text = _rows.Count == 0
            ? string.Format(Localizer.Get(StringKeys.ReferencesNoneTo), symbolName)
            : string.Format(Localizer.Get(StringKeys.ReferencesCount), _rows.Count, symbolName);

        UpdateEmpty();
    }

    /// <summary>The list, so a test can double-click it for real.</summary>
    internal ListBox ListForTests => _list;

    internal void ActivateForTests(ReferenceRow row) =>
        ReferenceActivated?.Invoke(this, row.Location);
}
