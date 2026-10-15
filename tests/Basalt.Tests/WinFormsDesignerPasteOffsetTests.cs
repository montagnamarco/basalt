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
/// Paste places a copied control the way Visual Studio does: on the same form, the copy is offset from the original by the
/// grid size, and each further paste offsets again; into another container it keeps the original's location.
/// </summary>
public sealed class WinFormsDesignerPasteOffsetTests : IDisposable
{
    public WinFormsDesignerPasteOffsetTests() => WinFormsDesignerView.ResetClipboardForTests();

    public void Dispose() => WinFormsDesignerView.ResetClipboardForTests();

    // A button directly on the form and an empty panel, so one paste can target the form and the other the panel.
    private const string ButtonAndPanel = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.Button1 = New Global.System.Windows.Forms.Button()
                Me.Panel1 = New Global.System.Windows.Forms.Panel()
                Me.Button1.Location = New System.Drawing.Point(150, 150)
                Me.Button1.Size = New System.Drawing.Size(80, 25)
                Me.Button1.Text = "One"
                Me.Panel1.Location = New System.Drawing.Point(150, 60)
                Me.Panel1.Size = New System.Drawing.Size(120, 80)
                Me.Controls.Add(Me.Button1)
                Me.Controls.Add(Me.Panel1)
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Text = "Main"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Panel1 As System.Windows.Forms.Panel
        End Class
        """;

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

    private static IEnumerable<Forms.Button> Buttons(WinFormsDesignSurface surface) =>
        surface.RootForm!.Controls.OfType<Forms.Button>();

    /// <summary>Opens the fixture in a main window and waits until its designer has built the form.</summary>
    private static async Task<(TestWindow Host, WinFormsDesignSurface Surface, string Folder)> OpenAsync()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-paste-offset-").FullName;
        var host = new TestWindow();
        var formsPath = Path.Combine(folder, "MainForm.Designer.vb");
        await File.WriteAllTextAsync(formsPath, ButtonAndPanel);
        var model = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
        await model.OpenFileAsync(formsPath, inDesigner: true);
        await host.SettleAsync();
        var formsHost = Assert.Single(host.Window.GetVisualDescendants().OfType<DesignerHost>());
        var surface = Assert.Single(formsHost.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null);
        return (host, surface, folder);
    }

    private static void Dismiss(TestWindow host, string folder)
    {
        host.Dispose();
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>Edit &gt; Copy and Edit &gt; Paste, invoked from the menu bar of the main window, as a user does.</summary>
    private static void EditMenu(TestWindow host, string command) =>
        UserJourneyItemsTests.InvokeMenu(host.Window, Localizer.Get(StringKeys.MenuEdit), command);

    [AvaloniaFact]
    public async Task PasteOntoTheSameFormOffsetsTheCopyByTheGridFromTheOriginal()
    {
        var (host, surface, folder) = await OpenAsync();
        try
        {
            var original = Live(surface, "Button1");
            var start = original.Location;
            surface.Select(original);
            EditMenu(host, Localizer.Get(StringKeys.MenuEditCopy));

            EditMenu(host, Localizer.Get(StringKeys.MenuEditPaste));
            await WaitUntilAsync(() => Buttons(surface).Count() == 2);

            // The grid is 8 by 8, so the copy sits one grid step right and down from the original.
            var grid = surface.GridSize;
            var copy = Buttons(surface).Single(button => button.Name != "Button1");
            Assert.Equal(new System.Drawing.Point(start.X + grid.Width, start.Y + grid.Height), copy.Location);
            Assert.Equal(start, Live(surface, "Button1").Location);

            // Pasting again offsets again, from the original, so the second copy is two steps away.
            var namesBefore = Buttons(surface).Select(button => button.Name).ToArray();
            EditMenu(host, Localizer.Get(StringKeys.MenuEditPaste));
            await WaitUntilAsync(() => Buttons(surface).Count() == 3);
            var second = Buttons(surface).Single(button => !namesBefore.Contains(button.Name));
            Assert.Equal(new System.Drawing.Point(start.X + 2 * grid.Width, start.Y + 2 * grid.Height), second.Location);
        }
        finally
        {
            Dismiss(host, folder);
        }
    }

    /// <summary>
    /// Repeated pastes walk right and down until the next copy would leave the form's client area, then wrap to one step from the
    /// top-left corner, so every copy stays visible. Button1 sits at (150, 150) in a 300 by 200 form: at 8 px steps the third copy
    /// still fits, the fourth would not.
    /// </summary>
    [AvaloniaFact]
    public async Task RepeatedPastesNearTheEdgeWrapBackInsideTheForm()
    {
        var (host, surface, folder) = await OpenAsync();
        try
        {
            var start = Live(surface, "Button1").Location;
            var grid = surface.GridSize;
            surface.Select(Live(surface, "Button1"));
            EditMenu(host, Localizer.Get(StringKeys.MenuEditCopy));

            var expected = new[]
            {
                new System.Drawing.Point(start.X + grid.Width, start.Y + grid.Height),
                new System.Drawing.Point(start.X + 2 * grid.Width, start.Y + 2 * grid.Height),
                new System.Drawing.Point(start.X + 3 * grid.Width, start.Y + 3 * grid.Height),
                new System.Drawing.Point(grid.Width, grid.Height),
            };
            for (var paste = 0; paste < expected.Length; paste++)
            {
                var namesBefore = Buttons(surface).Select(button => button.Name).ToArray();
                EditMenu(host, Localizer.Get(StringKeys.MenuEditPaste));
                await WaitUntilAsync(() => Buttons(surface).Count() == namesBefore.Length + 1);
                var copy = Buttons(surface).Single(button => !namesBefore.Contains(button.Name));
                Assert.Equal(expected[paste], copy.Location);
            }

            var client = surface.RootForm!.ClientSize;
            var last = Buttons(surface).Single(button => button.Location == expected[^1]);
            Assert.True(last.Right <= client.Width && last.Bottom <= client.Height, "The wrapped copy left the form.");
        }
        finally
        {
            Dismiss(host, folder);
        }
    }

    /// <summary>
    /// Pasting into a panel, a different container from the one the control was copied from, keeps the copy at its original
    /// location. The same test pastes onto the form, where the copy is offset, so the difference between the two is the rule.
    /// </summary>
    [AvaloniaFact]
    public async Task PasteIntoAnotherContainerKeepsTheOriginalLocationWhileTheFormOffsets()
    {
        var (host, surface, folder) = await OpenAsync();
        try
        {
            var start = Live(surface, "Button1").Location;
            var grid = surface.GridSize;
            surface.Select(Live(surface, "Button1"));
            EditMenu(host, Localizer.Get(StringKeys.MenuEditCopy));

            var formNames = Buttons(surface).Select(button => button.Name).ToArray();
            EditMenu(host, Localizer.Get(StringKeys.MenuEditPaste));
            await WaitUntilAsync(() => Buttons(surface).Count() == formNames.Length + 1);
            var formCopy = Buttons(surface).Single(button => !formNames.Contains(button.Name));
            Assert.Equal(new System.Drawing.Point(start.X + grid.Width, start.Y + grid.Height), formCopy.Location);

            surface.Select(Live(surface, "Panel1"));
            EditMenu(host, Localizer.Get(StringKeys.MenuEditPaste));
            // An edit replaces the live controls, so the panel is found again after the paste.
            await WaitUntilAsync(() => ((Forms.Panel)Live(surface, "Panel1")).Controls.OfType<Forms.Button>().Any());

            var panelCopy = ((Forms.Panel)Live(surface, "Panel1")).Controls.OfType<Forms.Button>().Single();
            Assert.Equal(start, panelCopy.Location);
        }
        finally
        {
            Dismiss(host, folder);
        }
    }
}
