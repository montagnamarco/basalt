using Basalt.Workspace.ObjectBrowser;

namespace Basalt.Shell.ViewModels;

/// <summary>What the Object Browser reads from the open solution.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// The browser's questions to the compiler. Handed out as a loader so
    /// that every panel built on it gets the newest-answer-wins behaviour.
    /// </summary>
    internal ObjectBrowserLoader CreateObjectBrowserLoader() =>
        new(new ObjectBrowserService(_languageService));

    /// <summary>
    /// Raised when another solution has been loaded or the text of one of its files changed. Not necessarily on the
    /// interface thread: a subscriber that touches controls must marshal.
    /// </summary>
    internal event EventHandler? BrowsableSolutionChanged
    {
        add
        {
            _languageService.SolutionChanged += value;
            _languageService.DocumentTextChanged += value;
        }
        remove
        {
            _languageService.SolutionChanged -= value;
            _languageService.DocumentTextChanged -= value;
        }
    }
}
