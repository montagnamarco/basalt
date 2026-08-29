using System.Reflection;
using System.Text.RegularExpressions;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;

namespace Basalt.Tests;

/// <summary>
/// The interface speaks English, and says so from one place.
/// </summary>
/// <remarks>
/// The IDE was written with Italian text scattered through the code: a string
/// in the source cannot be translated, and half the interface being in one
/// language and half in another is worse than either.
/// </remarks>
public class EnglishStringsTests
{
    /// <summary>Words that only appear in Italian, and only as user-visible text.</summary>
    private static readonly string[] Italian =
    [
        "Taglia", "Copia", "Incolla", "Elimina", "Griglia", "Righe", "Colonne",
        "Pulsante", "Etichetta", "Casella", "Pannello", "Trascina", "Sposta",
        "Aggiungi", "Seleziona", "Allinea", "Adatta", "Dimensione reale",
        "Struttura", "Voce di menu", "Barra di stato", "Espansore", "Bordo",
    ];

    private static IEnumerable<string> SourceFiles(string project)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", project));

        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))
            : [];
    }

    [Fact]
    public void NoAccentedTextIsLeftAnywhereInTheInterface()
    {
        // A word list only catches the words on it: three Italian messages
        // survived the first pass because "Esiste", "Nessun" and "Verra" were
        // not on mine. Accented letters do not appear in English, so they
        // find what a vocabulary misses — and they cost nothing to check.
        var found = new List<string>();

        foreach (var project in new[] { "Basalt.Shell", "Basalt.Designer", "Basalt.Core" })
        {
            foreach (var file in SourceFiles(project))
            {
                foreach (Match literal in Regex.Matches(
                    File.ReadAllText(file), "\"([^\"\\\\\n]{2,})\""))
                {
                    var value = literal.Groups[1].Value;

                    if (value.Any(c => "àèéìòùÀÈÉÌÒÙ".Contains(c)))
                        found.Add($"{Path.GetFileName(file)}: \"{value}\"");
                }
            }
        }

        Assert.Empty(found);
    }

    [Fact]
    public void NoItalianIsLeftInTheDesignerOrTheShell()
    {
        // Only string literals: a comment in Italian is a note to whoever
        // maintains this, not something a user ever reads.
        var found = new List<string>();

        foreach (var project in new[] { "Basalt.Shell", "Basalt.Designer" })
        {
            foreach (var file in SourceFiles(project))
            {
                var text = File.ReadAllText(file);

                foreach (Match literal in Regex.Matches(text, "\"([^\"\\\\\n]{2,})\""))
                {
                    var value = literal.Groups[1].Value;

                    if (!Italian.Any(word => value.Contains(word, StringComparison.Ordinal)))
                        continue;

                    // A resource key names the string; it is not the string.
                    if (value.StartsWith("Designer_") || value.StartsWith("Tool_")) continue;

                    found.Add($"{Path.GetFileName(file)}: \"{value}\"");
                }
            }
        }

        Assert.Empty(found);
    }

    [Fact]
    public void EveryToolboxControlIsNamedInEnglish()
    {
        var italian = ToolboxCatalog.Items
            .Where(item => Italian.Any(word =>
                item.DisplayName.Contains(word, StringComparison.Ordinal)))
            .Select(item => item.DisplayName)
            .ToList();

        Assert.Empty(italian);
    }

    [Fact]
    public void EveryToolboxCategoryIsNamedInEnglish()
    {
        Assert.Equal(
            ["Layout", "Common", "Lists", "Menu"],
            ToolboxCatalog.Items.Select(i => i.Category).Distinct().ToList());
    }

    [Fact]
    public void EveryKeyResolvesToSomethingOtherThanItself()
    {
        // Localizer.Get returns the key when nothing matches, so a missing
        // entry shows up in the interface as "Designer_Cut" rather than as an
        // error anyone would notice.
        var missing = typeof(StringKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(key => Localizer.Get(key) == key)
            .ToList();

        Assert.Empty(missing);
    }
}
