using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// The Localizable and Language rows of the root form, driven through the real property panel: the
/// switch and the drop-down are changed the way a user changes them, and the designer's Designer.vb,
/// Form1.resx and Form1.it.resx are read back.
/// </summary>
/// <remarks>
/// The panel and the view are real. The view builds the two rows from its session, but the view does not
/// pass them to the panel when it selects the form yet, so this test shows the form in its own panel with
/// <c>view.FormDesignRows()</c>. Button1's Text is set through the session, as the view test does.
/// Defect reintroduced once to prove the test catches it: the panel's edit handler called the control's own
/// edit path before trying the Design rows, so a Localizable edit never reached the session. The journey failed
/// at the wait for ApplyResources in Designer.vb ("The designer never reached the expected state."), and the
/// handler was restored afterwards.
/// </remarks>
public sealed class WinFormsDesignerLocalizationPropertyJourneyTests
{
    [AvaloniaFact]
    public async Task LocalizableAndLanguageRowsMoveValuesBetweenTheCodeAndTheResourceFiles()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb",
            WinFormsDesignerLocalizationDocumentTests.NotLocalized, true);
        var neutral = new EditorDocumentViewModel("MainForm.resx", WinFormsResourceDocumentTemplate.Empty);
        var italian = new EditorDocumentViewModel("MainForm.it.resx", WinFormsDesignerLocalizationSessionTests.ItalianResources);
        using var view = new WinFormsDesignerView(designer, findCode: path => path switch
        {
            "MainForm.it.resx" => italian,
            "MainForm.resx" => neutral,
            _ => null,
        });
        var panel = new WinFormsPropertyPanel();
        var window = new Window
        {
            Width = 1100,
            Height = 700,
            Content = new DockPanel
            {
                Children = { panel, view },
            },
        };
        DockPanel.SetDock(panel, Avalonia.Controls.Dock.Right);
        window.Show();

        try
        {
            await WaitUntilAsync(() => view.DesignerSession is not null);
            var session = view.DesignerSession!;
            var form = session.Form;
            var grid = Assert.Single(panel.GetVisualDescendants().OfType<PropertyGrid>());

            // Not localizable yet: the switch is editable and Language is shown read-only.
            ShowForm(panel, view);
            Assert.Equal("False", ComboOf(RowOf(grid, "Localizable")).SelectedItem);
            Assert.Empty(RowOf(grid, "Language").GetVisualDescendants().OfType<ComboBox>());
            Assert.Contains(RowOf(grid, "Language").GetVisualDescendants().OfType<TextBlock>(),
                label => label.Text == "(Default)");

            await SelectValueAsync(view, RowOf(grid, "Localizable"), "True");
            await WaitUntilAsync(() => designer.Text.Contains("ApplyResources", StringComparison.Ordinal));
            Assert.True(session.IsLocalizable);
            Assert.Contains("ComponentResourceManager", designer.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Me.Button1.Text = \"Button\"", designer.Text, StringComparison.Ordinal);
            var neutralResources = new WinFormsResourceDocument(neutral.Text);
            Assert.Equal("Main form", neutralResources.TryGet("$this.Text")!.Value);
            Assert.Equal("Button", neutralResources.TryGet("Button1.Text")!.Value);

            // Localizable and Language now editable; choosing Italian shows the culture file.
            ShowForm(panel, view);
            var languageBox = ComboOf(RowOf(grid, "Language"));
            Assert.Equal("(Default)", languageBox.SelectedItem);
            Assert.Contains("it", languageBox.ItemsSource!.Cast<string>());
            await SelectValueAsync(view, RowOf(grid, "Language"), "it");
            await WaitUntilAsync(() => session.Culture == "it");
            Assert.Equal("Pulsante", session.FindControl("Button1")!.Text);

            // A culture freezes the Localizable switch: the row offers no editor while Italian is shown.
            ShowForm(panel, view);
            Assert.Empty(RowOf(grid, "Localizable").GetVisualDescendants().OfType<ComboBox>());

            // Editing Button1.Text in Italian writes the culture file and leaves the neutral one alone.
            var neutralBefore = neutral.Text;
            var designerBefore = designer.Text;
            var button = session.FindControl("Button1")!;
            await view.DesignerSession!.SetPropertyAsync(button, "Text", "Pulsante OK");
            await WaitUntilAsync(() => italian.Text.Contains("Pulsante OK", StringComparison.Ordinal));
            Assert.Equal("Pulsante OK", new WinFormsResourceDocument(italian.Text).TryGet("Button1.Text")!.Value);
            Assert.Equal(neutralBefore, neutral.Text);
            Assert.Equal(designerBefore, designer.Text);

            // Back to (Default): the surface shows the neutral text again.
            ShowForm(panel, view);
            await SelectValueAsync(view, RowOf(grid, "Language"), "(Default)");
            await WaitUntilAsync(() => session.Culture is null);
            Assert.Equal("Button", session.FindControl("Button1")!.Text);
            Assert.Equal("Main form", form.Text);

            // A child control has neither row.
            // Back to (Default) rebuilt the surface, so the child is looked up again.
            ShowControl(panel, session, session.FindControl("Button1")!);
            Assert.Null(FindRowOrNull(grid, "Localizable"));
            Assert.Null(FindRowOrNull(grid, "Language"));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Selecting the form on the design surface shows Localizable and Language in the designer's
    /// own Properties grid; selecting a control on it does not.
    /// </summary>
    /// <remarks>
    /// The rows were built but the view's selection path (WinFormsDesignerView.ShowProperties)
    /// never passed them to the panel, so a user never saw them. Removing the formRows argument
    /// there makes this test fail.
    /// </remarks>
    [AvaloniaFact]
    public async Task SelectingTheFormOnTheSurfaceShowsTheDesignRowsAndAControlDoesNot()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb",
            WinFormsDesignerLocalizationDocumentTests.NotLocalized, true);
        using var view = new WinFormsDesignerView(designer);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());

            surface.Select(surface.RootForm!);
            await WaitUntilAsync(() => FindRowOrNull(grid, "Localizable") is not null);
            Assert.NotNull(FindRowOrNull(grid, "Language"));

            surface.Select(surface.RootForm!.Controls[0]);
            await WaitUntilAsync(() => FindRowOrNull(grid, "Localizable") is null);
            Assert.Null(FindRowOrNull(grid, "Language"));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void LanguageChoicesAreSortedByDisplayNameAndKeepTheCurrentCulture()
    {
        // en-US is a specific culture, so it is never in the neutral list; it must be added as the current one.
        var choices = WinFormsDesignerView.LanguageChoices("en-US");

        Assert.Contains("en-US", choices);
        Assert.Contains("it", choices);
        var displayNames = choices.Select(name => CultureInfo.GetCultureInfo(name).DisplayName).ToArray();
        Assert.Equal(displayNames.Order(StringComparer.CurrentCulture).ToArray(), displayNames);
    }

    private static void ShowForm(WinFormsPropertyPanel panel, WinFormsDesignerView view) =>
        panel.Show(view.DesignerSession!.Form, formRows: view.FormDesignRows());

    private static void ShowControl(WinFormsPropertyPanel panel, WinFormsDesignerSession session, Forms.Control control) =>
        panel.Show(control, name => session.ExplicitValue(control, name), componentName: control.Name);

    private static async Task SelectValueAsync(WinFormsDesignerView view, Border row, string value)
    {
        // The view ignores an edit while its previous one is still running, so wait for it to finish.
        await WaitUntilAsync(() => view.IsEnabled);
        ComboOf(row).SelectedItem = value;
    }

    private static ComboBox ComboOf(Border row) =>
        Assert.Single(row.GetVisualDescendants().OfType<ComboBox>(), combo => combo.Name == "PropertyGridValue");

    private static Border RowOf(PropertyGrid grid, string name)
    {
        var row = FindRowOrNull(grid, name);
        Assert.NotNull(row);
        return row;
    }

    private static Border? FindRowOrNull(PropertyGrid grid, string name) =>
        grid.Rows.OfType<Border>().SingleOrDefault(row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The designer never reached the expected state.");
    }
}
