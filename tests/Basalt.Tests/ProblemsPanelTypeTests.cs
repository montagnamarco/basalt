using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Model;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The Problems grid is set in the same type as every other panel.
///
/// Its text was drawn about 14 px where the other panels draw
/// <see cref="PanelMetrics.FontSize"/>, so the Problems panel looked like a
/// different application beside Output and the Solution Explorer. The grid
/// reads the shared size and row height; these tests measure what the cells
/// actually draw, not what the grid says it draws.
/// </summary>
public sealed class ProblemsPanelTypeTests
{
    [AvaloniaFact]
    public void EveryTextInTheGridIsTheSharedPanelSize()
    {
        using var vm = new MainWindowViewModel();
        vm.Diagnostics.Add(new("BC30451", "'notDeclared' is not declared.", DiagnosticSeverity.Error, "Program.vb", 5, 1));
        var panel = new ProblemsPanel(vm, _ => { });
        var window = new Window { Width = 900, Height = 200, Content = panel };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var texts = panel.Grid.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => !string.IsNullOrEmpty(t.Text))
                .ToList();

            Assert.Contains(texts, t => t.Text == "BC30451");
            Assert.Contains(texts, t => t.Text == "Program.vb");

            var wrong = texts
                .Where(t => Math.Abs(t.FontSize - PanelMetrics.FontSize) > 0.01)
                .Select(t => $"'{t.Text}' is {t.FontSize}px")
                .ToList();

            Assert.True(wrong.Count == 0,
                $"The grid draws its text larger than the other panels ({PanelMetrics.FontSize}px): {string.Join(", ", wrong)}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheGridRowsAreTheSharedPanelRowHeight()
    {
        using var vm = new MainWindowViewModel();
        var panel = new ProblemsPanel(vm, _ => { });

        Assert.Equal(PanelMetrics.RowHeight, panel.Grid.RowHeight);
    }
}
