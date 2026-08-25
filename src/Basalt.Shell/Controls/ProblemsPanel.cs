using Basalt.Core.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Basalt.Core.Model;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>List of errors and warnings, with jump-to-source support.</summary>
public sealed class ProblemsPanel : UserControl
{
    public ProblemsPanel(MainWindowViewModel viewModel, Action<IdeDiagnostic> apri)
    {
        var grid = new DataGrid
        {
            [!DataGrid.ItemsSourceProperty] = new Binding(nameof(MainWindowViewModel.Diagnostics)),
            IsReadOnly = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };

        grid.Columns.Add(new DataGridTextColumn
        { Header = Localizer.Get(StringKeys.ColumnSeverity), Binding = new Binding(nameof(IdeDiagnostic.Severity)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn
        { Header = Localizer.Get(StringKeys.ColumnCode), Binding = new Binding(nameof(IdeDiagnostic.Id)), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn
        { Header = Localizer.Get(StringKeys.ColumnDescription), Binding = new Binding(nameof(IdeDiagnostic.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn
        { Header = Localizer.Get(StringKeys.ColumnFile), Binding = new Binding(nameof(IdeDiagnostic.FilePath)), Width = new DataGridLength(240) });
        grid.Columns.Add(new DataGridTextColumn
        { Header = Localizer.Get(StringKeys.ColumnLine), Binding = new Binding(nameof(IdeDiagnostic.Line)), Width = new DataGridLength(60) });

        grid.DoubleTapped += (_, _) =>
        {
            if (grid.SelectedItem is IdeDiagnostic diagnostic) apri(diagnostic);
        };

        // An empty grid reads as a panel that failed to load. Saying there
        // is nothing wrong is information; a blank box is not.
        _empty = new TextBlock
        {
            Text = Localizer.Get(StringKeys.ProblemsEmpty),
            Opacity = 0.6,
            FontSize = 12,
            Margin = new Thickness(12, 8, 8, 8)
        };

        _grid = grid;

        DataContext = viewModel;

        viewModel.Diagnostics.CollectionChanged += (_, _) => ShowWhicheverFits();

        ShowWhicheverFits();
    }

    private readonly DataGrid _grid;
    private readonly TextBlock _empty;

    /// <summary>The grid when there is something to show, the message when not.</summary>
    private void ShowWhicheverFits() =>
        Content = ((MainWindowViewModel)DataContext!).Diagnostics.Count == 0
            ? _empty
            : _grid;

    /// <summary>What the panel is showing, for the tests.</summary>
    internal bool IsShowingEmptyState => ReferenceEquals(Content, _empty);
}
