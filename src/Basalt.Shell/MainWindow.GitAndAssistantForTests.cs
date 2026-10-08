using Basalt.Shell.Controls;

namespace Basalt.Shell;

/// <summary>
/// Test-only windows into the git and AI assistant panels <see cref="MainWindow"/>
/// builds and wires to the real services.
///
/// Kept apart from <c>MainWindow.axaml.cs</c> so that file does not grow for a
/// handful of accessors a test needs to reach a real, live-wired panel rather
/// than one built in isolation.
/// </summary>
public partial class MainWindow
{
    internal GitChangesPanel? GitChangesForTests => _gitChanges;
    internal GitHistoryPanel? GitHistoryForTests => _gitHistory;
    internal BranchPanel? BranchesForTests => _branches;
    internal DiffView? GitDiffForTests => _diff;
    internal AiChatPanel? AssistantForTests => _assistant;
}
