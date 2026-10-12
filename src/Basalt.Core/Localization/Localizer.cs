using System.Globalization;
using System.Resources;

namespace Basalt.Core.Localization;

/// <summary>
/// Access point for all user-visible text.
///
/// Strings live in Strings.resx; translations go into sibling files named
/// Strings.&lt;culture&gt;.resx (for example Strings.it.resx). No new code is
/// needed to add a language: .NET picks the satellite assembly that matches
/// <see cref="Culture"/> and falls back to English when a key is missing.
/// </summary>
public static class Localizer
{
    private static readonly ResourceManager Resources =
        new("Basalt.Core.Localization.Strings", typeof(Localizer).Assembly);

    private static CultureInfo _culture = CultureInfo.CurrentUICulture;

    /// <summary>
    /// The interface languages on offer.
    ///
    /// Only English is translated so far; the list is here so the setting has
    /// something to show and so adding a translation is a resource file rather
    /// than a code change.
    /// </summary>
    public static IReadOnlyList<string> AvailableLanguages { get; } = ["en"];

    /// <summary>
    /// Culture used to resolve strings. Changing it raises
    /// <see cref="CultureChanged"/> so open views can refresh their text.
    /// </summary>
    public static CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (Equals(_culture, value)) return;
            _culture = value;
            CultureChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static event EventHandler? CultureChanged;

    /// <summary>
    /// Cultures that ship with the application, English first.
    /// A culture appears here only once its satellite assembly exists.
    /// </summary>
    public static IReadOnlyList<CultureInfo> AvailableCultures { get; } = BuildAvailableCultures();

    /// <summary>
    /// The language the IDE is actually displayed in: the current culture when
    /// a translation of it ships, English otherwise.
    ///
    /// <see cref="Culture"/> starts as the operating system's language, so on
    /// an Italian machine it says "it" while every string still resolves to
    /// English. Anything that has to match what the user reads (the language
    /// of the tools the IDE launches) asks this rather than the raw culture.
    /// </summary>
    public static string InterfaceLanguage
    {
        get
        {
            var match = AvailableCultures.FirstOrDefault(c =>
                c.TwoLetterISOLanguageName == _culture.TwoLetterISOLanguageName);

            return (match ?? AvailableCultures[0]).Name;
        }
    }

    /// <summary>Looks up a string by key.</summary>
    public static string Get(string key) =>
        Resources.GetString(key, _culture) ?? key;

    /// <summary>Looks up a string and fills in its placeholders.</summary>
    public static string Get(string key, params object?[] arguments) =>
        string.Format(_culture, Get(key), arguments);

    private static List<CultureInfo> BuildAvailableCultures()
    {
        var english = new CultureInfo("en");
        var cultures = new List<CultureInfo> { english };

        // Satellite assemblies live in per-culture subdirectories next to the
        // executable; their presence is what makes a language selectable.
        var baseDirectory = AppContext.BaseDirectory;
        var assemblyName = typeof(Localizer).Assembly.GetName().Name;

        foreach (var directory in Directory.EnumerateDirectories(baseDirectory))
        {
            var name = Path.GetFileName(directory);
            if (!File.Exists(Path.Combine(directory, $"{assemblyName}.resources.dll"))) continue;

            try
            {
                var culture = new CultureInfo(name);
                if (!cultures.Any(c => c.Name == culture.Name)) cultures.Add(culture);
            }
            catch (CultureNotFoundException)
            {
                // A directory that is not a culture name: not a translation.
            }
        }

        return cultures;
    }
}

/// <summary>
/// Keys of the localized strings.
///
/// Constants rather than raw literals so a renamed or removed key breaks the
/// build instead of silently showing the key at runtime.
/// </summary>
public static class StringKeys
{
    public const string MenuFile = "Menu_File";
    public const string MenuFileNewSolution = "Menu_File_NewSolution";
    public const string MenuFileOpenSolution = "Menu_File_OpenSolution";
    public const string MenuFileSave = "Menu_File_Save";
    public const string MenuFileExit = "Menu_File_Exit";

    public const string MenuEdit = "Menu_Edit";
    public const string MenuEditUndo = "Menu_Edit_Undo";
    public const string MenuEditRedo = "Menu_Edit_Redo";
    public const string MenuEditFormatDocument = "Menu_Edit_FormatDocument";
    public const string MenuEditFind = "Menu_Edit_Find";
    public const string MenuEditReplace = "Menu_Edit_Replace";

    public const string MenuView = "Menu_View";
    public const string MenuNavigate = "Menu_Navigate";
    public const string MenuNavigateGoToDefinition = "Menu_Navigate_GoToDefinition";
    public const string MenuNavigateFindReferences = "Menu_Navigate_FindReferences";
    public const string MenuNavigateGoToFileSymbol = "Menu_Navigate_GoToFileSymbol";
    public const string MenuNavigateGoToSolutionSymbol = "Menu_Navigate_GoToSolutionSymbol";
    public const string MenuNavigateGoToFile = "Menu_Navigate_GoToFile";
    public const string GoToFilePlaceholder = "GoToFile_Placeholder";
    public const string GoToSymbolPlaceholder = "GoToSymbol_Placeholder";
    public const string GoToSymbolShown = "GoToSymbol_Shown";
    public const string GoToSymbolShownOf = "GoToSymbol_ShownOf";
    public const string GoToFileShown = "GoToFile_Shown";
    public const string GoToFileShownOf = "GoToFile_ShownOf";
    public const string CommandPaletteTitle = "CommandPalette_Title";
    public const string CommandPalettePlaceholder = "CommandPalette_Placeholder";
    public const string GoToLineTitle = "GoToLine_Title";
    public const string GoToLinePrompt = "GoToLine_Prompt";
    public const string MenuNavigateBack = "Menu_Navigate_Back";
    public const string MenuNavigateForward = "Menu_Navigate_Forward";
    public const string MenuViewResetLayout = "Menu_View_ResetLayout";
    public const string MenuViewNewTerminal = "Menu_View_NewTerminal";

    public const string MenuProject = "Menu_Project";
    public const string MenuProjectAddWindow = "Menu_Project_AddWindow";
    public const string MenuProjectReferences = "Menu_Project_References";
    public const string MenuProjectProperties = "Menu_Project_Properties";
    public const string MenuProjectAddItem = "Menu_Project_AddItem";

    public const string ReferencesTitle = "References_Title";
    public const string ReferencesTabProjects = "References_Tab_Projects";
    public const string ReferencesTabAssemblies = "References_Tab_Assemblies";
    public const string ReferencesTabPackages = "References_Tab_Packages";
    public const string ReferencesNoOtherProjects = "References_NoOtherProjects";
    public const string ReferencesBrowse = "References_Browse";
    public const string ReferencesBrowseTitle = "References_BrowseTitle";
    public const string ReferencesAdd = "References_Add";
    public const string ReferencesRemove = "References_Remove";
    public const string ReferencesPackageId = "References_PackageId";
    public const string ReferencesPackageVersion = "References_PackageVersion";
    public const string ReferencesPackageManagedCentrally = "References_PackageManagedCentrally";
    public const string ExplorerAddReference = "Explorer_AddReference";

    public const string MenuBuild = "Menu_Build";
    public const string MenuBuildBuildSolution = "Menu_Build_BuildSolution";
    public const string MenuRun = "Menu_Run";
    public const string MenuRunStart = "Menu_Run_Start";
    public const string MenuRunStop = "Menu_Run_Stop";

    public const string ToolSolutionExplorer = "Tool_SolutionExplorer";

    public const string StatusNothingToDescribe = "Status_NothingToDescribe";
    public const string StatusNothingToSave = "Status_NothingToSave";
    public const string StatusSavedMany = "Status_SavedMany";
    public const string StatusCleaned = "Status_Cleaned";
    public const string StatusCleanFailed = "Status_CleanFailed";

    public const string MenuGitInitialize = "Menu_Git_Initialize";
    public const string StatusRepositoryCreated = "Status_RepositoryCreated";
    public const string StatusRepositoryExists = "Status_RepositoryExists";

    public const string MenuFileRecentSolutions = "Menu_File_RecentSolutions";
    public const string MenuFileRecentFiles = "Menu_File_RecentFiles";
    public const string MenuFileRecentNone = "Menu_File_RecentNone";
    public const string MenuFileRecentClear = "Menu_File_RecentClear";
    public const string WelcomeRecent = "Welcome_Recent";
    public const string WelcomeRecentEmpty = "Welcome_Recent_Empty";
    public const string WelcomeGetStarted = "Welcome_GetStarted";
    public const string WelcomeNewProject = "Welcome_NewProject";
    public const string WelcomeNewProjectDescription = "Welcome_NewProject_Description";
    public const string WelcomeOpenProject = "Welcome_OpenProject";
    public const string WelcomeOpenProjectDescription = "Welcome_OpenProject_Description";
    public const string WelcomeRecentMissing = "Welcome_Recent_Missing";

