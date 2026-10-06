using Avalonia;
using Avalonia.VisualTree;
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

    [AvaloniaFact]
    public void EveryEmptyPanelUsesTheSharedControl()
    {
        // The panels that used to carry a copy of a faded TextBlock each.
        var viewModel = new MainWindowViewModel();

        Assert.IsType<EmptyState>(new ProblemsPanel(viewModel, _ => { }).EmptyState);
        Assert.IsType<EmptyState>(new OutputPanel(viewModel).EmptyState);
        Assert.IsType<EmptyState>(new OutlinePanel().Content);
        Assert.IsType<EmptyState>(new CallStackPanel().Content);
        Assert.IsType<EmptyState>(new VariablesPanel().Content);
        Assert.IsType<EmptyState>(new BreakpointsPanel().Content);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheEmptyStateIsDrawnCentredInTheThemesSecondaryColour(string name)
    {
        var variant = name == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        // The application's variant, not the window's: the Dock* brushes are
        // application resources and resolve their colours against it.
        using var theme = new ThemeScope(variant);
        var state = new EmptyState("Nothing here yet.", IconKind.Information);
        var window = new Window { Content = state, Width = 400, Height = 300 };

        // The surface a panel's content sits on in the IDE.
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("DockSurfacePanelBrush"));
        window.Show();
        window.UpdateLayout();

        try
        {
            window.TryFindResource("DescriptionForeground", window.ActualThemeVariant, out var desc);
            var description = (Avalonia.Media.Color)desc!;
            var raster = Raster.Of(window);

            // What the window actually painted behind the message, read from
            // the raster rather than assumed from a token.
            var background = raster.At(2, 2);
            window.TryFindResource("SideBarBackground", window.ActualThemeVariant, out var surface);
            Assert.True(Raster.Near(background, (Avalonia.Media.Color)surface!),
                $"{name}: the surface behind the message is {background}, not the panel surface.");

            var text = state.GetVisualDescendants().OfType<TextBlock>().Single();
            var area = Raster.BoundsIn(text, window);

            // Drawn at all, and readable on what is behind it.
            Assert.True(raster.BestContrastAgainst(area, background) >= 4.5,
                $"{name}: the message is not readable on the window's background.");

            // Drawn in the theme's secondary colour, not a faded copy of the
            // primary one: a fully covered pixel of a glyph is that colour.
            Assert.True(raster.Contains(area, description, 8),
                $"{name}: no pixel of the message is the theme's secondary colour {description}.");
            Assert.True(Raster.Contrast(description, background) >= 4.5);

            // Centred: the message's middle is the window's middle.
            var middle = area.X + area.Width / 2.0;
            Assert.InRange(middle, 200 - 4, 200 + 4);
        }
        finally { window.Close(); }
    }
}
