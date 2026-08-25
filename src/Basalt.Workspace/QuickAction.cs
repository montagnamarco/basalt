using Microsoft.CodeAnalysis.CodeActions;

namespace Basalt.Workspace;

/// <summary>
/// A fix the IDE can offer for a problem in the code.
///
/// The Roslyn action is carried along rather than being turned into edits
/// straight away: computing the change is expensive, and most of the actions
/// offered are never chosen.
/// </summary>
public sealed record QuickAction(string Title, CodeAction Action);
