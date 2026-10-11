using Avalonia.Controls;
using AvaloniaEdit;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>What the editor's context menu can ask for.</summary>
public enum EditorCommand
{
    Cut, Copy, Paste, SelectAll,
    GoToDefinition, FindReferences, Rename,
    QuickActions, ExtractVariable, ExtractConstant, ExtractMethod,
    InlineVariable, ConvertConditional,
    GenerateConstructor, GenerateProperty, MoveTypeToFile,
    ChangeSignature, TidyImports, AddImport, ExtractPartial,
    FormatSelection, FormatDocument,
    ToggleBreakpoint, RunToHere,
    AskAssistant, ExplainWithAssistant
}

/// <summary>
/// The menu shown on a right click in the editor.
///
/// Items are disabled rather than hidden when they do not apply: a menu whose
/// entries move about is one the user has to read every time, while a greyed
/// entry says both what exists and why it is unavailable.
/// </summary>
public sealed class EditorContextMenu
{
    private readonly TextEditor _editor;
    private readonly Dictionary<EditorCommand, MenuItem> _items = [];

    public EditorContextMenu(TextEditor editor)
    {
        _editor = editor;

        Menu = Build();
        Menu.Opening += (_, _) => UpdateAvailability();

        editor.ContextMenu = Menu;
    }

    public ContextMenu Menu { get; }

    /// <summary>Raised when the user picks something.</summary>
    public event EventHandler<EditorCommand>? CommandChosen;

    internal IReadOnlyDictionary<EditorCommand, MenuItem> Items => _items;

    /// <summary>Whether a command is currently available, for tests.</summary>
    internal bool IsEnabled(EditorCommand command) =>
        _items.TryGetValue(command, out var item) && item.IsEnabled;

    internal void ChooseForTests(EditorCommand command) =>
        CommandChosen?.Invoke(this, command);

    internal void UpdateAvailabilityForTests() => UpdateAvailability();

    private ContextMenu Build()
    {
        var menu = new ContextMenu();

        menu.ItemsSource = new object[]
        {
            Item(EditorCommand.Cut, Localizer.Get(StringKeys.EditorMenuCut), "Ctrl+X"),
            Item(EditorCommand.Copy, Localizer.Get(StringKeys.EditorMenuCopy), "Ctrl+C"),
            Item(EditorCommand.Paste, Localizer.Get(StringKeys.EditorMenuPaste), "Ctrl+V"),
            new Separator(),
            Item(EditorCommand.GoToDefinition, Localizer.Get(StringKeys.EditorMenuGoToDefinition), "F12"),
            Item(EditorCommand.FindReferences, Localizer.Get(StringKeys.EditorMenuFindReferences), "Shift+F12"),
            Item(EditorCommand.Rename, Localizer.Get(StringKeys.EditorMenuRename), "F2"),
            new Separator(),
            Item(EditorCommand.QuickActions, Localizer.Get(StringKeys.EditorMenuQuickActions), "Ctrl+."),
            Item(EditorCommand.ExtractVariable, Localizer.Get(StringKeys.EditorMenuExtractVariable), "Ctrl+Alt+V"),
            Item(EditorCommand.ExtractConstant, Localizer.Get(StringKeys.EditorMenuExtractConstant)),
            Item(EditorCommand.InlineVariable, Localizer.Get(StringKeys.EditorMenuInlineVariable), "Ctrl+Alt+N"),
            Item(EditorCommand.ConvertConditional, Localizer.Get(StringKeys.EditorMenuConvertConditional)),
            Item(EditorCommand.GenerateConstructor, Localizer.Get(StringKeys.EditorMenuGenerateConstructor)),
            Item(EditorCommand.GenerateProperty, Localizer.Get(StringKeys.EditorMenuGenerateProperty)),
            Item(EditorCommand.MoveTypeToFile, Localizer.Get(StringKeys.EditorMenuMoveTypeToFile)),
            Item(EditorCommand.ChangeSignature, Localizer.Get(StringKeys.EditorMenuChangeSignature)),
            Item(EditorCommand.ExtractMethod, Localizer.Get(StringKeys.EditorMenuExtractMethod), "Ctrl+Alt+M"),
            Item(EditorCommand.AddImport, Localizer.Get(StringKeys.EditorMenuAddImport)),
            Item(EditorCommand.ExtractPartial, Localizer.Get(StringKeys.EditorMenuExtractPartial)),
            Item(EditorCommand.TidyImports, Localizer.Get(StringKeys.EditorMenuTidyImports), "Ctrl+Alt+O"),
            Item(EditorCommand.FormatSelection, Localizer.Get(StringKeys.EditorMenuFormatSelection)),
            Item(EditorCommand.FormatDocument, Localizer.Get(StringKeys.EditorMenuFormatDocument)),
            new Separator(),
            Item(EditorCommand.ToggleBreakpoint, Localizer.Get(StringKeys.EditorMenuToggleBreakpoint), "F9"),
            Item(EditorCommand.RunToHere, Localizer.Get(StringKeys.EditorMenuRunToHere)),
            new Separator(),
            Item(EditorCommand.ExplainWithAssistant, Localizer.Get(StringKeys.EditorMenuExplainWithAssistant)),
            Item(EditorCommand.AskAssistant, Localizer.Get(StringKeys.EditorMenuAskAssistant))
        };

        return menu;
    }