    public const string ExplorerShowAllFiles = "Explorer_ShowAllFiles";
    public const string ExplorerCollapseAll = "Explorer_CollapseAll";
    public const string ExplorerSyncWithEditor = "Explorer_SyncWithEditor";
    public const string ExplorerRefresh = "Explorer_Refresh";
    public const string WinFormsDesignerDesignTabSuffix = "WinFormsDesigner_DesignTabSuffix";
    public const string WinFormsDesignerViewCode = "WinFormsDesigner_ViewCode";
    public const string ToolToolbox = "Tool_Toolbox";
    public const string ToolProperties = "Tool_Properties";
    public const string ToolProblems = "Tool_Problems";
    public const string ToolOutput = "Tool_Output";
    public const string ToolSearch = "Tool_Search";
    public const string ToolOutline = "Tool_Outline";
    public const string ToolElementTree = "Tool_ElementTree";
    public const string DesignerEmptyHint = "Designer_EmptyHint";
    public const string MenuDesignerRulers = "Menu_Designer_Rulers";
    public const string EventsNothingSelected = "Events_NothingSelected";
    public const string ToolEvents = "Tool_Events";
    public const string DatabaseOpen = "Database_Open";
    public const string DatabaseRun = "Database_Run";
    public const string DatabaseStop = "Database_Stop";
    public const string DatabaseStopped = "Database_Stopped";
    public const string DatabaseNothingOpen = "Database_NothingOpen";
    public const string DatabaseOpened = "Database_Opened";
    public const string DatabaseRows = "Database_Rows";
    public const string DatabaseCapped = "Database_Capped";
    public const string DatabaseAffected = "Database_Affected";
    public const string DatabaseRefresh = "Database_Refresh";
    public const string DatabaseDisconnect = "Database_Disconnect";
    public const string DatabaseDisconnected = "Database_Disconnected";
    public const string DatabaseNoFile = "Database_NoFile";
    public const string DatabaseNotOpen = "Database_NotOpen";
    public const string ToolDatabase = "Tool_Database";
    public const string GitNothingToCommit = "Git_NothingToCommit";
    public const string GitNothingStaged = "Git_NothingStaged";
    public const string GitNoRepository = "Git_NoRepository";
    public const string GitCannotStart = "Git_CannotStart";
    public const string DesignerSurfaceSize = "Designer_SurfaceSize";
    public const string DesignerCut = "Designer_Cut";
    public const string DesignerCopy = "Designer_Copy";
    public const string DesignerPaste = "Designer_Paste";
    public const string DesignerDelete = "Designer_Delete";
    public const string DesignerSelectContainer = "Designer_SelectContainer";
    public const string DesignerBringToFront = "Designer_BringToFront";
    public const string DesignerSendToBack = "Designer_SendToBack";
    public const string DesignerAlignLeftItem = "Designer_AlignLeft";
    public const string DesignerAlignRightItem = "Designer_AlignRight";
    public const string DesignerAlignTopItem = "Designer_AlignTop";
    public const string DesignerAlignBottomItem = "Designer_AlignBottom";
    public const string DesignerSameWidthItem = "Designer_SameWidth";
    public const string DesignerSameHeightItem = "Designer_SameHeight";
    public const string DesignerFitToWindow = "Designer_FitToWindow";
    public const string DesignerActualSize = "Designer_ActualSize";
    public const string DesignerRows = "Designer_Rows";
    public const string DesignerColumns = "Designer_Columns";
    public const string DesignerAddRow = "Designer_AddRow";
    public const string DesignerAddColumn = "Designer_AddColumn";
    public const string DesignerContainingGrid = "Designer_ContainingGrid";

    public const string MenuDesigner = "Menu_Designer";
    public const string MenuDesignerAlignLeft = "Menu_Designer_AlignLeft";
    public const string MenuDesignerAlignRight = "Menu_Designer_AlignRight";
    public const string MenuDesignerAlignTop = "Menu_Designer_AlignTop";
    public const string MenuDesignerAlignBottom = "Menu_Designer_AlignBottom";
    public const string MenuDesignerSameWidth = "Menu_Designer_SameWidth";
    public const string MenuDesignerSameHeight = "Menu_Designer_SameHeight";
    public const string MenuDesignerZoomIn = "Menu_Designer_ZoomIn";
    public const string MenuDesignerZoomOut = "Menu_Designer_ZoomOut";
    public const string MenuDesignerZoomReset = "Menu_Designer_ZoomReset";
    public const string MenuDesignerZoomToFit = "Menu_Designer_ZoomToFit";
    public const string MenuDesignerSelectParent = "Menu_Designer_SelectParent";
    public const string ToolReferences = "Tool_References";
    public const string MenuGit = "Menu_Git";
    public const string MenuGitCommit = "Menu_Git_Commit";
    public const string MenuGitFetch = "Menu_Git_Fetch";
    public const string MenuGitPull = "Menu_Git_Pull";
    public const string MenuGitPush = "Menu_Git_Push";
    public const string MenuGitBranches = "Menu_Git_Branches";
    public const string MenuGitHistory = "Menu_Git_History";
    public const string MenuWindow = "Menu_Window";
    public const string MenuWindowMinimize = "Menu_Window_Minimize";
    public const string MenuWindowZoom = "Menu_Window_Zoom";
    public const string MenuHelp = "Menu_Help";
    public const string MenuHelpAbout = "Menu_Help_About";

    public const string SettingsTitle = "Settings_Title";
    public const string SettingsEditor = "Settings_Editor";
    public const string SettingsAppearance = "Settings_Appearance";
    public const string SettingsTerminal = "Settings_Terminal";
    public const string SettingsDebug = "Settings_Debug";
    public const string SettingsSaved = "Settings_Saved";
    public const string SettingsSaveFailed = "Settings_SaveFailed";
    public const string DialogClose = "Dialog_Close";
    public const string MenuToolsSettings = "Menu_Tools_Settings";
    public const string MenuTools = "Menu_Tools";

    public const string MenuDebug = "Menu_Debug";
    public const string MenuDebugStart = "Menu_Debug_Start";
    public const string MenuDebugContinue = "Menu_Debug_Continue";
    public const string MenuDebugPause = "Menu_Debug_Pause";
    public const string MenuDebugStop = "Menu_Debug_Stop";
    public const string MenuDebugStepOver = "Menu_Debug_StepOver";
    public const string MenuDebugStepInto = "Menu_Debug_StepInto";
    public const string MenuDebugStepOut = "Menu_Debug_StepOut";
    public const string MenuDebugToggleBreakpoint = "Menu_Debug_ToggleBreakpoint";
    public const string MenuRunWithoutDebugging = "Menu_Run_WithoutDebugging";

    public const string ToolAssistant = "Tool_Assistant";
    public const string SettingsAssistant = "Settings_Assistant";
    public const string MenuToolsAssistant = "Menu_Tools_Assistant";

    public const string ToolTests = "Tool_Tests";
    public const string MenuTestRunAll = "Menu_Test_RunAll";
    public const string MenuTest = "Menu_Test";

    public const string ToolCallStack = "Tool_CallStack";
    public const string ToolVariables = "Tool_Variables";
    public const string ToolBreakpoints = "Tool_Breakpoints";
    public const string ToolGitBranches = "Tool_GitBranches";
    public const string ToolGitDiff = "Tool_GitDiff";
    public const string ToolGitChanges = "Tool_GitChanges";
    public const string ToolGitHistory = "Tool_GitHistory";
    public const string ToolTerminal = "Tool_Terminal";
    public const string ToolTerminalNumbered = "Tool_TerminalNumbered";

    public const string ColumnSeverity = "Column_Severity";
    public const string ColumnCode = "Column_Code";
    public const string ColumnDescription = "Column_Description";
    public const string ColumnFile = "Column_File";
    public const string ColumnLine = "Column_Line";

    public const string SeverityError = "Severity_Error";
    public const string SeverityWarning = "Severity_Warning";
    public const string SeverityInfo = "Severity_Info";
    public const string SeverityHidden = "Severity_Hidden";

    public const string WelcomeSubtitle = "Welcome_Subtitle";

    public const string NewSolutionTitle = "NewSolution_Title";
    public const string NewSolutionSubtitle = "NewSolution_Subtitle";
    public const string NewSolutionLanguage = "NewSolution_Language";
    public const string NewSolutionProjectType = "NewSolution_ProjectType";
    public const string NewSolutionName = "NewSolution_Name";
    public const string NewSolutionLocation = "NewSolution_Location";
    public const string NewSolutionBrowse = "NewSolution_Browse";
    public const string NewSolutionPreview = "NewSolution_Preview";

    public const string TemplateAvaloniaApp = "Template_AvaloniaApp";
    public const string TemplateAvaloniaAppDescription = "Template_AvaloniaApp_Description";
    public const string TemplateConsoleApp = "Template_ConsoleApp";
    public const string TemplateConsoleAppDescription = "Template_ConsoleApp_Description";
    public const string TemplateClassLibrary = "Template_ClassLibrary";
    public const string TemplateClassLibraryDescription = "Template_ClassLibrary_Description";
    public const string TemplateWebApi = "Template_WebApi";
    public const string TemplateWebApiDescription = "Template_WebApi_Description";
    public const string TemplateRazorPages = "Template_RazorPages";
    public const string TemplateRazorPagesDescription = "Template_RazorPages_Description";
    public const string TemplateMvc = "Template_Mvc";
    public const string TemplateMvcDescription = "Template_Mvc_Description";
    public const string TemplateBlazor = "Template_Blazor";
    public const string TemplateBlazorDescription = "Template_Blazor_Description";
    public const string TemplateWinFormsApp = "Template_WinFormsApp";
    public const string TemplateWinFormsAppDescription = "Template_WinFormsApp_Description";
    public const string TemplateTestProject = "Template_TestProject";
    public const string TemplateTestProjectDescription = "Template_TestProject_Description";
    public const string TemplateWebApp = "Template_WebApp";
    public const string TemplateWebAppDescription = "Template_WebApp_Description";
    public const string TemplateVisualBasic6 = "Template_VisualBasic6";
    public const string TemplateVisualBasic6Description = "Template_VisualBasic6_Description";
    public const string TemplateQuickBasic = "Template_QuickBasic";
    public const string TemplateQuickBasicDescription = "Template_QuickBasic_Description";

    // The New Solution window's tree: three dialects, Visual Basic .NET split
    // into categories, each leaf naming the one project template it creates.
    public const string NewSolutionDialectVisualBasicNet = "NewSolution_Dialect_VisualBasicNet";
    public const string NewSolutionDialectVisualBasic6 = "NewSolution_Dialect_VisualBasic6";
    public const string NewSolutionDialectQuickBasic = "NewSolution_Dialect_QuickBasic";
    public const string NewSolutionCategoryWindowsDesktop = "NewSolution_Category_WindowsDesktop";
    public const string NewSolutionCategoryWeb = "NewSolution_Category_Web";

    // One line naming the framework or platform a template targets, shown
    // above the form once its leaf is selected.
    public const string NewSolutionTargetWinFormsApp = "NewSolution_Target_WinFormsApp";
    public const string NewSolutionTargetAvaloniaApp = "NewSolution_Target_AvaloniaApp";
    public const string NewSolutionTargetConsoleApp = "NewSolution_Target_ConsoleApp";
    public const string NewSolutionTargetClassLibrary = "NewSolution_Target_ClassLibrary";
    public const string NewSolutionTargetTestProject = "NewSolution_Target_TestProject";
    public const string NewSolutionTargetWebApi = "NewSolution_Target_WebApi";
    public const string NewSolutionTargetWebApp = "NewSolution_Target_WebApp";
    public const string NewSolutionTargetRazorPages = "NewSolution_Target_RazorPages";
    public const string NewSolutionTargetMvc = "NewSolution_Target_Mvc";
    public const string NewSolutionTargetBlazor = "NewSolution_Target_Blazor";
    public const string NewSolutionTargetVisualBasic6 = "NewSolution_Target_VisualBasic6";
    public const string NewSolutionTargetQuickBasic = "NewSolution_Target_QuickBasic";

