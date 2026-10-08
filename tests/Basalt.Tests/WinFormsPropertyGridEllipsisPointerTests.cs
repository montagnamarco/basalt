using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Drives the Properties grid's "…" button on an Image row with a real pointer
/// click (MouseDown/MouseUp on the window, at the button's rendered centre),
/// inside the full <see cref="MainWindow"/> the way a user actually clicks it -
/// not <see cref="Button.ClickEvent"/> raised directly on the control, which
/// <see cref="WinFormsImageEditorUiTests"/> uses and which bypasses whatever a
/// real pointer press does to the row underneath the button.
///
/// Bug report: clicking "…" on Image refreshes the Properties list but never
/// opens <see cref="WinFormsImageResourceDialog"/>.
/// </summary>
public sealed class WinFormsPropertyGridEllipsisPointerTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-propertygrid-ellipsis-pointer-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private const string DesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.Button1.Location = New System.Drawing.Point(40, 40)
                Me.Button1.Size = New System.Drawing.Size(90, 30)
                Me.Button1.Text = "Button1"
                Me.Controls.Add(Me.Button1)
                Me.PictureBox1 = New System.Windows.Forms.PictureBox()
                Me.PictureBox1.Location = New System.Drawing.Point(40, 100)
                Me.PictureBox1.Size = New System.Drawing.Size(60, 60)
                Me.Controls.Add(Me.PictureBox1)
                Me.ClientSize = New System.Drawing.Size(320, 240)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub

            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents PictureBox1 As System.Windows.Forms.PictureBox
        End Class
        """;

    [AvaloniaFact]
    public async Task ClickingTheEllipsisOnAnAlreadySelectedRowOpensTheImageDialog()
    {
        var designerPath = Path.Combine(_root, "Form1.Designer.vb");
        await File.WriteAllTextAsync(designerPath, DesignerSource);

        using var window = new TestWindow();
        var vm = (MainWindowViewModel)window.Window.DataContext!;
        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await window.SettleAsync();

        var surface = Assert.Single(window.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null);

        SelectByPointer(window, surface, 40, 40); // Button1's top-left corner, inside its bounds.
        Assert.Equal("Button1", surface.SelectedControl?.Name);
        await window.SettleAsync();

        // Select the row once first (as if the user had already clicked the
        // row before reaching for the ellipsis), then click the ellipsis.
        var grid = Assert.Single(window.Window.GetVisualDescendants().OfType<PropertyGrid>());
        var row = await WaitForImageRowAsync(grid);
        // The row may be scrolled out of the viewport (Image is not always
        // near the top of a Button's Appearance category): a real user would
        // have scrolled the Properties grid to see it before clicking on it.
        row.BringIntoView();
        await window.SettleAsync();
        ClickRowLabel(window, row);
        await window.SettleAsync();

        ClickEllipsis(window, row);

        await WaitUntilAsync(() => window.Window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
    }

    [AvaloniaFact]
    public async Task ClickingTheEllipsisOnAnUnselectedRowOpensTheImageDialog()
    {
        var designerPath = Path.Combine(_root, "Form1.Designer.vb");
        await File.WriteAllTextAsync(designerPath, DesignerSource);

        using var window = new TestWindow();
        var vm = (MainWindowViewModel)window.Window.DataContext!;
        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await window.SettleAsync();

        var surface = Assert.Single(window.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null);

        SelectByPointer(window, surface, 40, 40); // Button1's top-left corner.
        Assert.Equal("Button1", surface.SelectedControl?.Name);
        await window.SettleAsync();

        // Select a different row first so the Image row starts unselected,
        // then click the ellipsis without ever selecting the row itself.
        var grid = Assert.Single(window.Window.GetVisualDescendants().OfType<PropertyGrid>());
        var otherRow = Assert.Single(grid.Rows.OfType<Border>(),
            candidate => candidate.Name == "PropertyGridRow"
                && candidate.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Text"));
        otherRow.BringIntoView();
        await window.SettleAsync();
        ClickRowLabel(window, otherRow);
        await window.SettleAsync();

        var row = await WaitForImageRowAsync(grid);
        row.BringIntoView();
        await window.SettleAsync();
        ClickEllipsis(window, row);

        await WaitUntilAsync(() => window.Window.OwnedWindows.OfType<WinFormsImageResourceDialog>().Any());
    }

    private static void SelectByPointer(TestWindow window, WinFormsDesignSurface surface, double x, double y)
    {
        var canvas = (Canvas)typeof(WinFormsDesignSurface)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(surface)!;
        // 12: the surface's gutter. 4, 23: Form1's own default frame (Sizable
        // border, captioned since it has a non-empty Text).
        var point = canvas.TranslatePoint(new Point(12 + 4 + x, 12 + 23 + y), window.Window)!.Value;
        window.Window.MouseDown(point, MouseButton.Left);
        window.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<Border> WaitForImageRowAsync(PropertyGrid grid)
    {
        await WaitUntilAsync(() => grid.Rows.OfType<Border>().Any(row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Image")));
        return Assert.Single(grid.Rows.OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Image"));
    }

    /// <summary>A real pointer click on the row's own name column (far left, clear of
    /// any editor or the ellipsis), the way a user selects a row before reaching for
    /// its editor - not <see cref="InputElement.Focus()"/> or an event raised directly
    /// on the row.</summary>
    private static void ClickRowLabel(TestWindow window, Border row)
    {
        var bounds = Raster.BoundsIn(row, window.Window);
        var point = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);
        Log($"ClickRowLabel bounds={bounds} point={point} rowVisible={row.IsEffectivelyVisible} windowSize={window.Window.Bounds}");
        window.Window.MouseDown(point, MouseButton.Left);
        window.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A real pointer click at the rendered centre of the row's "…" button,
    /// the way a user actually presses it - not <see cref="Button.ClickEvent"/> raised
    /// directly on the control.</summary>
    private static void ClickEllipsis(TestWindow window, Border row)
    {
        var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => b.Name == "PropertyGridEllipsis");
        var bounds = Raster.BoundsIn(ellipsis, window.Window);
        var centre = new Point(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
        var hit = window.Window.InputHitTest(centre);
        var chain = new List<string>();
        for (var v = hit as Avalonia.Visual; v is not null; v = v.GetVisualParent())
            chain.Add(v.GetType().Name + (v is Control c ? $"[{c.Name}]" : ""));
        Log($"ClickEllipsis bounds={bounds} centre={centre} visible={ellipsis.IsEffectivelyVisible} hit={hit?.GetType().FullName} chain={string.Join(" > ", chain)}");
        window.Window.MouseDown(centre, MouseButton.Left);
        window.Window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private const string LogPath = "/private/tmp/claude-501/-Users-marco-Sources-my-ide/1170799a-8c3c-4087-a4a7-6ce6990271a7/scratchpad/live.log";
    private static void Log(string message) => File.AppendAllText(LogPath, "[TESTDIAG] " + message + "\n");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The ellipsis click did not open the Select Resource dialog.");
    }
}