    private MenuItem Item(EditorCommand command, string header, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGesture = Parse(gesture) };

        // The menu had no icons at all while the ones beside it did.
        if (IconFor(command) is var icon && icon != IconKind.None)
            item.Icon = new IconView { Kind = icon, IconSize = 14 };

        item.Click += (_, _) => CommandChosen?.Invoke(this, command);

        _items[command] = item;

        return item;
    }

    /// <summary>
    /// The icon for a command, where there is one that says something.
    ///
    /// Most entries have none on purpose. Ten refactorings sharing one wrench
    /// is ten items the eye cannot tell apart: the icon stops being a way to
    /// find a command and becomes decoration in front of the words that do
    /// the work. An icon earns its place by being unmistakable — scissors for
    /// Cut, a breakpoint for Toggle Breakpoint — and nothing else gets one.
    /// </summary>
    private static IconKind IconFor(EditorCommand command) => command switch
    {
        EditorCommand.Cut => IconKind.Cut,
        EditorCommand.Copy => IconKind.Copy,
        EditorCommand.Paste => IconKind.Paste,

        EditorCommand.GoToDefinition => IconKind.GoToDefinition,
        EditorCommand.FindReferences => IconKind.FindReferences,

        EditorCommand.ToggleBreakpoint => IconKind.Breakpoint,
        EditorCommand.RunToHere => IconKind.Run,

        EditorCommand.AskAssistant or EditorCommand.ExplainWithAssistant => IconKind.Assistant,

        _ => IconKind.None
    };

    private static Avalonia.Input.KeyGesture? Parse(string? gesture) =>
        gesture is null ? null : Avalonia.Input.KeyGesture.Parse(gesture);

    /// <summary>
    /// Greys out what cannot be done right now.
    ///
    /// Cutting needs a selection; formatting a selection needs one too;
    /// pasting needs something on the clipboard, which is not worth asking
    /// for on every right click, so it stays available.
    /// </summary>
    private void UpdateAvailability()
    {
        var hasSelection = _editor.SelectionLength > 0;

        Set(EditorCommand.Cut, hasSelection);
        Set(EditorCommand.Copy, hasSelection);
        Set(EditorCommand.FormatSelection, hasSelection);
        Set(EditorCommand.ExplainWithAssistant, hasSelection);
        Set(EditorCommand.ExtractVariable, hasSelection);
        Set(EditorCommand.ExtractConstant, hasSelection);
        Set(EditorCommand.ExtractMethod, hasSelection);

        // These need a symbol under the caret; the caret is always somewhere,
        // and what it is on is decided when the command runs.
        Set(EditorCommand.GoToDefinition, true);
        Set(EditorCommand.FindReferences, true);
        Set(EditorCommand.Rename, true);
        Set(EditorCommand.InlineVariable, true);
        Set(EditorCommand.ConvertConditional, true);
        Set(EditorCommand.GenerateConstructor, true);
        Set(EditorCommand.GenerateProperty, true);
        Set(EditorCommand.MoveTypeToFile, true);
        Set(EditorCommand.ChangeSignature, true);
    }

    private void Set(EditorCommand command, bool enabled)
    {
        if (_items.TryGetValue(command, out var item)) item.IsEnabled = enabled;
    }
}