    public const string NewSolutionSolutionName = "NewSolution_SolutionName";
    public const string NewSolutionPlaceInSameDirectory = "NewSolution_PlaceInSameDirectory";
    public const string NewSolutionFolderNotEmpty = "NewSolution_FolderNotEmpty";

    public const string NewWindowTitle = "NewWindow_Title";
    public const string NewWindowClassName = "NewWindow_ClassName";
    public const string NewWindowNamespace = "NewWindow_Namespace";
    public const string NewWindowWindowTitle = "NewWindow_WindowTitle";
    public const string NewWindowCodeBehindVb = "NewWindow_CodeBehindVb";
    public const string NewWindowCodeBehindCs = "NewWindow_CodeBehindCs";

    public const string ButtonCreate = "Button_Create";
    public const string ButtonCancel = "Button_Cancel";
    public const string ButtonOk = "Button_Ok";
    public const string ButtonYes = "Button_Yes";
    public const string ButtonNo = "Button_No";

    public const string TerminalInputHint = "Terminal_InputHint";
    public const string TerminalCouldNotStart = "Terminal_CouldNotStart";

    public const string StatusReady = "Status_Ready";
    public const string StatusOpening = "Status_Opening";
    public const string StatusOpened = "Status_Opened";
    public const string StatusOpenFailed = "Status_OpenFailed";
    public const string StatusSaved = "Status_Saved";
    public const string StatusBuilding = "Status_Building";
    public const string StatusBuildSucceeded = "Status_BuildSucceeded";
    public const string StatusBuildFailed = "Status_BuildFailed";
    public const string StatusBuildInterrupted = "Status_BuildInterrupted";
    public const string StatusRunning = "Status_Running";
    public const string StatusStopped = "Status_Stopped";
    public const string StatusStartFailed = "Status_StartFailed";
    public const string StatusNoProject = "Status_NoProject";
    public const string StatusOpenProjectFirst = "Status_OpenProjectFirst";
    public const string StatusWindowCreated = "Status_WindowCreated";
    public const string StatusFormatted = "Status_Formatted";
    public const string StatusPropertiesSaved = "Status_PropertiesSaved";
    public const string StatusNoDefinition = "Status_NoDefinition";
    public const string StatusNoSymbolHere = "Status_NoSymbolHere";
    public const string StatusApplicationExited = "Status_ApplicationExited";
    public const string StatusRunExited = "Status_RunExited";

    public const string ValidationNameRequired = "Validation_NameRequired";
    public const string ValidationInvalidIdentifier = "Validation_InvalidIdentifier";
    public const string ValidationLocationRequired = "Validation_LocationRequired";

    public const string ProblemsEmpty = "Problems_Empty";
    public const string OutputEmpty = "Output_Empty";
    public const string DiffNoChanges = "Diff_NoChanges";
    public const string OutlineEmpty = "Outline_Empty";
    public const string CallStackEmpty = "CallStack_Empty";
    public const string VariablesEmpty = "Variables_Empty";
    public const string BreakpointsEmpty = "Breakpoints_Empty";

    public const string PropertiesNoSelection = "Properties_NoSelection";
    public const string PropertiesUnknownType = "Properties_UnknownType";
    public const string PropertiesCategoryCommon = "Properties_Category_Common";
    public const string PropertiesCategoryLayout = "Properties_Category_Layout";
    public const string PropertiesCategoryAppearance = "Properties_Category_Appearance";
    public const string PropertiesCategoryBehavior = "Properties_Category_Behavior";
    public const string PropertiesCategoryDesign = "Properties_Category_Design";
    public const string ToolboxHint = "Toolbox_Hint";
    public const string DesignerCannotRender = "Designer_CannotRender";
    public const string PropertiesSearch = "Properties_Search";
    public const string PropertiesNoMatch = "Properties_NoMatch";
    public const string PropertiesReset = "Properties_Reset";
    public const string PropertiesSortByCategory = "Properties_SortByCategory";

    public const string PickerOpenSolution = "Picker_OpenSolution";
    public const string PickerSolutionFilter = "Picker_SolutionFilter";
    public const string PickerChooseFolder = "Picker_ChooseFolder";

    public const string WinFormsDesignerFormCodeNotOpened = "WinFormsDesigner_FormCodeNotOpened";

    // Run and debug status messages that used to be passed to Localizer.Get
    // as string literals, bypassing the compile-time check this class gives
    // every other message.
    public const string StatusBuildStopped = "StatusBuildStopped";
    public const string DebugRunWithoutAdapter = "DebugRunWithoutAdapter";
    public const string StatusLaunchTargetChanged = "StatusLaunchTargetChanged";
    public const string StatusAlreadyRunning = "Status_AlreadyRunning";
    public const string WinFormsCollectionColumnAdditionalInitialization = "WinFormsCollection_ColumnAdditionalInitialization";
    public const string WinFormsCollectionColumnAdditionalReferences = "WinFormsCollection_ColumnAdditionalReferences";
    public const string WinFormsCollectionColumnHeaderSingleLine = "WinFormsCollection_ColumnHeaderSingleLine";
    public const string WinFormsCollectionColumnNameAlreadyUsed = "WinFormsCollection_ColumnNameAlreadyUsed";
    public const string WinFormsCollectionColumnNamesMustBeUniqueIdentifiers = "WinFormsCollection_ColumnNamesMustBeUniqueIdentifiers";
    public const string WinFormsCollectionColumnReferencedOutsideInitialize = "WinFormsCollection_ColumnReferencedOutsideInitialize";
    public const string WinFormsCollectionColumnTypeUnsupported = "WinFormsCollection_ColumnTypeUnsupported";
    public const string WinFormsCollectionItemTextSingleLine = "WinFormsCollection_ItemTextSingleLine";
    public const string WinFormsCollectionOriginalColumnNotFound = "WinFormsCollection_OriginalColumnNotFound";
    public const string WinFormsCollectionSplitSharedFieldChangeType = "WinFormsCollection_SplitSharedFieldChangeType";
    public const string WinFormsCollectionSplitSharedFieldRemove = "WinFormsCollection_SplitSharedFieldRemove";
    public const string WinFormsDesignerComponentNotFound = "WinFormsDesigner_ComponentNotFound";
    public const string WinFormsDesignerComponentTypeInvalid = "WinFormsDesigner_ComponentTypeInvalid";
    public const string WinFormsDesignerControlNotFound = "WinFormsDesigner_ControlNotFound";
    public const string WinFormsDesignerControlTypeInvalid = "WinFormsDesigner_ControlTypeInvalid";
    public const string WinFormsDesignerFieldNameAlreadyExists = "WinFormsDesigner_FieldNameAlreadyExists";
    public const string WinFormsDesignerHierarchyCycle = "WinFormsDesigner_HierarchyCycle";
    public const string WinFormsDesignerIdentifierRequired = "WinFormsDesigner_IdentifierRequired";
    public const string WinFormsDesignerInvalidExpression = "WinFormsDesigner_InvalidExpression";
    public const string WinFormsDesignerNameAlreadyExists = "WinFormsDesigner_NameAlreadyExists";
    public const string WinFormsDesignerNoInitializeComponent = "WinFormsDesigner_NoInitializeComponent";
    public const string WinFormsDesignerRenameAlreadyUsed = "WinFormsDesigner_RenameAlreadyUsed";
    public const string WinFormsDesignerRenameMustBeIdentifier = "WinFormsDesigner_RenameMustBeIdentifier";
    public const string WinFormsDesignerRootCannotBeCopied = "WinFormsDesigner_RootCannotBeCopied";
    public const string WinFormsDesignerRootCannotBeDeleted = "WinFormsDesigner_RootCannotBeDeleted";
    public const string WinFormsDesignerSelectAtLeastOneControl = "WinFormsDesigner_SelectAtLeastOneControl";
    public const string WinFormsDesignerSplitSharedFieldDeleteComponent = "WinFormsDesigner_SplitSharedFieldDeleteComponent";
    public const string WinFormsDesignerSplitSharedFieldDeleteControl = "WinFormsDesigner_SplitSharedFieldDeleteControl";
    public const string WinFormsDesignerSyntaxErrors = "WinFormsDesigner_SyntaxErrors";
    public const string WinFormsDesignerTargetFieldMissing = "WinFormsDesigner_TargetFieldMissing";
    public const string WinFormsDesignerTargetMethodMissing = "WinFormsDesigner_TargetMethodMissing";
    public const string WinFormsDesignerUnsupportedInitializationStatement = "WinFormsDesigner_UnsupportedInitializationStatement";
    public const string WinFormsDesignerZOrderMustSharedParent = "WinFormsDesigner_ZOrderMustSharedParent";
    public const string WinFormsDesignerZOrderUnsupported = "WinFormsDesigner_ZOrderUnsupported";
    public const string WinFormsMenuConstantInitializationOnly = "WinFormsMenu_ConstantInitializationOnly";
    public const string WinFormsMenuCustomItemType = "WinFormsMenu_CustomItemType";
    public const string WinFormsMenuDynamicText = "WinFormsMenu_DynamicText";
    public const string WinFormsMenuInvalidHierarchy = "WinFormsMenu_InvalidHierarchy";
    public const string WinFormsMenuItemCannotChangeType = "WinFormsMenu_ItemCannotChangeType";
    public const string WinFormsMenuItemFieldReference = "WinFormsMenu_ItemFieldReference";
    public const string WinFormsMenuItemNotInMenu = "WinFormsMenu_ItemNotInMenu";
    public const string WinFormsMenuItemsSharedOrCyclic = "WinFormsMenu_ItemsSharedOrCyclic";
    public const string WinFormsMenuNameAlreadyDeclared = "WinFormsMenu_NameAlreadyDeclared";
    public const string WinFormsMenuNamesMustBeUniqueIdentifiers = "WinFormsMenu_NamesMustBeUniqueIdentifiers";
    public const string WinFormsMenuOneConstantArgument = "WinFormsMenu_OneConstantArgument";
    public const string WinFormsMenuPropertyMustBeIdentifier = "WinFormsMenu_PropertyMustBeIdentifier";
    public const string WinFormsMenuSplitSharedFieldDeleteItem = "WinFormsMenu_SplitSharedFieldDeleteItem";
    public const string WinFormsMenuTextMustFitOnOneLine = "WinFormsMenu_TextMustFitOnOneLine";
    public const string WinFormsMenuUnknownParent = "WinFormsMenu_UnknownParent";
    public const string WinFormsToolStripConstantInitializationOnly = "WinFormsToolStrip_ConstantInitializationOnly";
    public const string WinFormsToolStripCustomItemType = "WinFormsToolStrip_CustomItemType";
    public const string WinFormsToolStripDynamicText = "WinFormsToolStrip_DynamicText";
    public const string WinFormsToolStripItemCannotChangeType = "WinFormsToolStrip_ItemCannotChangeType";
    public const string WinFormsToolStripItemFieldReference = "WinFormsToolStrip_ItemFieldReference";
    public const string WinFormsToolStripItemNotInStrip = "WinFormsToolStrip_ItemNotInStrip";
    public const string WinFormsToolStripItemsSharedOrCyclic = "WinFormsToolStrip_ItemsSharedOrCyclic";
    public const string WinFormsToolStripNameAlreadyDeclared = "WinFormsToolStrip_NameAlreadyDeclared";
    public const string WinFormsToolStripNamesMustBeUniqueIdentifiers = "WinFormsToolStrip_NamesMustBeUniqueIdentifiers";
    public const string WinFormsToolStripOneConstantArgument = "WinFormsToolStrip_OneConstantArgument";
    public const string WinFormsToolStripPropertyMustBeIdentifier = "WinFormsToolStrip_PropertyMustBeIdentifier";
    public const string WinFormsToolStripTextMustFitOnOneLine = "WinFormsToolStrip_TextMustFitOnOneLine";
    public const string WinFormsToolStripUnsupported = "WinFormsToolStrip_Unsupported";
    public const string WinFormsToolStripChooseType = "WinFormsToolStrip_ChooseType";
    public const string WinFormsToolStripButton = "WinFormsToolStrip_Button";
    public const string WinFormsToolStripLabel = "WinFormsToolStrip_Label";
    public const string WinFormsToolStripSplitButton = "WinFormsToolStrip_SplitButton";
    public const string WinFormsToolStripDropDownButton = "WinFormsToolStrip_DropDownButton";
    public const string WinFormsToolStripSeparator = "WinFormsToolStrip_Separator";
    public const string WinFormsToolStripComboBox = "WinFormsToolStrip_ComboBox";
    public const string WinFormsToolStripTextBox = "WinFormsToolStrip_TextBox";
    public const string WinFormsToolStripProgressBar = "WinFormsToolStrip_ProgressBar";
    public const string WinFormsToolStripStatusLabel = "WinFormsToolStrip_StatusLabel";
    public const string WinFormsToolStripMenuItem = "WinFormsToolStrip_MenuItem";
    public const string WinFormsDesignerEventDelegateTypeRequired = "WinFormsDesigner_EventDelegateTypeRequired";
    public const string WinFormsDesignerEventHandlerSyntaxErrors = "WinFormsDesigner_EventHandlerSyntaxErrors";
    public const string WinFormsDesignerGenericEventArgumentsUnsupported = "WinFormsDesigner_GenericEventArgumentsUnsupported";
    public const string WinFormsDesignerHandlerParameterCountMismatch = "WinFormsDesigner_HandlerParameterCountMismatch";
    public const string WinFormsDesignerPartialClassNotFound = "WinFormsDesigner_PartialClassNotFound";

