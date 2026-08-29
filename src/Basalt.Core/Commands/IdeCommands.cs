namespace Basalt.Core.Commands;

/// <summary>Which set of shortcuts to follow.</summary>
public enum KeyboardScheme { Basalt, VisualStudio, VisualStudioCode }

/// <summary>
/// Every command the IDE offers.
///
/// One place, so that a command added here appears in the menus, the palette
/// and the shortcut settings without any of them being touched.
/// </summary>
public static class IdeCommands
{
    // The ids are strings rather than an enum so that a language or an
    // extension can add its own without changing this file.

    public const string FileNewSolution = "file.newSolution";
    public const string FileOpenSolution = "file.openSolution";
    public const string FileSave = "file.save";
    public const string FileSaveAll = "file.saveAll";
    public const string FileClose = "file.close";

    public const string EditUndo = "edit.undo";
    public const string EditRedo = "edit.redo";
    public const string EditCut = "edit.cut";
    public const string EditCopy = "edit.copy";
    public const string EditPaste = "edit.paste";
    public const string EditFind = "edit.find";
    public const string EditReplace = "edit.replace";
    public const string EditFormatDocument = "edit.formatDocument";
    public const string EditFormatSelection = "edit.formatSelection";
    public const string EditQuickInfo = "edit.quickInfo";
    public const string EditToggleFold = "edit.toggleFold";
    public const string EditFoldAll = "edit.foldAll";
    public const string EditUnfoldAll = "edit.unfoldAll";
    public const string EditToggleComment = "edit.toggleComment";

    public const string ViewCommandPalette = "view.commandPalette";
    public const string ViewSolutionExplorer = "view.solutionExplorer";
    public const string ViewProblems = "view.problems";
    public const string ViewOutput = "view.output";
    public const string ViewTerminal = "view.terminal";
    public const string ViewTests = "view.tests";
    public const string ViewAssistant = "view.assistant";

    public const string NavigateGoToDefinition = "navigate.goToDefinition";
    public const string NavigateFindReferences = "navigate.findReferences";
    public const string NavigateGoToFile = "navigate.goToFile";
    public const string NavigateGoToSymbol = "navigate.goToSymbol";
    public const string NavigateGoToLine = "navigate.goToLine";
    public const string NavigateBack = "navigate.back";
    public const string NavigateForward = "navigate.forward";

    public const string RefactorRename = "refactor.rename";
    public const string RefactorQuickActions = "refactor.quickActions";
    public const string RefactorExtractMethod = "refactor.extractMethod";
    public const string RefactorExtractVariable = "refactor.extractVariable";
    public const string RefactorTidyImports = "refactor.tidyImports";
    public const string RefactorExtractConstant = "refactor.extractConstant";
    public const string RefactorAddImport = "refactor.addImport";
    public const string RefactorInlineVariable = "refactor.inlineVariable";
    public const string RefactorConvertConditional = "refactor.convertConditional";
    public const string RefactorGenerateConstructor = "refactor.generateConstructor";
    public const string RefactorGenerateProperty = "refactor.generateProperty";
    public const string RefactorMoveTypeToFile = "refactor.moveTypeToFile";
    public const string RefactorChangeSignature = "refactor.changeSignature";

    public const string BuildSolution = "build.solution";
    public const string BuildRebuild = "build.rebuild";
    public const string BuildClean = "build.clean";

    public const string DebugStart = "debug.start";
    public const string DebugStartWithout = "debug.startWithoutDebugging";
    public const string DebugContinue = "debug.continue";
    public const string DebugPause = "debug.pause";
    public const string DebugStop = "debug.stop";
    public const string DebugStepOver = "debug.stepOver";
    public const string DebugStepInto = "debug.stepInto";
    public const string DebugStepOut = "debug.stepOut";
    public const string DebugToggleBreakpoint = "debug.toggleBreakpoint";
    public const string DebugRunToCursor = "debug.runToCursor";

    public const string TestRunAll = "test.runAll";
    public const string TestRunSelected = "test.runSelected";
    public const string TestRunFailed = "test.runFailed";
    public const string TestDebugSelected = "test.debugSelected";

