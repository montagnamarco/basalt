using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Model;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The Problems panel as a Visual Studio Error List: paths relative to the
/// solution, severity toggles that carry counts and filter, a text filter,
/// and a severity mark that stays readable on every theme.
/// </summary>
public sealed class ProblemsPanelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "basalt-problems-ui");

    private static string In(params string[] parts) => Path.Combine([Root, .. parts]);

    private static MainWindowViewModel ViewModelWithSolution()
    {
        var viewModel = new MainWindowViewModel { SolutionPath = In("App.sln") };

        viewModel.Diagnostics.Add(new("BC30451", "'Foo' is not declared.", DiagnosticSeverity.Error, In("Views", "MainForm.vb"), 12, 5));
        viewModel.Diagnostics.Add(new("BC30002", "Type 'Bar' is not defined.", DiagnosticSeverity.Error, In("Program.vb"), 3, 1));
        viewModel.Diagnostics.Add(new("BC42024", "Unused local variable: 'x'.", DiagnosticSeverity.Warning, In("Views", "MainForm.vb"), 40, 9));
        viewModel.Diagnostics.Add(new("IDE0001", "Name can be simplified.", DiagnosticSeverity.Info, In("Views", "Other.vb"), 7, 1));

        return viewModel;
    }

    [AvaloniaFact]
    public void TheFileColumnShowsThePathRelativeToTheSolutionAndTheFullPathInTheTooltip()
    {
        using var viewModel = ViewModelWithSolution();
        var panel = new ProblemsPanel(viewModel, _ => { });

        var row = panel.VisibleRows.First(r => r.Id == "BC30451");

        Assert.Equal(Path.Combine("Views", "MainForm.vb"), row.File);
        Assert.Equal(In("Views", "MainForm.vb"), row.FullPath);

        // The tooltip really is on the cell, not only in the row's data.
        var window = new Window { Width = 800, Height = 240, Content = panel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var tips = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text == Path.Combine("Views", "MainForm.vb"))
                .Select(t => ToolTip.GetTip(t))
                .ToList();

            Assert.NotEmpty(tips);
            Assert.All(tips, tip => Assert.Equal(In("Views", "MainForm.vb"), tip));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void AFileOutsideTheSolutionKeepsItsWholePath()
    {
        Assert.Equal("/elsewhere/x.vb", ProblemsPanel.RelativePath("/elsewhere/x.vb", "/work/app"));
        Assert.Equal("", ProblemsPanel.RelativePath(null, "/work/app"));
        Assert.Equal("Form.vb", ProblemsPanel.RelativePath("Form.vb", "/work/app"));
    }

    [AvaloniaFact]
    public void TheSeverityTogglesCarryCountsAndFilterTheList()
    {
        using var viewModel = ViewModelWithSolution();
        var panel = new ProblemsPanel(viewModel, _ => { });

        Assert.Equal("2 Errors", panel.ErrorsText);
        Assert.Equal("1 Warning", panel.WarningsText);
        Assert.Equal("1 Message", panel.MessagesText);
        Assert.Equal(4, panel.VisibleRows.Count);

        panel.ErrorsToggle.IsChecked = false;

        Assert.Equal(2, panel.VisibleRows.Count);
        Assert.DoesNotContain(panel.VisibleRows, r => r.Severity == DiagnosticSeverity.Error);

        // The count is how many there are, not how many are shown: it is
        // what tells the user there is something behind a switched-off toggle.
        Assert.Equal("2 Errors", panel.ErrorsText);

        panel.WarningsToggle.IsChecked = false;
        panel.MessagesToggle.IsChecked = false;
        Assert.Empty(panel.VisibleRows);
        Assert.True(panel.IsShowingNoMatch);

        panel.ErrorsToggle.IsChecked = true;
        Assert.Equal(2, panel.VisibleRows.Count);
        Assert.False(panel.IsShowingNoMatch);
    }

    [AvaloniaFact]
    public void TheCountsFollowTheDiagnosticsAsTheyChange()
    {
        using var viewModel = ViewModelWithSolution();
        var panel = new ProblemsPanel(viewModel, _ => { });

        viewModel.Diagnostics.Add(new("BC1", "Another.", DiagnosticSeverity.Error, In("A.vb"), 1, 1));

        Assert.Equal("3 Errors", panel.ErrorsText);
        Assert.Equal(5, panel.VisibleRows.Count);

        viewModel.Diagnostics.Clear();

        Assert.Equal("0 Errors", panel.ErrorsText);
        Assert.True(panel.IsShowingEmptyState);
    }

    [AvaloniaFact]
    public void TheTextFilterMatchesTheCodeTheMessageAndThePath()
    {
        using var viewModel = ViewModelWithSolution();
        var panel = new ProblemsPanel(viewModel, _ => { });

        panel.FilterBox.Text = "bc30002";
        Assert.Equal("BC30002", Assert.Single(panel.VisibleRows).Id);

        panel.FilterBox.Text = "unused";
        Assert.Equal("BC42024", Assert.Single(panel.VisibleRows).Id);

        panel.FilterBox.Text = "other.vb";
        Assert.Equal("IDE0001", Assert.Single(panel.VisibleRows).Id);

        panel.FilterBox.Text = "no such thing";
        Assert.True(panel.IsShowingNoMatch);

        panel.FilterBox.Text = "";
        Assert.Equal(4, panel.VisibleRows.Count);
    }

    [AvaloniaFact]
    public void EnterOpensTheSelectedProblem()
    {
        using var viewModel = ViewModelWithSolution();
        var opened = new List<string>();
        var panel = new ProblemsPanel(viewModel, d => opened.Add(d.Id));
        var window = new Window { Width = 800, Height = 240, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            panel.Grid.SelectedItem = panel.VisibleRows.First(r => r.Id == "BC42024");

            panel.Grid.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(["BC42024"], opened);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheLineColumnIsNotClippedInANarrowPanel()
    {
        using var viewModel = ViewModelWithSolution();
        viewModel.Diagnostics.Add(new("CS8618", "A long description that wants every pixel it can get.", DiagnosticSeverity.Error, In("Program.vb"), 12345, 1));
        var panel = new ProblemsPanel(viewModel, _ => { });
        var window = new Window { Width = 520, Height = 240, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var line = panel.Grid.Columns.Last();
            var right = panel.Grid.Columns.Sum(c => c.ActualWidth);

            Assert.True(line.ActualWidth >= 44, $"The Line column is {line.ActualWidth}px wide.");
            Assert.True(right <= panel.Grid.Bounds.Width + 1,
                $"The columns need {right}px in a grid {panel.Grid.Bounds.Width}px wide, so the last one is cut off.");

            // The description takes what the fixed columns leave.
            var description = panel.Grid.Columns[2];
            Assert.True(description.ActualWidth > panel.Grid.Columns[3].ActualWidth);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void AnErrorRowCarriesAnAccentBarInTheThemesErrorColour(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = new ThemeScope(variant);
        using var viewModel = new MainWindowViewModel();
        viewModel.Diagnostics.Add(new("BC1", "Error", DiagnosticSeverity.Error, In("A.vb"), 1, 1));
        var panel = new ProblemsPanel(viewModel, _ => { });
        var window = new Window { Width = 600, Height = 160, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            window.TryFindResource("ErrorForeground", window.ActualThemeVariant, out var token);
            var error = (Color)token!;
            var raster = Raster.Of(window);

            // The bar is the row's first three pixels; find the row on the
            // first column rather than hard-coding its y.
            var bar = window.GetVisualDescendants().OfType<Border>().First(b => b.Width == 3);
            var area = Raster.BoundsIn(bar, window);

            Assert.True(area.Height > 10, "The accent bar has no height.");
            Assert.True(raster.Contains(area, error, 6), $"{name}: the accent bar is not {error}.");
            Assert.True(Raster.Contrast(error, raster.At(300, 140)) >= 3,
                $"{name}: the error colour is too faint on the panel surface.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TabVisitsTheTogglesThenTheFilterThenTheGrid()
    {
        // The filter sits to the right of the toggles, so Tab must reach it
        // after them. A DockPanel needs its right-hand child added first, which
        // made the keyboard order the reverse of the visual one.
        using var viewModel = ViewModelWithSolution();
        var panel = new ProblemsPanel(viewModel, _ => { });
        var window = new Window { Width = 800, Height = 300, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var order = new List<InputElement>
            {
                panel.ErrorsToggle, panel.WarningsToggle, panel.MessagesToggle, panel.FilterBox
            };

            var focus = window.FocusManager!;
            for (var i = 0; i < order.Count - 1; i++)
            {
                order[i].Focus();
                Assert.Same(order[i], focus.GetFocusedElement());
                Assert.True(focus.TryMoveFocus(NavigationDirection.Next));
                Assert.Same(order[i + 1], focus.GetFocusedElement());
            }

            // And the filter is drawn to the right of the toggles, so the order
            // is also the visual one.
            var toggleRight = panel.MessagesToggle.TranslatePoint(new Point(panel.MessagesToggle.Bounds.Width, 0), window)!.Value.X;
            var filterLeft = panel.FilterBox.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.True(filterLeft >= toggleRight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ARefreshOfTwoThousandDiagnosticsMapsEachVisibleRowOnceAndEachFileOnce()
    {
        using var viewModel = new MainWindowViewModel { SolutionPath = In("App.sln") };
        var panel = new ProblemsPanel(viewModel, _ => { });
        panel.WarningsToggle.IsChecked = false;
        var mappedBefore = panel.RowsMapped;
        var changes = 0;
        viewModel.Diagnostics.CollectionChanged += (_, _) => changes++;

        var batch = Enumerable.Range(0, 2000).Select(i => new IdeDiagnostic(
            "BC" + i, "message " + i, i % 4 == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            In("Views", "File" + (i % 10) + ".vb"), i + 1, 1)).ToList();

        viewModel.ReplaceDiagnosticsOf(In("Views", "File0.vb"), batch);
        Assert.Equal(1, changes);

        var rows = panel.VisibleRows;

        // 500 errors are shown; the 1500 warnings the toggle hides are not
        // mapped at all, and ten files are made relative once each.
        Assert.Equal(500, rows.Count);
        Assert.True(panel.RowsMapped - mappedBefore <= 500, $"mapped {panel.RowsMapped - mappedBefore} rows");
        Assert.True(panel.RelativePathsComputed <= 10, $"{panel.RelativePathsComputed} relative paths");
        Assert.Equal("500 Errors", panel.ErrorsText);
    }
}