    public const string EditorSmartTags = "Editor_SmartTags";
    public const string EditorSmartTagsNone = "Editor_SmartTagsNone";
    public const string EditorSmartTagsChanged = "Editor_SmartTagsChanged";
    public const string EditorSmartTagsFailed = "Editor_SmartTagsFailed";
    public const string EditorSmartTagRename = "Editor_SmartTagRename";
    public const string EditorSmartTagExtractVariable = "Editor_SmartTagExtractVariable";
    public const string EditorSmartTagExtractMethod = "Editor_SmartTagExtractMethod";
    public const string EditorSmartTagTidyImports = "Editor_SmartTagTidyImports";

    public const string ResourceEditorAdd = "ResourceEditor_Add";
    public const string ResourceEditorApply = "ResourceEditor_Apply";
    public const string ResourceEditorXml = "ResourceEditor_Xml";
    public const string ResourceEditorBack = "ResourceEditor_Back";
    public const string ResourceEditorName = "ResourceEditor_Name";
    public const string ResourceEditorType = "ResourceEditor_Type";
    public const string ResourceEditorValue = "ResourceEditor_Value";
    public const string ResourceEditorComment = "ResourceEditor_Comment";
    public const string ResourceEditorInvalid = "ResourceEditor_Invalid";
    public const string ResourceEditorReadOnly = "ResourceEditor_ReadOnly";
    public const string ResourceEditorDuplicateName = "ResourceEditor_DuplicateName";
    public const string ResourceEditorInvalidValue = "ResourceEditor_InvalidValue";
    public const string ResourceEditorUnsupportedScalar = "ResourceEditor_UnsupportedScalar";
    public const string ResourceEditorEditFailed = "ResourceEditor_EditFailed";
    public const string DesignerView = "Designer_View";
    public const string EditorSmartTagsUnsupportedChanges = "Editor_SmartTagsUnsupportedChanges";
    public const string EditorVbDeclarations = "Editor_VbDeclarations";
    public const string EditorVbFormEvents = "Editor_VbFormEvents";
    public const string EditorVbGeneral = "Editor_VbGeneral";
    public const string EditorVbMembers = "Editor_VbMembers";
    public const string EditorVbNavigationFailed = "Editor_VbNavigationFailed";
    public const string EditorVbObjects = "Editor_VbObjects";
    public const string MenuEditorNewItem = "MenuEditor_NewItem";
    public const string MenuEditorParent = "MenuEditor_Parent";
    public const string MenuEditorSeparator = "MenuEditor_Separator";
    public const string MenuEditorTitle = "MenuEditor_Title";
    public const string MenuInlineBack = "MenuInline_Back";
    public const string MenuInlineEditText = "MenuInline_EditText";
    public const string MenuInlineOpenSubmenu = "MenuInline_OpenSubmenu";
    public const string MenuInlineTypeHere = "MenuInline_TypeHere";
    public const string MenuInlineUnsupported = "MenuInline_Unsupported";
    public const string NewSolutionChooseFolder = "NewSolution_ChooseFolder";
    public const string NewSolutionInvalidName = "NewSolution_InvalidName";
    public const string NewSolutionInvalidSolutionName = "NewSolution_InvalidSolutionName";
    public const string NewSolutionLocationRequired = "NewSolution_LocationRequired";
    public const string NewSolutionNameRequired = "NewSolution_NameRequired";
    public const string NewSolutionCreateFailed = "NewSolution_CreateFailed";
    public const string PropertyGridCollection = "PropertyGrid_Collection";
    public const string PropertyGridComponentReference = "PropertyGrid_ComponentReference";
    public const string PropertyGridName = "PropertyGrid_Name";
    public const string PropertyGridNone = "PropertyGrid_None";
    public const string PropertyGridOpenEditor = "PropertyGrid_OpenEditor";
    public const string WinFormsCollectionInvalidColumn = "WinFormsCollection_InvalidColumn";
    public const string WinFormsCollectionInvalidEntry = "WinFormsCollection_InvalidEntry";
    public const string WinFormsCollectionNewItem = "WinFormsCollection_NewItem";
    public const string WinFormsCollectionUnsupported = "WinFormsCollection_Unsupported";
    public const string WinFormsColorEditorCustom = "WinFormsColorEditor_Custom";
    public const string WinFormsColorEditorHex = "WinFormsColorEditor_Hex";
    public const string WinFormsColorEditorKnown = "WinFormsColorEditor_Known";
    public const string WinFormsColorEditorPreview = "WinFormsColorEditor_Preview";
    public const string WinFormsColorEditorSystem = "WinFormsColorEditor_System";
    public const string WinFormsColorEditorTitle = "WinFormsColorEditor_Title";
    public const string WinFormsDesignerAlignSelection = "WinFormsDesigner_AlignSelection";
    public const string WinFormsDesignerCodeChanged = "WinFormsDesigner_CodeChanged";
    public const string WinFormsDesignerCodeUnavailable = "WinFormsDesigner_CodeUnavailable";
    public const string WinFormsDesignerComponents = "WinFormsDesigner_Components";
    public const string WinFormsDesignerDistributeSelection = "WinFormsDesigner_DistributeSelection";
    public const string WinFormsDesignerError = "WinFormsDesigner_Error";
    public const string WinFormsDesignerEvent = "WinFormsDesigner_Event";
    public const string WinFormsDesignerEventsHint = "WinFormsDesigner_EventsHint";
    public const string WinFormsDesignerHandler = "WinFormsDesigner_Handler";
    public const string WinFormsDesignerMixedValues = "WinFormsDesigner_MixedValues";
    public const string WinFormsDesignerObjects = "WinFormsDesigner_Objects";
    public const string WinFormsDesignerPreviewProblems = "WinFormsDesigner_PreviewProblems";
    public const string WinFormsDesignerProjectComponents = "WinFormsDesigner_ProjectComponents";
    public const string WinFormsDesignerNotDesignable = "WinFormsDesigner_NotDesignable";
    public const string WinFormsDesignerUserCodeFailed = "WinFormsDesigner_UserCodeFailed";
    public const string WinFormsDesignerPlaceholder = "WinFormsDesigner_Placeholder";
    public const string WinFormsDesignerDesignContextFailed = "WinFormsDesigner_DesignContextFailed";
    public const string WinFormsDesignerSelectedObjects = "WinFormsDesigner_SelectedObjects";
    public const string WinFormsDesignerTabOrder = "WinFormsDesigner_TabOrder";
    public const string WinFormsDesignerUnsupportedExpression = "WinFormsDesigner_UnsupportedExpression";
    public const string WinFormsDesignerLocalizableNeedsDefaultLanguage = "WinFormsDesigner_LocalizableNeedsDefaultLanguage";
    public const string WinFormsDesignerLocalizable = "WinFormsDesigner_Localizable";
    public const string WinFormsDesignerLanguage = "WinFormsDesigner_Language";
    public const string WinFormsDesignerLanguageDefault = "WinFormsDesigner_LanguageDefault";
    public const string WinFormsDesignerNotInLanguage = "WinFormsDesigner_NotInLanguage";
    public const string WinFormsDesignerResourcesUnreadable = "WinFormsDesigner_ResourcesUnreadable";
    public const string WinFormsDesignerResourcesNotReplaced = "WinFormsDesigner_ResourcesNotReplaced";
    public const string WinFormsDesignerLibraryComponents = "WinFormsDesigner_LibraryComponents";
    public const string WinFormsDesignerLibraryNotListed = "WinFormsDesigner_LibraryNotListed";
    public const string WinFormsDesignerDependencyListMissing = "WinFormsDesigner_DependencyListMissing";
    public const string WinFormsDesignerDependencyListUnreadable = "WinFormsDesigner_DependencyListUnreadable";
    public const string WinFormsDesignerPanelNotSelectable = "WinFormsDesigner_PanelNotSelectable";
    public const string WinFormsEditorCancel = "WinFormsEditor_Cancel";
    public const string WinFormsEditorInvalidColor = "WinFormsEditor_InvalidColor";
    public const string WinFormsEditorInvalidFont = "WinFormsEditor_InvalidFont";
    public const string WinFormsEditorOk = "WinFormsEditor_Ok";
    public const string WinFormsFontEditorBold = "WinFormsFontEditor_Bold";
    public const string WinFormsFontEditorFamily = "WinFormsFontEditor_Family";
    public const string WinFormsFontEditorItalic = "WinFormsFontEditor_Italic";
    public const string WinFormsFontEditorPreview = "WinFormsFontEditor_Preview";
    public const string WinFormsFontEditorSample = "WinFormsFontEditor_Sample";
    public const string WinFormsFontEditorSize = "WinFormsFontEditor_Size";
    public const string WinFormsFontEditorStrikeout = "WinFormsFontEditor_Strikeout";
    public const string WinFormsFontEditorTitle = "WinFormsFontEditor_Title";
    public const string WinFormsFontEditorUnderline = "WinFormsFontEditor_Underline";

