namespace Basalt.Shell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<(string Path, bool InDesigner), Task> _pendingExplorerOpens = [];

    /// <summary>Opens a form in its generated designer and other files in their usual view.</summary>
    public Task OpenExplorerNodeAsync(SolutionTreeNode node)
    {
        if (node.DesignerPath is { } designerPath)
            return OpenExplorerFileAsync(designerPath, inDesigner: true);

        return OpenExplorerFileAsync(node.Path, inDesigner: node.Kind == NodeKind.XamlFile);
    }

    /// <summary>Opens the handwritten code behind a form.</summary>
    public Task OpenExplorerCodeAsync(SolutionTreeNode node) =>
        OpenExplorerFileAsync(node.Path, inDesigner: false);

    /// <summary>Explicitly opens the design surface for a designable file.</summary>
    public Task OpenExplorerDesignerAsync(SolutionTreeNode node) =>
        OpenExplorerFileAsync(node.DesignerPath ?? node.Path, inDesigner: true);

    private Task OpenExplorerFileAsync(string path, bool inDesigner)
    {
        var key = (path, inDesigner);
        lock (_pendingExplorerOpens)
        {
            if (_pendingExplorerOpens.TryGetValue(key, out var pending)) return pending;

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingExplorerOpens.Add(key, completion.Task);
            _ = CompleteAsync();
            return completion.Task;

            async Task CompleteAsync()
            {
                try
                {
                    await OpenFileAsync(path, inDesigner).ConfigureAwait(true);
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
                finally
                {
                    lock (_pendingExplorerOpens) _pendingExplorerOpens.Remove(key);
                }
            }
        }
    }
}
