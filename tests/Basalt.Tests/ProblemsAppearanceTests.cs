using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using Basalt.Core.Model;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Basalt.Tests;

public sealed class ProblemsAppearanceTests
{
    [AvaloniaFact]
    public void ProblemsHasCompactRowsAndSeverityIcons()
    {
        using var vm = new MainWindowViewModel();
        vm.Diagnostics.Add(new("BC1", "Error", DiagnosticSeverity.Error, "Form.vb", 1, 1));
        var panel = new ProblemsPanel(vm, _ => { });
        var grid = panel.Grid;
        Assert.Equal(PanelMetrics.FontSize, grid.FontSize);
        Assert.Equal(24, grid.RowHeight);
        Assert.IsType<DataGridTemplateColumn>(grid.Columns[0]);
    }

    /// <summary>
    /// The error and warning rows are tinted with the theme's own token, and
    /// only faintly: the accent bar and icon carry the severity, so the text
    /// must stay as readable on a tinted row as on a plain one. Rendered in a
    /// light and a dark theme and read from pixels, not from the brush.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorAndWarningRowsAreFaintlyTintedAndStayReadableInEveryTheme(bool dark)
    {
        using var theme = new ThemeScope(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var vm = new MainWindowViewModel();
        vm.Diagnostics.Add(new("BC1", "Error", DiagnosticSeverity.Error, "Form.vb", 1, 1));
        vm.Diagnostics.Add(new("BC2", "Warning", DiagnosticSeverity.Warning, "Form.vb", 2, 1));
        vm.Diagnostics.Add(new("BC3", "Info", DiagnosticSeverity.Info, "Form.vb", 3, 1));
        var window = new Window { Width = 900, Height = 200, Content = new ProblemsPanel(vm, _ => { }) };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var raster = Raster.Of(window);

            // A blank pixel at the right edge of each row, past its last cell's text.
            Color RowColour(string message)
            {
                var text = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == message);
                var row = text.FindAncestorOfType<DataGridRow>()!;
                var bounds = Raster.BoundsIn(row, window);

                return raster.At(bounds.Right - 4, bounds.Y + bounds.Height / 2);
            }

            var plain = RowColour("Info");
            var error = RowColour("Error");
            var warning = RowColour("Warning");

            Assert.True(error.R - plain.R > 4 || plain.G - error.G > 4,
                $"The error row {error} shows no red tint against a plain row {plain}.");
            Assert.True(warning.R > plain.R + 4 || warning.B < plain.B - 4,
                $"The warning row {warning} shows no amber tint against a plain row {plain}.");

            // Faint: no channel moved by more than a fifth of its range.
            Assert.All(new[] { error, warning }, c =>
            {
                Assert.InRange(Math.Abs(c.R - plain.R), 0, 52);
                Assert.InRange(Math.Abs(c.G - plain.G), 0, 52);
                Assert.InRange(Math.Abs(c.B - plain.B), 0, 52);
            });

            // The message text is still readable on its own row.
            foreach (var message in new[] { "Error", "Warning" })
            {
                var text = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == message);
                var row = RowColour(message);

                Assert.True(raster.BestContrastAgainst(Raster.BoundsIn(text, window), row) >= 4.5,
                    $"The {message} text is not readable on its tinted row.");
            }
        }
        finally { window.Close(); }
    }
}