    // Run, debug-start and editor status lines added with the F5 and smart-tag review.
    public const string EditorOnlyVbHtmlGenerated = "Editor_OnlyVbHtmlGenerated";
    public const string EditorSelectStatementsFirst = "Editor_SelectStatementsFirst";
    public const string EditorSelectExpressionFirst = "Editor_SelectExpressionFirst";
    public const string EditorChangeNotWritten = "Editor_ChangeNotWritten";
    public const string EditorSmartTagsNoDocumentArea = "Editor_SmartTagsNoDocumentArea";
    public const string StatusNoBuildOutput = "Status_NoBuildOutput";
    public const string DebugStarting = "Debug_Starting";
    public const string DebugBuilding = "Debug_Building";
    public const string DebugBannerStarting = "Debug_BannerStarting";
    public const string DebugNoProject = "Debug_NoProject";
    public const string DebugBuildFailed = "Debug_BuildFailed";
    public const string DebugNoAssembly = "Debug_NoAssembly";

    // Visual Studio Windows desktop projects retargeted onto Basalt.Forms.
    public const string BuildWindowsDesktopSourcesMissing = "Build_WindowsDesktopSourcesMissing";

    // Problems, Output and Search panels: toolbars, filters and the search literals.
    public const string ProblemsErrors = "Problems_Errors";
    public const string ProblemsWarnings = "Problems_Warnings";
    public const string ProblemsMessages = "Problems_Messages";
    public const string ProblemsErrorsCount = "Problems_ErrorsCount";
    public const string ProblemsWarningsCount = "Problems_WarningsCount";
    public const string ProblemsMessagesCount = "Problems_MessagesCount";
    public const string ProblemsFilter = "Problems_Filter";
    public const string ProblemsNoMatch = "Problems_NoMatch";
    public const string OutputClear = "Output_Clear";
    public const string OutputWordWrap = "Output_WordWrap";
    public const string OutputScrollToEnd = "Output_ScrollToEnd";
    public const string SearchFind = "Search_Find";
    public const string SearchReplaceWith = "Search_ReplaceWith";
    public const string SearchReplaceAll = "Search_ReplaceAll";
    public const string SearchFileTypes = "Search_FileTypes";
    public const string SearchMatchCase = "Search_MatchCase";
    public const string SearchWholeWord = "Search_WholeWord";
    public const string SearchUseRegex = "Search_UseRegex";
    public const string SearchSearching = "Search_Searching";
    public const string SearchOpenSolutionFirst = "Search_OpenSolutionFirst";
    public const string SearchNoResults = "Search_NoResults";
    public const string SearchProgress = "Search_Progress";
    public const string SearchResults = "Search_Results";
    public const string SearchReplaced = "Search_Replaced";
    public const string SearchEmpty = "Search_Empty";
    public const string SearchFileMatches = "Search_FileMatches";

