using System.Text.RegularExpressions;
using Basalt.Core.Localization;

namespace Basalt.Designer;

/// <summary>The groups Add New Item sorts its templates into, in the order its tree lists them.</summary>
public enum ItemCategory
{
    Code,
    WindowsForms,
    Avalonia,
    Web,
    Razor,
    Data,
    Testing,
    General
}

/// <summary>
/// What kind of project a template can be built in. A template that asks for
/// one the project is not stays on offer, marked, instead of vanishing: the
/// user learns what is missing rather than that the item does not exist.
/// </summary>
public enum ItemRequirement
{
    None,
    Web,
    WindowsForms,
    Avalonia,
    XUnit,
    NUnit,
    MSTest
}

/// <summary>How the name typed for a template becomes the name of its file.</summary>
public enum ItemNaming
{
    /// <summary>
    /// The name is a Visual Basic type name: an identifier that is not a
    /// reserved word. The file takes the template's extension.
    /// </summary>
    Identifier,

    /// <summary>The name is a file name; only the characters a file cannot hold are refused.</summary>
    FileName
}

/// <summary>What is wrong with the name typed for a template.</summary>
public enum ItemNameProblem
{
    None,
    Empty,
    NotAnIdentifier,
    InvalidFileName,

    /// <summary>The name is that of the class the item inherits from: it would inherit itself.</summary>
    ShadowsBaseType
}

/// <summary>The outcome of <see cref="ItemTemplates.Check"/>: the file name the typed name stands for, or why it cannot be one.</summary>
public sealed record ItemNameResult(ItemNameProblem Problem, string? FileName)
{
    public bool IsValid => Problem == ItemNameProblem.None;
}

/// <summary>A file an item template writes: its name within the folder, and its text.</summary>
public sealed record ItemFile(string FileName, string Content);

/// <summary>
/// One template of Add New Item.
///
/// Data, not code: the dialog, the tests and the docs all read the same list,
/// so a template added to the catalogue is offered, previewed, validated and
/// tested without touching the dialog.
/// </summary>
/// <param name="Id">Stable identifier, also the root of the template's localization keys.</param>
/// <param name="Icon">The name of an <c>IconKind</c> of the shell: this assembly does not reference it.</param>
/// <param name="NameKey">Localization key of the name shown in the list.</param>
/// <param name="DescriptionKey">Localization key of the one-sentence description.</param>
/// <param name="FileSuffixes">
/// What each file the template writes is named after the type name:
/// <c>[".vb", ".Designer.vb"]</c> for a Windows Form. The first is the one the
/// editor opens, and its extension is the one a typed name takes.
/// </param>
/// <param name="DefaultName">The name suggested, with its extension, as Visual Studio suggests Class1.vb.</param>
/// <param name="ForcedExtension">
/// Whether the first suffix is always the file's extension (a view is always
/// .vbhtml), or only the one a name typed without an extension takes (a
/// stylesheet may be .scss).
/// </param>
/// <param name="RequiredSuffix">A suffix the type name must carry for the framework to find it: a controller's "Controller".</param>
/// <param name="Generate">The files for a type name (the file name without its extension).</param>
/// <param name="AlternateExtensions">
/// For a template without a forced extension: the other extensions a typed
/// name may already carry and keep. Any other dot in the name is part of the
/// name ("Models.Customer" is Models.Customer.vb), not an extension.
/// </param>
/// <param name="BaseTypeNames">
/// The names of the classes the item inherits from. A type cannot be named
/// after its own base class (it would inherit itself), so these are refused.
/// </param>
public sealed record ItemTemplateDefinition(
    string Id,
    ItemCategory Category,
    string Icon,
    string NameKey,
    string DescriptionKey,
    string DefaultName,
    ItemNaming Naming,
    ItemRequirement Requires,
    IReadOnlyList<string> FileSuffixes,
    Func<string, IReadOnlyList<ItemFile>> Generate,
    bool ForcedExtension = true,
    string? RequiredSuffix = null,
    IReadOnlyList<string>? AlternateExtensions = null,
    IReadOnlyList<string>? BaseTypeNames = null)
{
    public string Name => Localizer.Get(NameKey);

    public string Description => Localizer.Get(DescriptionKey);

    /// <summary>The extension of the first file: ".vb", ".vbhtml".</summary>
    public string Extension => FileSuffixes[0];
}

