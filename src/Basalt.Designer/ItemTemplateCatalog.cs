using Basalt.Core.Model;

namespace Basalt.Designer;

/// <summary>
/// The identifiers of the templates. Strings rather than an enum so that a
/// plug-in or a test can name one, and so that a template can be added
/// without a type every caller has to be recompiled for.
/// </summary>
public static class ItemTemplateIds
{
    // The six the dialog has always offered.
    public const string Class = "Class";
    public const string Module = "Module";
    public const string Interface = "Interface";
    public const string WindowsForm = "WindowsForm";
    public const string UserControl = "UserControl";
    public const string EmptyFile = "EmptyFile";

    // Code
    public const string Structure = "Structure";
    public const string Enum = "Enum";
    public const string ExceptionClass = "ExceptionClass";

    // Windows Forms
    public const string Dialog = "Dialog";
    public const string AboutBox = "AboutBox";
    public const string Component = "Component";

    // Avalonia
    public const string AvaloniaWindow = "AvaloniaWindow";
    public const string AvaloniaUserControl = "AvaloniaUserControl";

    // Web
    public const string HtmlPage = "HtmlPage";
    public const string StyleSheet = "StyleSheet";
    public const string JavaScript = "JavaScript";
    public const string JsonFile = "JsonFile";

    // Razor and Blazor
    public const string MvcView = "MvcView";
    public const string LayoutPage = "LayoutPage";
    public const string PartialView = "PartialView";
    public const string MvcController = "MvcController";
    public const string ApiController = "ApiController";
    public const string BlazorComponent = "BlazorComponent";
    public const string VbPage = "VbPage";

    // Data
    public const string XmlFile = "XmlFile";
    public const string XmlSchema = "XmlSchema";
    public const string TextFile = "TextFile";
    public const string ResourceFile = "ResourceFile";
    public const string AppConfig = "AppConfig";

    // Testing
    public const string XUnitTest = "XUnitTest";
    public const string NUnitTest = "NUnitTest";
    public const string MSTestTest = "MSTestTest";

    // General
    public const string AnyFile = "AnyFile";
}

/// <summary>
/// The catalogue of Add New Item: one record per template, in the order the
/// dialog lists them within a category.
/// </summary>
public static class ItemTemplateCatalog
{
    public static IReadOnlyList<ItemCategory> Categories { get; } = Enum.GetValues<ItemCategory>();

    public static IReadOnlyList<ItemTemplateDefinition> All { get; } = Build();

    /// <summary>
    /// The keys of the six templates that existed before the catalogue keep
    /// their names, so every language file that translated them still does.
    /// </summary>
    private static string NameKey(string id) => id switch
    {
        ItemTemplateIds.Class => "AddItem_Class",
        ItemTemplateIds.Module => "AddItem_Module",
        ItemTemplateIds.Interface => "AddItem_Interface",
        ItemTemplateIds.WindowsForm => "AddItem_WindowsForm",
        ItemTemplateIds.UserControl => "AddItem_UserControl",
        ItemTemplateIds.EmptyFile => "AddItem_EmptyFile",
        _ => $"AddItem_Tpl_{id}_Name"
    };

    private static string DescriptionKey(string id) => $"AddItem_Tpl_{id}_Description";

    private static ItemTemplateDefinition Make(
        string id, ItemCategory category, string icon, string defaultName,
        Func<string, IReadOnlyList<ItemFile>> generate,
        ItemNaming naming = ItemNaming.Identifier,
        ItemRequirement requires = ItemRequirement.None,
        string[]? suffixes = null,
        bool forcedExtension = true,
        string? requiredSuffix = null,
        string[]? alternates = null,
        string[]? baseTypes = null)
    {
        // The first suffix is the extension a name takes. An empty one (an
        // empty file of any kind) means the name is kept as typed.
        suffixes ??= [Path.GetExtension(defaultName)];

        return new ItemTemplateDefinition(
            id, category, icon, NameKey(id), DescriptionKey(id), defaultName, naming, requires,
            suffixes, generate, forcedExtension, requiredSuffix, alternates, baseTypes);
    }

    private static IReadOnlyList<ItemFile> One(string name, string extension, string text) =>
        [new ItemFile(name + extension, text)];