    // Phase 2b panels: toolbox filter, panel tooltips and empty states.
    public const string ToolboxFilterWatermark = "Toolbox_FilterWatermark";
    public const string ToolboxFilterTip = "Toolbox_FilterTip";
    public const string ToolboxNoMatches = "Toolbox_NoMatches";
    public const string OutlineCollapseAll = "Outline_CollapseAll";
    public const string OutlineExpandAll = "Outline_ExpandAll";
    public const string OutlineGoTo = "Outline_GoTo";
    public const string ElementTreeEmpty = "ElementTree_Empty";
    public const string DatabaseNoTables = "Database_NoTables";
    public const string WatchAddExpression = "Watch_AddExpression";
    public const string BreakpointWhen = "Breakpoint_When";
    public const string BreakpointRemove = "Breakpoint_Remove";
    public const string BreakpointGoTo = "Breakpoint_GoTo";
    public const string BreakpointAddCondition = "Breakpoint_AddCondition";
    public const string BreakpointEditCondition = "Breakpoint_EditCondition";
    public const string ReferencesCount = "References_Count";
    public const string ReferencesNoneTo = "References_NoneTo";
    public const string ReferencesEmpty = "References_Empty";
    public const string ReferencesNone = "References_None";
    public const string BranchRemote = "Branch_Remote";
    public const string BranchLocal = "Branch_Local";
    public const string BranchDelete = "Branch_Delete";
    public const string BranchMerge = "Branch_Merge";
    public const string BranchCreate = "Branch_Create";
    public const string BranchNewName = "Branch_NewName";
    public const string GitHistoryEmpty = "Git_HistoryEmpty";
    public const string GitStageSomething = "Git_StageSomething";
    public const string GitWriteMessageFirst = "Git_WriteMessageFirst";
    public const string GitUpToDate = "Git_UpToDate";
    public const string GitNotPublished = "Git_NotPublished";
    public const string GitNoBranch = "Git_NoBranch";
    public const string GitCopyPath = "Git_CopyPath";
    public const string GitDiscardChanges = "Git_DiscardChanges";
    public const string GitOpenFile = "Git_OpenFile";
    public const string GitOpenDiff = "Git_OpenDiff";
    public const string GitStage = "Git_Stage";
    public const string GitUnstage = "Git_Unstage";
    public const string GitChanges = "Git_Changes";
    public const string GitStaged = "Git_Staged";
    public const string GitUnstageAll = "Git_UnstageAll";
    public const string GitStageAll = "Git_StageAll";
    public const string GitAmend = "Git_Amend";
    public const string GitCommit = "Git_Commit";
    public const string GitAmendLast = "Git_AmendLast";
    public const string GitCommitMessage = "Git_CommitMessage";
    public const string TestsSummary = "Tests_Summary";
    public const string TestsCount = "Tests_Count";
    public const string TestsRunning = "Tests_Running";
    public const string TestsNoClass = "Tests_NoClass";
    public const string TestsCopyFailure = "Tests_CopyFailure";
    public const string TestsCopyName = "Tests_CopyName";
    public const string TestsOpenSource = "Tests_OpenSource";
    public const string TestsRun = "Tests_Run";
    public const string TestsDebug = "Tests_Debug";
    public const string TestsRunFailed = "Tests_RunFailed";
    public const string TestsRunSelected = "Tests_RunSelected";
    public const string TestsRunAll = "Tests_RunAll";
    public const string TestsNoneDiscovered = "Tests_NoneDiscovered";
    public const string TestsFilterWatermark = "Tests_FilterWatermark";
    public const string TestsFilterPassed = "Tests_FilterPassed";
    public const string TestsFilterFailed = "Tests_FilterFailed";
    public const string TestsFilterNotRun = "Tests_FilterNotRun";
    public const string TestsShown = "Tests_Shown";
    public const string TestsFilterSkipped = "Tests_FilterSkipped";
    public const string AiApply = "Ai_Apply";
    public const string AiAssistant = "Ai_Assistant";
    public const string AiYou = "Ai_You";
    public const string AiNoAssistant = "Ai_NoAssistant";
    public const string AiClearTip = "Ai_ClearTip";
    public const string AiClear = "Ai_Clear";
    public const string AiSendTip = "Ai_SendTip";
    public const string AiSend = "Ai_Send";
    public const string AiStop = "Ai_Stop";
    public const string AiStopTip = "Ai_StopTip";
    public const string AiAskPlaceholder = "Ai_AskPlaceholder";
    public const string AiActionDocument = "Ai_Action_Document";
    public const string AiActionTests = "Ai_Action_Tests";
    public const string AiActionFix = "Ai_Action_Fix";
    public const string AiActionExplain = "Ai_Action_Explain";
    public const string AiActionAsk = "Ai_Action_Ask";
    public const string AiNoticeFallback = "Ai_Notice_Fallback";
    public const string AiNoticeRefusal = "Ai_Notice_Refusal";
    public const string AiNoticeRefusalCategory = "Ai_Notice_RefusalCategory";
    public const string AiNoticeTruncated = "Ai_Notice_Truncated";
    public const string AiNoticeTruncatedTool = "Ai_Notice_TruncatedTool";
    public const string AiNoticeRoundLimit = "Ai_Notice_RoundLimit";
    public const string AiToolUsed = "Ai_Tool_Used";
    public const string AiToolFailed = "Ai_Tool_Failed";
    public const string AiChangesTitle = "Ai_Changes_Title";
    public const string AiChangesTitlePlain = "Ai_Changes_TitlePlain";
    public const string AiChangeAccept = "Ai_Change_Accept";
    public const string AiChangeReject = "Ai_Change_Reject";
    public const string AiChangeAcceptAll = "Ai_Change_AcceptAll";
    public const string AiChangeRejectAll = "Ai_Change_RejectAll";
    public const string AiChangeAccepted = "Ai_Change_Accepted";
    public const string AiChangeRejected = "Ai_Change_Rejected";
    public const string AiChangeNotApplied = "Ai_Change_NotApplied";
    public const string AiChangeNewFile = "Ai_Change_NewFile";
    public const string AiChangeApplying = "Ai_Change_Applying";
    public const string AiStopped = "Ai_Stopped";
    public const string AiErrorKeyRefused = "Ai_Error_KeyRefused";
    public const string AiErrorRateLimited = "Ai_Error_RateLimited";
    public const string AiErrorStatus = "Ai_Error_Status";
    public const string AiNoticeNewConversation = "Ai_Notice_NewConversation";
    public const string AiNoticeContextFull = "Ai_Notice_ContextFull";
    public const string AiApplyTitle = "Ai_Apply_Title";
    public const string AiApplyNoEditor = "Ai_Apply_NoEditor";
    public const string AiNeedsKey = "Ai_NeedsKey";
    public const string AiStatusModel = "Ai_Status_Model";
    public const string SettingsAiModelHint = "Settings_Ai_ModelHint";
    public const string ProblemsErrorsCountOne = "Problems_ErrorsCountOne";
    public const string ProblemsWarningsCountOne = "Problems_WarningsCountOne";
    public const string ProblemsMessagesCountOne = "Problems_MessagesCountOne";
    public const string MenuFileMakeExecutable = "MenuFile_MakeExecutable";
    public const string MakeExeTitle = "MakeExe_Title";
    public const string MakeExeTargets = "MakeExe_Targets";
    public const string MakeExeOutputFolder = "MakeExe_OutputFolder";
    public const string MakeExeOutputHint = "MakeExe_OutputHint";
    public const string MakeExeConfiguration = "MakeExe_Configuration";
    public const string MakeExeMake = "MakeExe_Make";
    public const string MakeExeCancel = "MakeExe_Cancel";
    public const string MakeExeNeedTarget = "MakeExe_NeedTarget";
    public const string MakeExeTargetWinX64 = "MakeExe_Target_WinX64";
    public const string MakeExeTargetWinArm64 = "MakeExe_Target_WinArm64";
    public const string MakeExeTargetMacArm64 = "MakeExe_Target_MacArm64";
    public const string MakeExeTargetMacX64 = "MakeExe_Target_MacX64";
    public const string MakeExeTargetLinuxX64 = "MakeExe_Target_LinuxX64";
    public const string MakeExeTargetLinuxArm64 = "MakeExe_Target_LinuxArm64";
    public const string MakeExePublishing = "MakeExe_Publishing";
    public const string MakeExeProduced = "MakeExe_Produced";
    public const string MakeExeMissingOutput = "MakeExe_MissingOutput";
    public const string MakeExeIconNotIcns = "MakeExe_IconNotIcns";
    public const string StatusMakeExeRunning = "Status_MakeExeRunning";
    public const string StatusMakeExeDone = "Status_MakeExeDone";
    public const string StatusMakeExeFailed = "Status_MakeExeFailed";
    public const string StatusMakeExeStopped = "Status_MakeExeStopped";
    public const string StatusMakeExeInterrupted = "Status_MakeExeInterrupted";
    public const string MakeExeUnsignedBundle = "MakeExe_UnsignedBundle";
    public const string MakeExeAssemblyNameFallback = "MakeExe_AssemblyNameFallback";
    public const string MakeExeFailedExit = "MakeExe_FailedExit";
    public const string MakeExeNuGetHint = "MakeExe_NuGetHint";
    public const string StatusMakeExeBusy = "Status_MakeExeBusy";
    public const string SearchFileMatchesOne = "Search_FileMatchesOne";
    public const string ToolObjectBrowser = "Tool_ObjectBrowser";
    public const string ObjectBrowserLibraryTip = "ObjectBrowser_LibraryTip";
    public const string ObjectBrowserRefresh = "ObjectBrowser_Refresh";
    public const string ObjectBrowserSearchPlaceholder = "ObjectBrowser_SearchPlaceholder";
    public const string ObjectBrowserInherited = "ObjectBrowser_Inherited";
    public const string ObjectBrowserInheritedTip = "ObjectBrowser_InheritedTip";
    public const string ObjectBrowserTypes = "ObjectBrowser_Types";
    public const string ObjectBrowserMembers = "ObjectBrowser_Members";
    public const string ObjectBrowserCopySignature = "ObjectBrowser_CopySignature";
    public const string ObjectBrowserGoToSource = "ObjectBrowser_GoToSource";
    public const string ObjectBrowserEmpty = "ObjectBrowser_Empty";
    public const string ObjectBrowserNoMatches = "ObjectBrowser_NoMatches";
    public const string ObjectBrowserAllLibraries = "ObjectBrowser_AllLibraries";
    public const string ObjectBrowserSelectType = "ObjectBrowser_SelectType";
    public const string ObjectBrowserMemberOf = "ObjectBrowser_MemberOf";
    public const string ObjectBrowserNamespace = "ObjectBrowser_Namespace";
    public const string ObjectBrowserAssembly = "ObjectBrowser_Assembly";
    public const string ObjectBrowserFromMetadata = "ObjectBrowser_FromMetadata";
    public const string ToolImmediate = "Tool_Immediate";
    public const string ImmediateClear = "Immediate_Clear";
    public const string ImmediatePrompt = "Immediate_Prompt";
    public const string ImmediateRunning = "Immediate_Running";
    public const string ImmediateNoValue = "Immediate_NoValue";
    public const string ImmediateTimedOut = "Immediate_TimedOut";
    public const string ImmediateExited = "Immediate_Exited";
    public const string ImmediateStartFailed = "Immediate_StartFailed";
    public const string ImmediateBuilding = "Immediate_Building";
    public const string ImmediateBuildFailed = "Immediate_BuildFailed";
    public const string ObjectBrowserTypesCapped = "ObjectBrowser_TypesCapped";
    public const string ImmediateOutputTruncated = "Immediate_OutputTruncated";
    public const string ImmediateSessionEnded = "Immediate_SessionEnded";
    public const string ImmediateNoStatements = "Immediate_NoStatements";
    public const string ImmediateBuildBusy = "Immediate_BuildBusy";
    public const string ImmediateStale = "Immediate_Stale";
    public const string ImmediateNoRuntime = "Immediate_NoRuntime";
    public const string ImmediateStopped = "Immediate_Stopped";
    public const string ImmediateStop = "Immediate_Stop";
    public const string ImmediateOutputIncomplete = "Immediate_OutputIncomplete";
    public const string StatusWaitingForBuild = "Status_WaitingForBuild";
    public const string StatusBuildAlreadyWaiting = "Status_BuildAlreadyWaiting";
    public const string GateBuild = "Gate_Build";
    public const string GatePublish = "Gate_Publish";
    public const string GateImmediate = "Gate_Immediate";
    public const string GateTests = "Gate_Tests";
    public const string SettingsAiInline = "Settings_Ai_Inline";
    public const string SettingsAiInlineNote = "Settings_Ai_InlineNote";
    public const string SettingsAiInlineModel = "Settings_Ai_InlineModel";
    public const string SettingsAiInlineModelHint = "Settings_Ai_InlineModelHint";
    public const string InlineFailed = "Inline_Failed";
    public const string InlineMoreLine = "Inline_MoreLine";
    public const string InlineMoreLines = "Inline_MoreLines";

    public const string ToolWatch = "Tool_Watch";

    public const string ExplorerNewFileTitle = "Explorer_NewFile_Title";
    public const string ExplorerNewFilePrompt = "Explorer_NewFile_Prompt";
    public const string ExplorerNewFolderTitle = "Explorer_NewFolder_Title";
    public const string ExplorerNewFolderPrompt = "Explorer_NewFolder_Prompt";
    public const string ExplorerRenameTitle = "Explorer_Rename_Title";
    public const string ExplorerRenamePrompt = "Explorer_Rename_Prompt";
    public const string ExplorerRenameClassTitle = "Explorer_RenameClass_Title";
    public const string ExplorerRenameClassPrompt = "Explorer_RenameClass_Prompt";
    public const string ExplorerDeleteTitle = "Explorer_Delete_Title";
    public const string ExplorerDeleteFileMessage = "Explorer_Delete_FileMessage";
    public const string ExplorerDeleteFolderMessage = "Explorer_Delete_FolderMessage";
    public const string ExplorerDeleteFileMessageUnsaved = "Explorer_Delete_FileMessageUnsaved";
    public const string ExplorerDeleteFolderMessageUnsaved = "Explorer_Delete_FolderMessageUnsaved";
    public const string ExplorerDeleteConfirm = "Explorer_Delete_Confirm";
    public const string ExplorerNameEmpty = "Explorer_NameEmpty";
    public const string ExplorerNameExists = "Explorer_NameExists";
    public const string ExplorerNameInvalid = "Explorer_NameInvalid";
    public const string ExplorerOperationFailed = "Explorer_OperationFailed";
    public const string ExplorerBlockedWhileDebugging = "Explorer_BlockedWhileDebugging";
    public const string ExplorerAddNewItem = "Explorer_AddNewItem";

    // Save/Don't Save/Cancel, asked whenever a dirty document is about to be
    // closed: a tab's own close, Close All and friends, opening another
    // solution, New Project and quitting.
    public const string SaveChangesTitle = "SaveChanges_Title";
    public const string SaveChangesMessageOne = "SaveChanges_MessageOne";
    public const string SaveChangesMessageMany = "SaveChanges_MessageMany";
    public const string SaveChangesSave = "SaveChanges_Save";
    public const string SaveChangesDontSave = "SaveChanges_DontSave";