    public const string GitInitialize = "git.initialize";
    public const string GitCommit = "git.commit";
    public const string GitPull = "git.pull";
    public const string GitPush = "git.push";
    public const string GitBranches = "git.branches";

    // The designer's own. Align exists in the arithmetic and was reachable
    // only from the tests; a command that nothing can invoke is a feature
    // nobody has.
    public const string DesignerAlignLeft = "designer.alignLeft";
    public const string DesignerAlignRight = "designer.alignRight";
    public const string DesignerAlignTop = "designer.alignTop";
    public const string DesignerAlignBottom = "designer.alignBottom";
    public const string DesignerAlignHorizontalCentre = "designer.alignHorizontalCentre";
    public const string DesignerAlignVerticalCentre = "designer.alignVerticalCentre";
    public const string DesignerSameWidth = "designer.sameWidth";
    public const string DesignerSameHeight = "designer.sameHeight";
    public const string DesignerZoomIn = "designer.zoomIn";
    public const string DesignerZoomOut = "designer.zoomOut";
    public const string DesignerZoomReset = "designer.zoomReset";
    public const string DesignerZoomToFit = "designer.zoomToFit";
    public const string DesignerSelectParent = "designer.selectParent";

    public const string ToolsSettings = "tools.settings";
    public const string ViewGeneratedCode = "view.generatedCode";

    /// <summary>Builds a registry holding every command, under a scheme.</summary>
    public static CommandRegistry CreateRegistry(KeyboardScheme scheme = KeyboardScheme.Basalt)
    {
        var registry = new CommandRegistry();

        registry.RegisterAll(Definitions(scheme));

        return registry;
    }

