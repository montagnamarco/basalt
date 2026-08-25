using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// What a panel shows before it has anything to show.
///
/// An empty panel and a broken panel look identical, and the user cannot
/// tell whether the build said nothing or never ran. Every panel that can be
/// empty has to say so.
/// </summary>
public class PanelEmptyStateTests
{
    /// <summary>
    /// The text a control shows, wherever in its tree it sits.
    ///
    /// The logical tree, not the visual one: a control that has never been
    /// attached to a window has no visual children, and every one of these
    /// panels is built without ever being shown.
    /// </summary>
    private static string TextIn(Control control)
    {
        var found = new List<string>();

        void Walk(ILogical node)
        {
            if (node is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
                found.Add(text.Text!);

            if (node is ContentControl { Content: ILogical content })
                Walk(content);

            foreach (var child in node.LogicalChildren)
                Walk(child);
        }

        Walk(control);

        return string.Join(" ", found);
    }

    [AvaloniaFact]
    public void TheProblemsPanelSaysThereAreNoProblems()
    {
        var viewModel = new MainWindowViewModel();
        var panel = new ProblemsPanel(viewModel, _ => { });

        Assert.True(panel.IsShowingEmptyState);

        Assert.Contains("No errors", TextIn((Control)panel.Content!),
            StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TheProblemsPanelShowsTheGridOnceThereIsAProblem()
    {
        var viewModel = new MainWindowViewModel();
        var panel = new ProblemsPanel(viewModel, _ => { });

        viewModel.Diagnostics.Add(new Basalt.Core.Model.IdeDiagnostic(
            "BC30451", "'x' is not declared.",
            Basalt.Core.Model.DiagnosticSeverity.Error, "/a.vb", 1, 1));

        Assert.False(panel.IsShowingEmptyState);
    }

    [AvaloniaFact]
    public void TheOutputPanelSaysNothingHasBeenBuilt()
    {
        var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);

        Assert.True(panel.IsShowingEmptyState);

        Assert.Contains("built", TextIn((Control)panel.Content!),
            StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TheOutputPanelShowsTheOutputOnceThereIsSome()
    {
        var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);

        viewModel.BuildOutput.Add("Build succeeded.");

        Assert.False(panel.IsShowingEmptyState);
    }

    [AvaloniaFact]
    public void TheOutputPanelGoesBackToTheMessageWhenCleared()
    {
        // A rebuild clears the output first; the panel must not stay blank.
        var viewModel = new MainWindowViewModel();
        var panel = new OutputPanel(viewModel);

        viewModel.BuildOutput.Add("Build succeeded.");
        viewModel.BuildOutput.Clear();

        Assert.True(panel.IsShowingEmptyState);
    }

    [AvaloniaFact]
    public void ThePropertyPanelSaysNothingIsSelected()
    {
        var panel = new PropertyPanel();

        panel.Show(null);

        Assert.Contains("No element", TextIn(panel), StringComparison.OrdinalIgnoreCase);
    }
}
