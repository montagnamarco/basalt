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
    public const string MenuNavigateBack = "Menu_Navigate_Back";
    public const string MenuNavigateForward = "Menu_Navigate_Forward";
    public const string MenuViewResetLayout = "Menu_View_ResetLayout";
    public const string MenuViewNewTerminal = "Menu_View_NewTerminal";

    public const string MenuProject = "Menu_Project";
    public const string MenuProjectAddWindow = "Menu_Project_AddWindow";
    public const string MenuProjectProperties = "Menu_Project_Properties";

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

    public const string ExplorerShowAllFiles = "Explorer_ShowAllFiles";
    public const string ExplorerCollapseAll = "Explorer_CollapseAll";
    public const string ExplorerSyncWithEditor = "Explorer_SyncWithEditor";
    public const string ExplorerRefresh = "Explorer_Refresh";
    public const string ToolToolbox = "Tool_Toolbox";
    public const string ToolProperties = "Tool_Properties";
    public const string ToolProblems = "Tool_Problems";
    public const string ToolOutput = "Tool_Output";
    public const string ToolSearch = "Tool_Search";
    public const string ToolOutline = "Tool_Outline";
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

    public const string NewWindowTitle = "NewWindow_Title";
    public const string NewWindowSubtitle = "NewWindow_Subtitle";
    public const string NewWindowClassName = "NewWindow_ClassName";
    public const string NewWindowNamespace = "NewWindow_Namespace";
    public const string NewWindowWindowTitle = "NewWindow_WindowTitle";
    public const string NewWindowCodeBehindVb = "NewWindow_CodeBehindVb";
    public const string NewWindowCodeBehindCs = "NewWindow_CodeBehindCs";

    public const string ButtonCreate = "Button_Create";
    public const string ButtonCancel = "Button_Cancel";

    public const string TerminalInputHint = "Terminal_InputHint";

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

    public const string ValidationNameRequired = "Validation_NameRequired";
    public const string ValidationInvalidIdentifier = "Validation_InvalidIdentifier";
    public const string ValidationLocationRequired = "Validation_LocationRequired";

    public const string ProblemsEmpty = "Problems_Empty";
    public const string OutputEmpty = "Output_Empty";

    public const string PropertiesNoSelection = "Properties_NoSelection";
    public const string PropertiesUnknownType = "Properties_UnknownType";
    public const string PropertiesCategoryCommon = "Properties_Category_Common";
    public const string PropertiesCategoryLayout = "Properties_Category_Layout";
    public const string PropertiesCategoryAppearance = "Properties_Category_Appearance";
    public const string PropertiesCategoryBehavior = "Properties_Category_Behavior";
    public const string ToolboxHint = "Toolbox_Hint";
    public const string DesignerCannotRender = "Designer_CannotRender";
    public const string PropertiesSearch = "Properties_Search";
    public const string PropertiesNoMatch = "Properties_NoMatch";
    public const string PropertiesReset = "Properties_Reset";
    public const string PropertiesSortByCategory = "Properties_SortByCategory";

    public const string PickerOpenSolution = "Picker_OpenSolution";
    public const string PickerSolutionFilter = "Picker_SolutionFilter";
    public const string PickerChooseFolder = "Picker_ChooseFolder";
}
