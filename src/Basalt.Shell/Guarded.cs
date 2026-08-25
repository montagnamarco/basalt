namespace Basalt.Shell;

/// <summary>
/// Running work started from an event handler without risking the process.
///
/// An "async void" handler that throws has nowhere to report to: the
/// exception reaches the runtime and takes the application down. There were
/// forty of them, and pressing Start Debug went through one — a crash where
/// a line in the output pane was the right answer.
///
/// Cancellation is not a failure: it happens whenever a panel is closed while
/// it is still loading, and reporting it would be noise.
/// </summary>
public static class Guarded
{
    /// <summary>
    /// Runs the work, reporting anything it throws instead of letting it
    /// escape.
    /// </summary>
    public static async void Run(Func<Task> work, Action<string> report, string what)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The user closed something, or a newer request replaced this one.
        }
        catch (Exception ex)
        {
            report($"[{what}] {ex.Message}");
        }
    }
}