    // Shown instead of closing the document when its save failed (a
    // read-only file, say): the text stays in the tab rather than being
    // lost, and the user is told why it was not saved.
    public const string SaveChangesSaveFailedTitle = "SaveChanges_SaveFailedTitle";
    public const string SaveChangesSaveFailedMessage = "SaveChanges_SaveFailedMessage";

    // WinForms designer smart tag: the Visual Studio style glyph a selected control with
    // tasks shows at the top-right corner of its selection, and its "<Type> Tasks" flyout.
    public const string WinFormsSmartTagTasksHeader = "WinFormsSmartTag_TasksHeader";
    public const string WinFormsSmartTagInsertStandardItems = "WinFormsSmartTag_InsertStandardItems";
    public const string WinFormsSmartTagEditItems = "WinFormsSmartTag_EditItems";
    public const string WinFormsSmartTagDock = "WinFormsSmartTag_Dock";
    public const string WinFormsSmartTagDockInParent = "WinFormsSmartTag_DockInParent";
    public const string WinFormsSmartTagUndockInParent = "WinFormsSmartTag_UndockInParent";
    public const string WinFormsSmartTagSizeMode = "WinFormsSmartTag_SizeMode";

    // "Select Resource" dialog for Image, BackgroundImage, InitialImage, ErrorImage
    // and Icon properties: Visual Studio's Local resource / Project resource file choice.
    public const string WinFormsImageDialogTitle = "WinFormsImageDialog_Title";
    public const string WinFormsImageDialogLocalResource = "WinFormsImageDialog_LocalResource";
    public const string WinFormsImageDialogProjectResource = "WinFormsImageDialog_ProjectResource";
    public const string WinFormsImageDialogImport = "WinFormsImageDialog_Import";
    public const string WinFormsImageDialogClear = "WinFormsImageDialog_Clear";
    public const string WinFormsImageDialogImageFiles = "WinFormsImageDialog_ImageFiles";
    public const string WinFormsImageDialogImportFailed = "WinFormsImageDialog_ImportFailed";
    public const string WinFormsImageDialogInvalidFormat = "WinFormsImageDialog_InvalidFormat";
    public const string WinFormsImageDialogProjectImportFailed = "WinFormsImageDialog_ProjectImportFailed";

    // Project > Add Window… dialog: asks for the new window's class name,
    // namespace and title before writing the Avalonia window to disk.
    // (NewWindowTitle, NewWindowClassName, NewWindowNamespace and
    // NewWindowWindowTitle are declared once already, above.)
    public const string NewWindowHeading = "NewWindow_Heading";
    public const string NewWindowClassNamePlaceholder = "NewWindow_ClassNamePlaceholder";
    public const string NewWindowNamespacePlaceholder = "NewWindow_NamespacePlaceholder";
    public const string NewWindowWindowTitlePlaceholder = "NewWindow_WindowTitlePlaceholder";
    public const string NewWindowNameRequired = "NewWindow_NameRequired";
    public const string NewWindowNameInvalid = "NewWindow_NameInvalid";

    // Add New Item (Solution Explorer and Project), and the File > Open filter.
    public const string AddItemTitle = "AddItem_Title";
    public const string AddItemTemplates = "AddItem_Templates";
    public const string AddItemName = "AddItem_Name";
    public const string AddItemCreate = "AddItem_Create";
    public const string AddItemNameInvalid = "AddItem_NameInvalid";
    public const string AddItemClass = "AddItem_Class";
    public const string AddItemModule = "AddItem_Module";
    public const string AddItemInterface = "AddItem_Interface";
    public const string AddItemWindowsForm = "AddItem_WindowsForm";
    public const string AddItemUserControl = "AddItem_UserControl";
    public const string AddItemEmptyFile = "AddItem_EmptyFile";
    public const string OpenFilterSolutions = "Open_Filter_Solutions";

    // Project Properties dialog
    public const string ProjectPropertiesTitle = "ProjectProperties_Title";
    public const string ProjectPropertiesSave = "ProjectProperties_Save";
    // Project Properties: categories
    public const string ProjectPropertiesCategoryApplication = "ProjectProperties_Category_Application";
    public const string ProjectPropertiesCategoryCompile = "ProjectProperties_Category_Compile";
    public const string ProjectPropertiesCategoryAdvanced = "ProjectProperties_Category_Advanced";
    public const string ProjectPropertiesCategoryReferences = "ProjectProperties_Category_References";
    public const string ProjectPropertiesCategoryDebug = "ProjectProperties_Category_Debug";
    public const string ProjectPropertiesCategoryPublish = "ProjectProperties_Category_Publish";
    // Project Properties: fields
    public const string ProjectPropertiesAssemblyName = "ProjectProperties_AssemblyName";
    public const string ProjectPropertiesRootNamespace = "ProjectProperties_RootNamespace";
    public const string ProjectPropertiesRootNamespaceHelp = "ProjectProperties_RootNamespaceHelp";
    public const string ProjectPropertiesOutputType = "ProjectProperties_OutputType";
    public const string ProjectPropertiesStartupObject = "ProjectProperties_StartupObject";
    public const string ProjectPropertiesStartupObjectHelp = "ProjectProperties_StartupObjectHelp";
    public const string ProjectPropertiesTargetFramework = "ProjectProperties_TargetFramework";
    public const string ProjectPropertiesOptionStrict = "ProjectProperties_OptionStrict";
    public const string ProjectPropertiesOptionStrictHelp = "ProjectProperties_OptionStrictHelp";
    public const string ProjectPropertiesOptionExplicit = "ProjectProperties_OptionExplicit";
    public const string ProjectPropertiesOptionExplicitHelp = "ProjectProperties_OptionExplicitHelp";
    public const string ProjectPropertiesOptionInfer = "ProjectProperties_OptionInfer";
    public const string ProjectPropertiesOptionInferHelp = "ProjectProperties_OptionInferHelp";
    public const string ProjectPropertiesOptionCompare = "ProjectProperties_OptionCompare";
    public const string ProjectPropertiesOptionCompareHelp = "ProjectProperties_OptionCompareHelp";
    public const string ProjectPropertiesConditionalConstants = "ProjectProperties_ConditionalConstants";
    public const string ProjectPropertiesConditionalConstantsHelp = "ProjectProperties_ConditionalConstantsHelp";
    public const string ProjectPropertiesSuppressedWarnings = "ProjectProperties_SuppressedWarnings";
    public const string ProjectPropertiesSuppressedWarningsHelp = "ProjectProperties_SuppressedWarningsHelp";
    public const string ProjectPropertiesTreatWarningsAsErrors = "ProjectProperties_TreatWarningsAsErrors";
    public const string ProjectPropertiesGenerateXmlDocumentation = "ProjectProperties_GenerateXmlDocumentation";
    public const string ProjectPropertiesNullable = "ProjectProperties_Nullable";
    public const string ProjectPropertiesPackages = "ProjectProperties_Packages";
    public const string ProjectPropertiesPackageId = "ProjectProperties_PackageId";
    public const string ProjectPropertiesVersion = "ProjectProperties_Version";
    public const string ProjectPropertiesAddPackage = "ProjectProperties_AddPackage";
    public const string ProjectPropertiesRemovePackage = "ProjectProperties_RemovePackage";
    public const string ProjectPropertiesProjects = "ProjectProperties_Projects";
    public const string ProjectPropertiesCommandLineArguments = "ProjectProperties_CommandLineArguments";
    public const string ProjectPropertiesWorkingDirectory = "ProjectProperties_WorkingDirectory";
    public const string ProjectPropertiesApplicationUrl = "ProjectProperties_ApplicationUrl";
    public const string ProjectPropertiesApplicationUrlHelp = "ProjectProperties_ApplicationUrlHelp";
    public const string ProjectPropertiesLaunchBrowser = "ProjectProperties_LaunchBrowser";
    public const string ProjectPropertiesEnvironmentVariables = "ProjectProperties_EnvironmentVariables";
    public const string ProjectPropertiesEnvironmentVariablesHelp = "ProjectProperties_EnvironmentVariablesHelp";
    public const string ProjectPropertiesRuntimeIdentifier = "ProjectProperties_RuntimeIdentifier";
    public const string ProjectPropertiesRuntimeIdentifierHelp = "ProjectProperties_RuntimeIdentifierHelp";
    public const string ProjectPropertiesSelfContained = "ProjectProperties_SelfContained";
    public const string ProjectPropertiesSelfContainedHelp = "ProjectProperties_SelfContainedHelp";
    public const string ProjectPropertiesTrimUnusedCode = "ProjectProperties_TrimUnusedCode";
    // About window
    public const string AboutVersion = "About_Version";
    public const string AboutDescription = "About_Description";
    public const string AboutLicence = "About_Licence";
    public const string AboutCopyDetails = "About_CopyDetails";
    // Change Signature dialog
    public const string ChangeSignatureTitle = "ChangeSignature_Title";
    public const string ChangeSignatureAddParameter = "ChangeSignature_AddParameter";
    public const string ChangeSignatureApply = "ChangeSignature_Apply";
    public const string ChangeSignatureNamePlaceholder = "ChangeSignature_NamePlaceholder";
    public const string ChangeSignatureAdd = "ChangeSignature_Add";
    public const string ChangeSignaturePass = "ChangeSignature_Pass";
    // Breakpoint condition dialog
    public const string BreakpointConditionEveryTime = "BreakpointCondition_EveryTime";
    public const string BreakpointConditionStopWhenTrue = "BreakpointCondition_StopWhenTrue";
    public const string BreakpointConditionStopWhenTrueHelp = "BreakpointCondition_StopWhenTrueHelp";
    public const string BreakpointConditionHitCount = "BreakpointCondition_HitCount";
    public const string BreakpointConditionHitCountHelp = "BreakpointCondition_HitCountHelp";
    public const string BreakpointConditionEvaluates = "BreakpointCondition_Evaluates";
    // Breakpoint margin menu
    public const string BreakpointRemoveMenu = "Breakpoint_RemoveMenu";
    public const string BreakpointAddMenu = "Breakpoint_AddMenu";
    public const string BreakpointCondition = "Breakpoint_Condition";
    public const string BreakpointRunToHere = "Breakpoint_RunToHere";
    // Refactoring preview dialog
    public const string RefactoringPreviewDefaultTitle = "RefactoringPreview_DefaultTitle";
    public const string RefactoringPreviewApply = "RefactoringPreview_Apply";
    public const string DebugBannerStop = "Debug_BannerStop";