/// <summary>
/// The rules around the catalogue: which templates exist, which fit a
/// project, what a typed name stands for and what a template writes.
///
/// The text of each template lives in the <c>ItemTemplateTexts</c> folder;
/// the list of templates is <see cref="ItemTemplateCatalog"/>. Nothing here
/// declares a Namespace in a file it writes: a new project's RootNamespace is
/// empty and the compiler places every type of a file at the root of the
/// project, which is what the other templates do too. Declaring one here
/// would double the project name in the type's full name.
/// </summary>
public static class ItemTemplates
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<ItemTemplateDefinition> All => ItemTemplateCatalog.All;

    public static ItemTemplateDefinition ById(string id) =>
        ItemTemplateCatalog.All.FirstOrDefault(t => t.Id == id)
        ?? throw new ArgumentException($"There is no item template '{id}'.", nameof(id));

    public static IEnumerable<ItemTemplateDefinition> InCategory(ItemCategory category) =>
        ItemTemplateCatalog.All.Where(t => t.Category == category);

    /// <summary>
    /// Whether the template needs System.Windows.Forms. Only a Windows Forms
    /// project can build such an item.
    /// </summary>
    public static bool NeedsWindowsForms(ItemTemplateDefinition template) =>
        template.Requires == ItemRequirement.WindowsForms;

    /// <summary>The name the dialog suggests for a template, as Visual Studio suggests Class1.vb.</summary>
    public static string DefaultName(ItemTemplateDefinition template) => template.DefaultName;

    /// <summary>
    /// Whether the template matches what was typed in the search box: every
    /// word of the query must be part of the name or the description, so
    /// "razor view" finds the MVC view and not every Razor item.
    /// </summary>
    public static bool Matches(ItemTemplateDefinition template, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return true;

        var name = template.Name;
        var description = template.Description;

        return words.All(word =>
            name.Contains(word, StringComparison.CurrentCultureIgnoreCase)
            || description.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// The requirement the project does not meet, or null when the template fits.
    /// Nothing is known of a folder that belongs to no project, so a template
    /// that needs more than a plain project is marked there.
    /// </summary>
    public static ItemRequirement? Misfit(ItemTemplateDefinition template, ItemProjectProfile profile) =>
        profile.Satisfies(template.Requires) ? null : template.Requires;

    /// <summary>
    /// The file name a typed name stands for, or why it cannot be one.
    ///
    /// A code template takes a Visual Basic type name, so the extension the
    /// user may have typed is taken off and put back, and the rest must be an
    /// identifier that is not a reserved word. An escaped name would have to
    /// be written [End] in every file, which the templates do not do, so a
    /// reserved word is refused instead.
    ///
    /// <paramref name="isReservedWord"/> is the compiler's answer, supplied by
    /// the caller: this assembly does not reference the Visual Basic compiler.
    /// </summary>
    public static ItemNameResult Check(ItemTemplateDefinition template, string typed, Func<string, bool> isReservedWord)
    {
        var name = typed.Trim();
        var extension = template.Extension;

        if (template.Naming == ItemNaming.FileName)
        {
            if (name.Length == 0) return new(ItemNameProblem.Empty, null);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return new(ItemNameProblem.InvalidFileName, null);

            // A name is never only an extension, and "." or ".." are not files.
            if (name.Trim('.').Length == 0) return new(ItemNameProblem.InvalidFileName, null);

            // A trailing dot is no extension and Windows drops it: "Foo." is Foo.
            name = name.TrimEnd('.');

            // The extension of a template that offers none (an empty file of any
            // kind) is whatever the user types, and nothing is added.
            if (extension.Length == 0) return new(ItemNameProblem.None, name);

            // Only the extension typed: there is no name in it.
            if (name.Equals(extension, StringComparison.OrdinalIgnoreCase)) return new(ItemNameProblem.Empty, null);

            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return new(ItemNameProblem.None, name);

            // Another extension of the same kind of file is kept ("index.htm");
            // any other dot belongs to the name ("Models.Customer" is a code file
            // called Models.Customer, not a file with a .Customer extension).
            if (!template.ForcedExtension
                && template.AlternateExtensions?.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) == true)
                return new(ItemNameProblem.None, name);

            return new(ItemNameProblem.None, name + extension);
        }

        if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            name = name[..^extension.Length];

        if (name.Length == 0) return new(ItemNameProblem.Empty, null);

        if (template.RequiredSuffix is { } suffix && !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name += suffix;

        if (!Identifier.IsMatch(name) || isReservedWord(name)) return new(ItemNameProblem.NotAnIdentifier, null);

        if (template.BaseTypeNames?.Contains(name, StringComparer.OrdinalIgnoreCase) == true)
            return new(ItemNameProblem.ShadowsBaseType, null);

        return new(ItemNameProblem.None, name + extension);
    }

    /// <summary>The file name a typed name stands for, or null when the typed name cannot be one.</summary>
    public static string? FileNameFor(ItemTemplateDefinition template, string typed, Func<string, bool> isReservedWord) =>
        Check(template, typed, isReservedWord).FileName;

    /// <summary>
    /// The files a template writes for a file name from <see cref="Check"/>.
    /// The first file is the one the editor opens; a Windows Form or
    /// UserControl adds its Designer.vb companion after it.
    /// </summary>
    public static IReadOnlyList<ItemFile> FilesFor(ItemTemplateDefinition template, string fileName)
    {
        // The type name is the file name without the extension the first
        // suffix stands for. A template that offers no extension keeps the
        // whole name: it is the file's, not a type's.
        var extension = template.Extension;
        var typeName = extension.Length > 0 && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^extension.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        if (!template.ForcedExtension && extension.Length > 0
            && !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            // An HTML page saved as .htm: the files keep the extension typed.
            return [.. template.Generate(typeName).Select((file, index) =>
                index == 0 ? file with { FileName = fileName } : file)];
        }

        return template.Generate(typeName);
    }

    /// <summary>
    /// The name to suggest for a template in a folder: its default name, or
    /// the first of Class2.vb, Class3.vb... that none of the template's files
    /// would collide with, as Visual Studio counts up.
    /// </summary>
    /// <param name="exists">Whether a file of that name is already in the folder.</param>
    public static string SuggestName(ItemTemplateDefinition template, Func<string, bool> exists)
    {
        // The file a typed name would really be written to: a controller typed
        // as "Home" is HomeController.vb, and that is the file to look for.
        string Written(string typed) => Check(template, typed, _ => false).FileName ?? typed;

        bool Taken(string typed) => FilesFor(template, Written(typed)).Any(file => exists(file.FileName));

        var candidate = template.DefaultName;
        if (!Taken(candidate)) return candidate;

        // Count up the name the user would type, not the file name: the suffix
        // a controller must carry stays at the end (Home2Controller), where
        // counting up the file name would put the number in front of it
        // (HomeController1Controller).
        var extension = template.Extension;
        var hasExtension = extension.Length > 0 && candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        var stem = hasExtension ? candidate[..^extension.Length] : candidate;

        if (template.RequiredSuffix is { } suffix && stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            stem = stem[..^suffix.Length];

        // Class1 is followed by Class2; a name without a number (HomeController,
        // App.config) has used up its first and is followed by 2.
        var digits = stem.Length - stem.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length;
        var number = digits > 0 ? int.Parse(stem[^digits..]) + 1 : 2;
        stem = stem[..^digits];

        // A bounded search: a folder cannot hold a thousand files of one template and still be sane.
        for (var attempts = 0; attempts < 1000; attempts++, number++)
        {
            var typed = stem + number + (hasExtension ? extension : "");
            if (!Taken(typed)) return Written(typed);
        }

        return template.DefaultName;
    }

    /// <summary>The localization key of a category's name.</summary>
    public static string CategoryKey(ItemCategory category) => "AddItem_Category_" + category;
}
