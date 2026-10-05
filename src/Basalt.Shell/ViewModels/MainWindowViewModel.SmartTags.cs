using Basalt.Workspace;

namespace Basalt.Shell.ViewModels;

public sealed partial class MainWindowViewModel
{
    internal Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(string filePath, string source,
        int position, CancellationToken cancellationToken) => Task.Run(async () =>
        {
            return await _languageService.GetQuickActionsAsync(filePath, source, position, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
}