    /// <summary>
    /// The commands, with the shortcut each has under a scheme.
    ///
    /// The modifier is written as "Ctrl" throughout; on macOS the platform
    /// substitutes Cmd, which is what a Mac user expects to press.
    /// </summary>
    public static IReadOnlyList<IdeCommand> Definitions(KeyboardScheme scheme)
    {
        var visualStudio = scheme == KeyboardScheme.VisualStudio;
        var code = scheme == KeyboardScheme.VisualStudioCode;

        return
        [
            new(FileNewSolution, "New Solution…", CommandCategory.File)
                { DefaultGesture = "Ctrl+Shift+N" },
            new(FileOpenSolution, "Open Solution…", CommandCategory.File)
                { DefaultGesture = "Ctrl+O" },
            new(FileSave, "Save", CommandCategory.File) { DefaultGesture = "Ctrl+S" },
            new(FileSaveAll, "Save All", CommandCategory.File)
                { DefaultGesture = "Ctrl+Shift+S" },
            new(FileClose, "Close Document", CommandCategory.File)
                { DefaultGesture = "Ctrl+W" },

            new(EditUndo, "Undo", CommandCategory.Edit) { DefaultGesture = "Ctrl+Z" },
            new(EditRedo, "Redo", CommandCategory.Edit)
                { DefaultGesture = visualStudio ? "Ctrl+Y" : "Ctrl+Shift+Z" },
            new(EditCut, "Cut", CommandCategory.Edit) { DefaultGesture = "Ctrl+X" },
            new(EditCopy, "Copy", CommandCategory.Edit) { DefaultGesture = "Ctrl+C" },
            new(EditPaste, "Paste", CommandCategory.Edit) { DefaultGesture = "Ctrl+V" },
            new(EditFind, "Find", CommandCategory.Edit) { DefaultGesture = "Ctrl+F" },
            new(EditReplace, "Replace", CommandCategory.Edit)
                { DefaultGesture = code ? "Ctrl+H" : "Ctrl+H" },
            new(EditFormatDocument, "Format Document", CommandCategory.Edit)
                { DefaultGesture = code ? "Ctrl+Shift+I" : "Ctrl+K, Ctrl+D" },
            new(EditFormatSelection, "Format Selection", CommandCategory.Edit),

            // The gestures Visual Studio uses, so the fingers already
            // know them.
            // Ctrl+K, Ctrl+I as in Visual Studio: the same description the
            // pointer shows, for anyone whose hands are on the keyboard.
            new(EditQuickInfo, "Quick Info", CommandCategory.Edit)
                { DefaultGesture = "Ctrl+K, Ctrl+I" },
            new(EditToggleFold, "Fold or Unfold", CommandCategory.Edit)
                { DefaultGesture = "Ctrl+M, Ctrl+M" },
            new(EditFoldAll, "Fold All", CommandCategory.Edit)
                { DefaultGesture = "Ctrl+M, Ctrl+O" },
            new(EditUnfoldAll, "Unfold All", CommandCategory.Edit)
                { DefaultGesture = "Ctrl+M, Ctrl+L" },
            new(EditToggleComment, "Toggle Comment", CommandCategory.Edit)
                { DefaultGesture = "Ctrl+/" },

            new(ViewCommandPalette, "Command Palette…", CommandCategory.View)
                { DefaultGesture = "Ctrl+Shift+P",
                  Description = "Find and run any command by name." },
            new(ViewSolutionExplorer, "Solution Explorer", CommandCategory.View),
            new(ViewProblems, "Problems", CommandCategory.View),
            new(ViewOutput, "Output", CommandCategory.View),
            new(ViewTerminal, "Terminal", CommandCategory.View)
                { DefaultGesture = "Ctrl+`" },
            new(ViewTests, "Tests", CommandCategory.View),
            new(ViewAssistant, "Assistant", CommandCategory.View)
                { DefaultGesture = "Ctrl+Shift+A" },

            new(NavigateGoToDefinition, "Go to Definition", CommandCategory.Navigate)
                { DefaultGesture = "F12" },
            new(NavigateFindReferences, "Find References", CommandCategory.Navigate)
                { DefaultGesture = "Shift+F12" },
            new(NavigateGoToFile, "Go to File…", CommandCategory.Navigate)
                { DefaultGesture = code ? "Ctrl+P" : "Ctrl+Shift+O" },
            new(NavigateGoToSymbol, "Go to Symbol…", CommandCategory.Navigate)
                { DefaultGesture = "Ctrl+T" },
            new(NavigateGoToLine, "Go to Line…", CommandCategory.Navigate)
                { DefaultGesture = "Ctrl+G" },
            new(NavigateBack, "Navigate Back", CommandCategory.Navigate)
                { DefaultGesture = "Ctrl+-" },
            new(NavigateForward, "Navigate Forward", CommandCategory.Navigate)
                { DefaultGesture = "Ctrl+Shift+-" },

            new(RefactorRename, "Rename…", CommandCategory.Refactor)
                { DefaultGesture = "F2" },
            new(RefactorQuickActions, "Quick Actions…", CommandCategory.Refactor)
                { DefaultGesture = "Ctrl+." },
            new(RefactorExtractMethod, "Extract Method…", CommandCategory.Refactor)
                { DefaultGesture = "Ctrl+Alt+M" },
            new(RefactorExtractVariable, "Extract Variable…", CommandCategory.Refactor)
                { DefaultGesture = "Ctrl+Alt+V" },
            new(RefactorAddImport, "Add Imports for the Name Under the Caret",
                CommandCategory.Refactor),
            new(RefactorTidyImports, "Sort and Remove Imports", CommandCategory.Refactor)
                { DefaultGesture = "Ctrl+Alt+O" },
            new(RefactorExtractConstant, "Extract Constant…", CommandCategory.Refactor),
            new(RefactorInlineVariable, "Inline Variable", CommandCategory.Refactor)
                { DefaultGesture = "Ctrl+Alt+N" },
            new(RefactorConvertConditional, "Convert If / Select Case",
                CommandCategory.Refactor),
            new(RefactorGenerateConstructor, "Generate Constructor…", CommandCategory.Refactor),
            new(RefactorGenerateProperty, "Generate Property…", CommandCategory.Refactor),
            new(RefactorMoveTypeToFile, "Move Type to File…", CommandCategory.Refactor),
            new(RefactorChangeSignature, "Change Signature…", CommandCategory.Refactor),

            new(BuildSolution, "Build Solution", CommandCategory.Build)
                { DefaultGesture = visualStudio ? "Ctrl+Shift+B" : "F6" },
            new(BuildRebuild, "Rebuild Solution", CommandCategory.Build),
            new(BuildClean, "Clean Solution", CommandCategory.Build),

            new(DebugStart, "Start Debugging", CommandCategory.Debug)
                { DefaultGesture = "F5" },
            new(DebugStartWithout, "Start Without Debugging", CommandCategory.Debug)
                { DefaultGesture = "Ctrl+F5" },
            // F5 both starts and continues, as in Visual Basic. Continue is
            // listed in its own right so it can be found by name, bound to
            // something else, and put on the toolbar.
            new(DebugContinue, "Continue", CommandCategory.Debug),
            // No default gesture: Visual Studio uses Ctrl+Alt+Break, and Break
            // is not a key the recorder can capture, so binding it would leave
            // a shortcut nobody could change. The menu and the command palette
            // reach it, and the user can bind a key that does record.
            new(DebugPause, "Break All", CommandCategory.Debug),
            new(DebugStop, "Stop Debugging", CommandCategory.Debug)
                { DefaultGesture = "Shift+F5" },
            new(DebugStepOver, "Step Over", CommandCategory.Debug)
                { DefaultGesture = "F10" },
            new(DebugStepInto, "Step Into", CommandCategory.Debug)
                { DefaultGesture = "F11" },
            new(DebugStepOut, "Step Out", CommandCategory.Debug)
                { DefaultGesture = "Shift+F11" },
            new(DebugToggleBreakpoint, "Toggle Breakpoint", CommandCategory.Debug)
                { DefaultGesture = "F9" },
            new(DebugRunToCursor, "Run to Cursor", CommandCategory.Debug)
                { DefaultGesture = "Ctrl+F10" },

            new(TestRunAll, "Run All Tests", CommandCategory.Test)
                { DefaultGesture = "Ctrl+Alt+T" },
            new(TestRunSelected, "Run Selected Tests", CommandCategory.Test),
            new(TestRunFailed, "Run Failed Tests", CommandCategory.Test),
            new(TestDebugSelected, "Debug Selected Test", CommandCategory.Test),

            new(GitInitialize, "Start a Repository Here…", CommandCategory.Git),
            new(GitCommit, "Commit…", CommandCategory.Git),
            new(GitPull, "Pull", CommandCategory.Git),
            new(GitPush, "Push", CommandCategory.Git),
            new(GitBranches, "Branches", CommandCategory.Git),

            new(DesignerAlignLeft, "Align Left", CommandCategory.Designer),
            new(DesignerAlignRight, "Align Right", CommandCategory.Designer),
            new(DesignerAlignTop, "Align Top", CommandCategory.Designer),
            new(DesignerAlignBottom, "Align Bottom", CommandCategory.Designer),
            new(DesignerAlignHorizontalCentre, "Centre Horizontally", CommandCategory.Designer),
            new(DesignerAlignVerticalCentre, "Centre Vertically", CommandCategory.Designer),
            new(DesignerSameWidth, "Same Width", CommandCategory.Designer),
            new(DesignerSameHeight, "Same Height", CommandCategory.Designer),

            // No default keys for the two zooms: navigating back and forward
            // already hold Ctrl+- and Ctrl+Shift+-, and moving through the
            // code is the older claim. Ctrl and the wheel does it on the
            // surface, the menu names it, and anyone who wants a key can
            // bind one — better than shadowing a command that was here
            // first.
            new(DesignerZoomIn, "Zoom In", CommandCategory.Designer),
            new(DesignerZoomOut, "Zoom Out", CommandCategory.Designer),
            new(DesignerZoomReset, "Actual Size", CommandCategory.Designer)
                { DefaultGesture = "Ctrl+0" },
            new(DesignerZoomToFit, "Fit to Window", CommandCategory.Designer)
                { DefaultGesture = "Ctrl+9" },
            new(DesignerSelectParent, "Select Container", CommandCategory.Designer)
                { DefaultGesture = "Escape" },

            new(ToolsSettings, "Settings…", CommandCategory.Tools)
                { DefaultGesture = "Ctrl+," },

            // Razor's own tooling exposes this, and it is what makes a
            // mapping bug diagnosable instead of mysterious.
            new(ViewGeneratedCode, "Show Generated Code", CommandCategory.View)
                { DefaultGesture = "Ctrl+Alt+G" }
        ];
    }
}
