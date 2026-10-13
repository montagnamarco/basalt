using Basalt.Core.Features;
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
    /// <param name="features">
    /// What the window offers. Left out, it is what a user sees; a test of the
    /// legacy Basic dialects passes <c>new IdeFeatures { LegacyBasic = true }</c>.
    /// </param>
    public TestWindow(string? solutionToOpen = null, IdeFeatures? features = null)
    {
        Window = new MainWindow(solutionToOpen, null, features);
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

    /// <summary>
    /// Closes the window without the "Save changes?" prompt: a test that
    /// deliberately left a dirty document open would otherwise have its
    /// dialog stay up forever, Closed never raised, and the window's
    /// terminal processes outlive the test.
    /// </summary>
    public void Dispose()
    {
        Window.SkipSaveChangesPromptForTests = true;
        Window.Close();
    }
}
