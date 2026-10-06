using Basalt.Core.Settings;
using Basalt.Extensibility;
using Basalt.Workspace.Ai;

namespace Basalt.Shell;

public partial class MainWindow
{
    private AiSettings? _inlineAiSettings;
    private AiInlineSuggestionProvider? _inlineProvider;

    /// <summary>
    /// The one source of inline suggestions for every editor in the window:
    /// the assistant chosen for the chat, with the chat's key store.
    /// </summary>
    /// <remarks>
    /// It reads a snapshot of the settings rather than the settings file: it
    /// is asked on the interface thread at every pause in typing, and the file
    /// is read from disk each time it is loaded. The snapshot is replaced when
    /// settings change.
    /// </remarks>
    private IInlineSuggestionProvider InlineSuggestions => _inlineProvider ??= CreateInlineSuggestions();

    private AiInlineSuggestionProvider CreateInlineSuggestions()
    {
        _inlineAiSettings = _settingsStore.Load().Ai;

        // The store announces every load and save, which is when the
        // settings window has changed something.
        // The key kept for the session is forgotten too, but not the
        // connection: every load announces itself (opening a file is one), and
        // a request may be streaming on it.
        _settingsStore.Changed += (_, settings) =>
        {
            _inlineAiSettings = settings.Ai;
            _inlineProvider?.ForgetKey();
        };

        return new AiInlineSuggestionProvider(() => _inlineAiSettings ?? new AiSettings(), vendor => _apiKeys.Get(vendor));
    }

    /// <summary>
    /// A key saved or removed in the settings window. The key store raises no
    /// settings change, so without this a removed key would still be used
    /// until the cached one expires.
    /// </summary>
    private void OnApiKeyChanged() => _inlineProvider?.ForgetKey();
}