    private static IReadOnlyList<ItemTemplateDefinition> Build() =>
    [
        // ---- Code ---------------------------------------------------------
        Make(ItemTemplateIds.Class, ItemCategory.Code, "ClassItem", "Class1.vb",
            name => One(name, ".vb", CodeTexts.Class(name))),
        Make(ItemTemplateIds.Module, ItemCategory.Code, "ModuleItem", "Module1.vb",
            name => One(name, ".vb", CodeTexts.Module(name))),
        Make(ItemTemplateIds.Interface, ItemCategory.Code, "InterfaceItem", "Interface1.vb",
            name => One(name, ".vb", CodeTexts.Interface(name))),
        Make(ItemTemplateIds.Structure, ItemCategory.Code, "StructureItem", "Structure1.vb",
            name => One(name, ".vb", CodeTexts.Structure(name))),
        Make(ItemTemplateIds.Enum, ItemCategory.Code, "EnumItem", "Enum1.vb",
            name => One(name, ".vb", CodeTexts.Enum(name))),
        Make(ItemTemplateIds.ExceptionClass, ItemCategory.Code, "ExceptionItem", "CustomException.vb",
            name => One(name, ".vb", CodeTexts.Exception(name))),

        // The empty file of old: any extension typed is kept, none typed is .vb.
        Make(ItemTemplateIds.EmptyFile, ItemCategory.Code, "VisualBasicFile", "NewFile.vb",
            name => One(name, ".vb", ""), ItemNaming.FileName, forcedExtension: false,
            alternates: [".cs", ".bas", ".txt", ".md", ".json", ".xml", ".html", ".css", ".js", ".config",
                         ".vbhtml", ".vbrazor", ".vbpage", ".resx", ".yml", ".yaml", ".sql"]),

        // ---- Windows Forms ------------------------------------------------
        Make(ItemTemplateIds.WindowsForm, ItemCategory.WindowsForms, "FormItem", "Form1.vb",
            WinFormsTexts.Form, requires: ItemRequirement.WindowsForms, suffixes: [".vb", ".Designer.vb"],
            baseTypes: ["Form"]),
        Make(ItemTemplateIds.UserControl, ItemCategory.WindowsForms, "UserControlItem", "UserControl1.vb",
            WinFormsTexts.UserControl, requires: ItemRequirement.WindowsForms, suffixes: [".vb", ".Designer.vb"],
            baseTypes: ["UserControl"]),
        Make(ItemTemplateIds.Dialog, ItemCategory.WindowsForms, "FormItem", "Dialog1.vb",
            WinFormsTexts.Dialog, requires: ItemRequirement.WindowsForms, suffixes: [".vb", ".Designer.vb"],
            baseTypes: ["Form"]),
        Make(ItemTemplateIds.AboutBox, ItemCategory.WindowsForms, "FormItem", "AboutBox1.vb",
            WinFormsTexts.AboutBox, requires: ItemRequirement.WindowsForms, suffixes: [".vb", ".Designer.vb"],
            baseTypes: ["Form"]),
        Make(ItemTemplateIds.Component, ItemCategory.WindowsForms, "ComponentItem", "Component1.vb",
            name => One(name, ".vb", CodeTexts.Component(name)), baseTypes: ["Component"]),

        // ---- Avalonia -----------------------------------------------------
        // The text is the one Project > Add Window writes (FormTemplates), so
        // the two ways of adding a window cannot drift apart.
        Make(ItemTemplateIds.AvaloniaWindow, ItemCategory.Avalonia, "Window", "Window1.axaml",
            name => Avalonia(name, FormTemplates.WindowXaml(name, name)),
            requires: ItemRequirement.Avalonia, suffixes: [".axaml", ".axaml.vb"], baseTypes: ["Window"]),
        Make(ItemTemplateIds.AvaloniaUserControl, ItemCategory.Avalonia, "UserControlItem", "UserControl1.axaml",
            name => Avalonia(name, FormTemplates.UserControlXaml(name)),
            requires: ItemRequirement.Avalonia, suffixes: [".axaml", ".axaml.vb"], baseTypes: ["UserControl"]),

        // ---- Web ----------------------------------------------------------
        Make(ItemTemplateIds.HtmlPage, ItemCategory.Web, "WebFile", "HtmlPage1.html",
            name => One(name, ".html", WebTexts.HtmlPage(name)), ItemNaming.FileName, forcedExtension: false,
            alternates: [".htm"]),
        Make(ItemTemplateIds.StyleSheet, ItemCategory.Web, "StyleFile", "StyleSheet1.css",
            name => One(name, ".css", WebTexts.StyleSheet(name)), ItemNaming.FileName, forcedExtension: false,
            alternates: [".scss", ".less"]),
        Make(ItemTemplateIds.JavaScript, ItemCategory.Web, "ScriptFile", "JavaScript1.js",
            name => One(name, ".js", WebTexts.JavaScript(name)), ItemNaming.FileName, forcedExtension: false,
            alternates: [".mjs", ".ts"]),
        Make(ItemTemplateIds.JsonFile, ItemCategory.Web, "JsonFile", "JsonFile1.json",
            name => One(name, ".json", WebTexts.Json()), ItemNaming.FileName, forcedExtension: false,
            alternates: [".jsonc"]),

        // ---- Razor and Blazor ---------------------------------------------
        Make(ItemTemplateIds.MvcView, ItemCategory.Razor, "RazorFile", "Index.vbhtml",
            name => One(name, ".vbhtml", RazorTexts.MvcView(name)), ItemNaming.FileName, ItemRequirement.Web),
        Make(ItemTemplateIds.LayoutPage, ItemCategory.Razor, "RazorFile", "_Layout.vbhtml",
            name => One(name, ".vbhtml", RazorTexts.Layout(name)), ItemNaming.FileName, ItemRequirement.Web),
        Make(ItemTemplateIds.PartialView, ItemCategory.Razor, "RazorFile", "_Partial1.vbhtml",
            name => One(name, ".vbhtml", RazorTexts.PartialView(name)), ItemNaming.FileName, ItemRequirement.Web),
        Make(ItemTemplateIds.MvcController, ItemCategory.Razor, "ControllerItem", "HomeController.vb",
            name => One(name, ".vb", RazorTexts.MvcController(name)), requires: ItemRequirement.Web,
            requiredSuffix: "Controller", baseTypes: ["Controller"]),
        Make(ItemTemplateIds.ApiController, ItemCategory.Razor, "ApiItem", "ValuesController.vb",
            name => One(name, ".vb", RazorTexts.ApiController(name)), requires: ItemRequirement.Web,
            requiredSuffix: "Controller", baseTypes: ["Controller", "ControllerBase"]),
        Make(ItemTemplateIds.BlazorComponent, ItemCategory.Razor, "BlazorItem", "Component1.vbrazor",
            name => One(name, ".vbrazor", RazorTexts.BlazorComponent(name)), requires: ItemRequirement.Web),
        Make(ItemTemplateIds.VbPage, ItemCategory.Razor, "WebFile", "Page1.vbpage",
            name => One(name, ".vbpage", RazorTexts.VbPage(name)), ItemNaming.FileName, ItemRequirement.Web),

        // ---- Data ---------------------------------------------------------
        Make(ItemTemplateIds.XmlFile, ItemCategory.Data, "XmlFile", "XMLFile1.xml",
            name => One(name, ".xml", DataTexts.Xml()), ItemNaming.FileName, forcedExtension: false,
            alternates: [".xaml", ".svg", ".config", ".props", ".targets"]),
        Make(ItemTemplateIds.XmlSchema, ItemCategory.Data, "SchemaItem", "XMLSchema1.xsd",
            name => One(name, ".xsd", DataTexts.XmlSchema()), ItemNaming.FileName),
        Make(ItemTemplateIds.TextFile, ItemCategory.Data, "TextFile", "TextFile1.txt",
            name => One(name, ".txt", ""), ItemNaming.FileName, forcedExtension: false,
            alternates: [".md", ".log", ".csv", ".ini", ".cfg"]),
        Make(ItemTemplateIds.ResourceFile, ItemCategory.Data, "ResourceItem", "Resources1.resx",
            name => One(name, ".resx", DataTexts.Resources()), ItemNaming.FileName),
        Make(ItemTemplateIds.AppConfig, ItemCategory.Data, "ConfigItem", "App.config",
            name => One(name, ".config", DataTexts.AppConfig()), ItemNaming.FileName),

        // ---- Testing ------------------------------------------------------
        Make(ItemTemplateIds.XUnitTest, ItemCategory.Testing, "TestItem", "UnitTest1.vb",
            name => One(name, ".vb", TestTexts.XUnit(name)), requires: ItemRequirement.XUnit),
        Make(ItemTemplateIds.NUnitTest, ItemCategory.Testing, "TestItem", "NUnitTest1.vb",
            name => One(name, ".vb", TestTexts.NUnit(name)), requires: ItemRequirement.NUnit),
        Make(ItemTemplateIds.MSTestTest, ItemCategory.Testing, "TestItem", "MSTest1.vb",
            name => One(name, ".vb", TestTexts.MSTest(name)), requires: ItemRequirement.MSTest),

        // ---- General ------------------------------------------------------
        // A file of any kind: the name is kept exactly as typed.
        Make(ItemTemplateIds.AnyFile, ItemCategory.General, "TextFile", "File1",
            name => [new ItemFile(name, "")], ItemNaming.FileName, suffixes: [""])
    ];

    /// <summary>
    /// A window or control of Avalonia: the XAML and the Visual Basic
    /// code-behind that loads it, generated from the XAML as Add Window does.
    /// </summary>
    private static IReadOnlyList<ItemFile> Avalonia(string name, string xaml)
    {
        var path = name + ".axaml";
        var document = Model.XamlDocument.Parse(xaml, path);

        return
        [
            new ItemFile(path, xaml),
            new ItemFile(CodeBehindGenerator.CodeBehindPath(path, SourceLanguage.VisualBasic),
                CodeBehindGenerator.Generate(document, SourceLanguage.VisualBasic))
        ];
    }
}
