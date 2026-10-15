using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// The Designer, Format and Edit menu commands reaching the WinForms designer in front: the router decides where a command
/// goes, and the Format entries run the designer's own commands. Each test works on a real designer, opened from a source.
/// </summary>
public sealed class WinFormsDesignerMenuCommandTests : IDisposable
{
    public WinFormsDesignerMenuCommandTests() => WinFormsDesignerView.ResetClipboardForTests();

    public void Dispose() => WinFormsDesignerView.ResetClipboardForTests();

    // Two buttons directly on the form, so that a command with two selected controls has a pair to act on.
    private const string TwoButtons = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.Button1 = New Global.System.Windows.Forms.Button()
                Me.Button2 = New Global.System.Windows.Forms.Button()
                Me.Button1.Location = New System.Drawing.Point(10, 10)
                Me.Button1.Size = New System.Drawing.Size(80, 25)
                Me.Button1.Text = "One"
                Me.Button2.Location = New System.Drawing.Point(150, 60)
                Me.Button2.Size = New System.Drawing.Size(80, 25)
                Me.Button2.Text = "Two"
                Me.Controls.Add(Me.Button1)
                Me.Controls.Add(Me.Button2)
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Text = "Main"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Button2 As System.Windows.Forms.Button
        End Class
        """;

    private static async Task<(Window Window, WinFormsDesignerView View, WinFormsDesignSurface Surface)> OpenAsync()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", TwoButtons, true);
        var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null);
        return (window, view, surface);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "The designer did not reach the expected state.");
    }

    // An edit replaces the live controls, so every check finds them again by name.
    private static Forms.Control Live(WinFormsDesignSurface surface, string name) =>
        surface.RootForm!.Controls.Find(name, false).Single();

    private static MenuEntry Entry(IReadOnlyList<MenuEntry> level, params string[] headers)
    {
        MenuEntry? found = null;
        foreach (var header in headers)
        {
            found = level.Single(entry => entry.Header == header);
            level = found.Children ?? [];
        }
        return found!;
    }

    [AvaloniaFact]
    public async Task DesignerMenuAlignRunsOnTheWinFormsDesignerInFront()
    {
        var (window, view, surface) = await OpenAsync();
        var first = Live(surface, "Button1");
        var second = Live(surface, "Button2");
        surface.SelectControls([first, second], primary: second);

        Assert.True(WinFormsDesignerCommands.TryRun(view, WinFormsDesignerCommand.AlignLeft));

        // Align Lefts moves the others to the left of the last one selected.
        await WaitUntilAsync(() => Live(surface, "Button1").Left == Live(surface, "Button2").Left);
        Assert.False(WinFormsDesignerCommands.TryRun(null, WinFormsDesignerCommand.AlignLeft));
        window.Close();
    }

    [AvaloniaFact]
    public async Task FormatCenterInFormCentresTheSelectionInTheForm()
    {
        var (window, view, surface) = await OpenAsync();
        surface.Select(Live(surface, "Button2"));

        var entries = WinFormsDesignerCommands.FormatEntries(() => view);
        Entry(entries, Localizer.Get(StringKeys.MenuFormatCenterInForm),
            Localizer.Get(StringKeys.MenuFormatCenterHorizontally)).Action!();

        var client = surface.RootForm!.ClientSize.Width;
        await WaitUntilAsync(() =>
        {
            var button = Live(surface, "Button2");
            return Math.Abs(button.Left + button.Width / 2 - client / 2) <= 1;
        });
        window.Close();
    }

    [AvaloniaFact]
    public async Task DeleteRunsOnTheSelectionOfTheWinFormsDesignerInFront()
    {
        var (window, view, surface) = await OpenAsync();
        surface.Select(Live(surface, "Button1"));

        Assert.True(WinFormsDesignerCommands.TryRun(view, WinFormsDesignerCommand.Delete));

        await WaitUntilAsync(() => surface.RootForm!.Controls.Find("Button1", false).Length == 0);
        window.Close();
    }

    /// <summary>Edit &gt; Select All, Copy, Cut and Paste, invoked from the menu bar of the main window, reach the WinForms designer in front.</summary>
    [AvaloniaFact]
    public async Task EditMenuEntriesReachTheWinFormsDesignerInFront()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-edit-menu-").FullName;
        using var host = new TestWindow();
        try
        {
            var formsPath = Path.Combine(folder, "MainForm.Designer.vb");
            await File.WriteAllTextAsync(formsPath, TwoButtons);
            var model = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
            await model.OpenFileAsync(formsPath, inDesigner: true);
            await host.SettleAsync();
            var formsHost = Assert.Single(host.Window.GetVisualDescendants().OfType<DesignerHost>());
            var surface = Assert.Single(formsHost.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);

            UserJourneyItemsTests.InvokeMenu(host.Window, Localizer.Get(StringKeys.MenuEdit), Localizer.Get(StringKeys.MenuEditSelectAll));
            Assert.Equal(2, surface.SelectedControls.Count);

            // Copy alone changes nothing: the paste that follows adds copies, and the originals stay.
            UserJourneyItemsTests.InvokeMenu(host.Window, Localizer.Get(StringKeys.MenuEdit), Localizer.Get(StringKeys.MenuEditCopy));
            Assert.Equal(2, surface.RootForm!.Controls.OfType<Forms.Button>().Count());

            UserJourneyItemsTests.InvokeMenu(host.Window, Localizer.Get(StringKeys.MenuEdit), Localizer.Get(StringKeys.MenuEditPaste));
            await WaitUntilAsync(() => surface.RootForm!.Controls.OfType<Forms.Button>().Count() == 4);
            Assert.Contains(surface.RootForm!.Controls.OfType<Forms.Button>(), button => button.Name == "Button1");
            Assert.Contains(surface.RootForm!.Controls.OfType<Forms.Button>(), button => button.Name == "Button2");

            UserJourneyItemsTests.InvokeMenu(host.Window, Localizer.Get(StringKeys.MenuEdit), Localizer.Get(StringKeys.MenuEditCut));
            await WaitUntilAsync(() => surface.RootForm!.Controls.OfType<Forms.Button>().Count() < 4);
        }
        finally
        {
            host.Dispose();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Edit &gt; Delete with a XAML designer in front removes the selected element, as the Designer menu does.</summary>
    [AvaloniaFact]
    public async Task EditDeleteRemovesTheSelectionOfAXamlDesignerInFront()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-edit-delete-xaml-").FullName;
        using var host = new TestWindow();
        try
        {
            var avaloniaPath = Path.Combine(folder, "MainWindow.axaml");
            await File.WriteAllTextAsync(avaloniaPath, """
                <Window xmlns="https://github.com/avaloniaui">
                    <StackPanel><Button Content="Hello" /></StackPanel>
                </Window>
                """);
            var model = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
            await model.OpenFileAsync(avaloniaPath, inDesigner: true);
            await host.SettleAsync();
            var session = Assert.IsAssignableFrom<Basalt.Designer.DesignerSession>(model.ActiveDesigner);
            var button = session.Document.Root.Descendants().First(element => element.Name.LocalName == "Button");
            session.Select(button);

            UserJourneyItemsTests.InvokeMenu(host.Window, Localizer.Get(StringKeys.MenuEdit), Localizer.Get(StringKeys.MenuEditDelete));

            Assert.DoesNotContain(session.Document.Root.Descendants(), element => element.Name.LocalName == "Button");
        }
        finally
        {
            host.Dispose();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