    // MainWindow: operations, designer and debug banner
    public const string OperationStopping = "Operation_Stopping";
    public const string DesignerCannotShow = "Designer_CannotShow";
    public const string DebugBannerPaused = "Debug_BannerPaused";
    public const string DebugBannerRunning = "Debug_BannerRunning";
    public const string DebugExited = "Debug_Exited";
    public const string DebugTheProgram = "Debug_TheProgram";
    public const string DebugInputOnInterfaceThread = "Debug_InputOnInterfaceThread";
    // MainWindow: refactoring prompts
    public const string RefactorExtractPartialTitle = "Refactor_ExtractPartial_Title";
    public const string RefactorExtractPartialPrompt = "Refactor_ExtractPartial_Prompt";
    public const string RefactorExtractPartialName = "Refactor_ExtractPartial_Name";
    public const string RefactorExtractConstantTitle = "Refactor_ExtractConstant_Title";
    public const string RefactorExtractConstantPrompt = "Refactor_ExtractConstant_Prompt";
    public const string RefactorExtractConstantName = "Refactor_ExtractConstant_Name";
    public const string RefactorExtractMethodTitle = "Refactor_ExtractMethod_Title";
    public const string RefactorExtractMethodPrompt = "Refactor_ExtractMethod_Prompt";
    public const string RefactorExtractMethodName = "Refactor_ExtractMethod_Name";
    public const string RefactorExtractVariableTitle = "Refactor_ExtractVariable_Title";
    public const string RefactorExtractVariablePrompt = "Refactor_ExtractVariable_Prompt";
    public const string RefactorExtractVariableName = "Refactor_ExtractVariable_Name";
    public const string RefactorRenameTitle = "Refactor_Rename_Title";
    public const string RefactorRenamePrompt = "Refactor_Rename_Prompt";
    public const string EditorNeedsSubOrFunction = "Editor_NeedsSubOrFunction";
    public const string EditorMappedRegions = "Editor_MappedRegions";
    public const string EditorNoNameUnderCaret = "Editor_NoNameUnderCaret";
    public const string EditorPartialOnlyFromTemplate = "Editor_PartialOnlyFromTemplate";
    public const string EditorSelectMarkupFirst = "Editor_SelectMarkupFirst";
    public const string EditorTreatingAs = "Editor_TreatingAs";
    public const string EditorReopenedAs = "Editor_ReopenedAs";
    public const string RenameCaretOnNameFirst = "Rename_CaretOnNameFirst";
    public const string RenameNotWritten = "Rename_NotWritten";
    public const string TestNothingToRun = "Test_NothingToRun";
    public const string TestSelectSingleToDebug = "Test_SelectSingleToDebug";
    public const string TestDebugging = "Test_Debugging";
    public const string PluginLoaded = "Plugin_Loaded";
    public const string PluginDidNotLoad = "Plugin_DidNotLoad";
    public const string CommandNotImplemented = "Command_NotImplemented";
    // MainWindow: indentation, line endings, encoding, language
    public const string IndentTitle = "Indent_Title";
    public const string IndentUse = "Indent_Use";
    public const string IndentSpaces = "Indent_Spaces";
    public const string IndentTabs = "Indent_Tabs";
    public const string IndentLabel = "Indent_Label";
    public const string LineEndingTitle = "LineEnding_Title";
    public const string LineEndingPrompt = "LineEnding_Prompt";
    public const string EncodingReopenTitle = "Encoding_ReopenTitle";
    public const string EncodingReopenPrompt = "Encoding_ReopenPrompt";
    public const string LanguageTitle = "Language_Title";
    public const string LanguagePrompt = "Language_Prompt";
    public const string StatusCaret = "Status_Caret";
    public const string StatusCaretSelected = "Status_CaretSelected";
    // MainWindow: toolbar and dialog labels
    public const string ToolbarBuildConfiguration = "Toolbar_BuildConfiguration";
    public const string ToolbarStartupProject = "Toolbar_StartupProject";
    public const string OpenFilterSqlite = "Open_Filter_Sqlite";
    public const string OpenFilterAllFiles = "Open_Filter_AllFiles";
    public const string RevertTitle = "Revert_Title";
    public const string RevertMessage = "Revert_Message";
    public const string RevertDiscard = "Revert_Discard";
    public const string DialogGo = "Dialog_Go";

    // Solution Explorer context menu
    public const string SolutionExplorerOpen = "SolutionExplorer_Open";
    public const string SolutionExplorerOpenInDesigner = "SolutionExplorer_OpenInDesigner";
    public const string SolutionExplorerNewFile = "SolutionExplorer_NewFile";
    public const string SolutionExplorerNewFolder = "SolutionExplorer_NewFolder";
    public const string SolutionExplorerRename = "SolutionExplorer_Rename";
    public const string SolutionExplorerDelete = "SolutionExplorer_Delete";
    public const string SolutionExplorerCopyPath = "SolutionExplorer_CopyPath";
    public const string SolutionExplorerCopyRelativePath = "SolutionExplorer_CopyRelativePath";
    public const string SolutionExplorerRefresh = "SolutionExplorer_Refresh";
    public const string SolutionExplorerProperties = "SolutionExplorer_Properties";
    // Code editor context menu
    public const string EditorMenuCut = "EditorMenu_Cut";
    public const string EditorMenuCopy = "EditorMenu_Copy";
    public const string EditorMenuPaste = "EditorMenu_Paste";
    public const string EditorMenuGoToDefinition = "EditorMenu_GoToDefinition";
    public const string EditorMenuFindReferences = "EditorMenu_FindReferences";
    public const string EditorMenuRename = "EditorMenu_Rename";
    public const string EditorMenuQuickActions = "EditorMenu_QuickActions";
    public const string EditorMenuExtractVariable = "EditorMenu_ExtractVariable";
    public const string EditorMenuExtractConstant = "EditorMenu_ExtractConstant";
    public const string EditorMenuInlineVariable = "EditorMenu_InlineVariable";
    public const string EditorMenuConvertConditional = "EditorMenu_ConvertConditional";
    public const string EditorMenuGenerateConstructor = "EditorMenu_GenerateConstructor";
    public const string EditorMenuGenerateProperty = "EditorMenu_GenerateProperty";
    public const string EditorMenuMoveTypeToFile = "EditorMenu_MoveTypeToFile";
    public const string EditorMenuChangeSignature = "EditorMenu_ChangeSignature";
    public const string EditorMenuExtractMethod = "EditorMenu_ExtractMethod";
    public const string EditorMenuAddImport = "EditorMenu_AddImport";
    public const string EditorMenuExtractPartial = "EditorMenu_ExtractPartial";
    public const string EditorMenuTidyImports = "EditorMenu_TidyImports";
    public const string EditorMenuFormatSelection = "EditorMenu_FormatSelection";
    public const string EditorMenuFormatDocument = "EditorMenu_FormatDocument";
    public const string EditorMenuToggleBreakpoint = "EditorMenu_ToggleBreakpoint";
    public const string EditorMenuRunToHere = "EditorMenu_RunToHere";
    public const string EditorMenuExplainWithAssistant = "EditorMenu_ExplainWithAssistant";
    public const string EditorMenuAskAssistant = "EditorMenu_AskAssistant";
    // Terminal context menu
    public const string TerminalCopy = "Terminal_Copy";
    public const string TerminalPaste = "Terminal_Paste";
    public const string TerminalClear = "Terminal_Clear";
    public const string TerminalCloseTerminal = "Terminal_CloseTerminal";
    // Document tab context menu
    public const string DocumentTabClose = "DocumentTab_Close";
    public const string DocumentTabCloseOthers = "DocumentTab_CloseOthers";
    public const string DocumentTabCloseAll = "DocumentTab_CloseAll";
    public const string DocumentTabCloseToTheRight = "DocumentTab_CloseToTheRight";
    public const string DocumentTabCopyPath = "DocumentTab_CopyPath";
    public const string DocumentTabOpenContainingFolder = "DocumentTab_OpenContainingFolder";
    // Dock titles (the Id values stay as they are)
    public const string DockDocuments = "Dock_Documents";
    public const string DockLeft = "Dock_Left";
    public const string DockRightTop = "Dock_RightTop";
    public const string DockRightBottom = "Dock_RightBottom";
    public const string DockRight = "Dock_Right";
    public const string DockBottom = "Dock_Bottom";
    public const string DockRoot = "Dock_Root";
    // Status bar
    public const string LineEndingMixed = "LineEnding_Mixed";
    public const string ShortcutRecorderPlaceholder = "ShortcutRecorder_Placeholder";
    // Designer problems
    public const string DesignerCannotShowAt = "Designer_CannotShowAt";
    public const string DesignerCannotShowMessage = "Designer_CannotShowMessage";
    public const string DesignerOcxPlaceholders = "Designer_OcxPlaceholders";
    public const string DebugAdapterNotFound = "Debug_AdapterNotFound";

    // Change Signature: the preview row for a new parameter
    public const string ChangeSignatureNewParameter = "ChangeSignature_NewParameter";
    // Code editor and Output messages
    public const string EditorCopied = "Editor_Copied";
    public const string InputTitle = "Input_Title";
    public const string BreakpointConditionTitle = "BreakpointCondition_Title";
    // Theme names shown in Settings
    public const string ThemeLight = "Theme_Light";
    public const string ThemeDark = "Theme_Dark";
    public const string ThemeSystem = "Theme_System";
    public const string ThemeVisualStudioBlue = "Theme_VisualStudioBlue";
    public const string ThemeHighContrastLight = "Theme_HighContrastLight";
    public const string ThemeHighContrastDark = "Theme_HighContrastDark";
    public const string ThemeSolarizedLight = "Theme_SolarizedLight";
    public const string ThemeSolarizedDark = "Theme_SolarizedDark";

    public const string LanguagePlainText = "Language_PlainText";
}
