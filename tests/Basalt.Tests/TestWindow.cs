using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// A MainWindow that closes itself when the test ends.
///
/// Each window owns terminal panels, and every terminal holds a live shell
/// process. Leaving windows open makes those processes outlive the test and
/// hang the test runner once several tests have run.
/// </summary>
public sealed class TestWindow : IDisposable
{
    public TestWindow(string? solutionToOpen = null)
    {
        Window = new MainWindow(solutionToOpen);
        Window.Show();
    }

    public MainWindow Window { get; }

    /// <summary>
    /// Runs several layout passes with short pauses.
    ///
    /// Dock builds its controls over more than one layout pass, so a single
    /// UpdateLayout is not enough for panels to appear in the visual tree.
    /// </summary>
    public async Task SettleAsync(int passes = 5)
    {
        for (var i = 0; i < passes; i++)
        {
            Window.UpdateLayout();
            await Task.Delay(60);
        }
        Window.UpdateLayout();
    }

    public static implicit operator MainWindow(TestWindow window) => window.Window;

    public void Dispose() => Window.Close();
}
