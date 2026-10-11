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
        "Stessa", "Ingrandisci", "Riduci",
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
    public void NoItalianIsLeftInAxamlFiles()
    {
        // Only attribute values (Content="…", Text="…"): an XML comment
        // written in Italian is a note to whoever maintains the style, not
        // something a user ever reads, and this heuristic would otherwise
        // flag Styles.axaml's own design comments.
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));

        var found = new List<string>();

        if (!Directory.Exists(root)) { Assert.Empty(found); return; }

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/")))
        {
            var text = File.ReadAllText(file);

            foreach (Match literal in Regex.Matches(text, "=\"([^\"]{2,})\""))
            {
                var value = literal.Groups[1].Value;

                if (!AxamlItalian.Any(word => value.Contains(word, StringComparison.Ordinal)))
                    continue;

                found.Add($"{Path.GetFileName(file)}: \"{value}\"");
            }
        }

        Assert.Empty(found);
    }

    /// <summary>
    /// <see cref="Italian"/> plus the words the welcome screen used before it
    /// was translated. Kept apart from <see cref="Italian"/> rather than
    /// merged into it: "Apri" alone is too short a fragment to trust against
    /// attribute values in general (binding paths, resource keys), so it is
    /// only checked here, against literal quoted text.
    /// </summary>
    private static readonly string[] AxamlItalian =
    [
        .. Italian,
        "Nuova soluzione", "Apri soluzione", "progetto",
    ];

    [Fact]
    public void NoItalianIsLeftInStringsResx()
    {
        // Strings.resx is the one place every user-visible string ultimately
        // comes from. A translated key here is translated everywhere; an
        // Italian one is Italian everywhere, including resource-key fallbacks
        // in the English culture that no satellite assembly overrides.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Basalt.Core", "Localization", "Strings.resx"));

        Assert.True(File.Exists(path), $"Strings.resx not found at {path}");

        var found = new List<string>();

        foreach (Match value in Regex.Matches(
            File.ReadAllText(path), "<value>([^<]*)</value>"))
        {
            var text = value.Groups[1].Value;

            foreach (var word in Italian)
            {
                if (text.Contains(word, StringComparison.Ordinal))
                    found.Add($"\"{text}\" (matched \"{word}\")");
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

    /// <summary>
    /// Where a user-visible string is written in the source: the property
    /// assignments a control takes its text from, the Output and status
    /// writers, the menu entries and the dialog prompts. Each captures the
    /// literal in group "text".
    /// </summary>
    private static readonly Regex[] UiLiteralSinks =
    [
        // Header = "…", Content = "…", Text = "…", Title = "…", ToolTip = "…", Watermark, PlaceholderText.
        new(@"\b(?:Header|Content|Text|Title|Watermark|PlaceholderText|ToolTip|Tooltip)\s*=\s*\$?""(?<text>[^""]*)""",
            RegexOptions.Compiled),
        // ToolTip.SetTip(control, "…"), WriteOutput("…"), AppendOutput("…"), Item(command, "…"), AskForTextAsync("…", …).
        new(@"(?:ToolTip\.SetTip\([^,]+,|\bWriteOutput\(|\bAppendOutput\(|\bItem\((?:[\w.]+,)?|\bAskFor\w*Async\(|\bStatusMessage\s*=)\s*\$?""(?<text>[^""]*)""",
            RegexOptions.Compiled),
        // new("…", () => …): a menu entry, as opposed to a font family or a resource key.
        new(@"\bnew\(\s*\$?""(?<text>[^""]*)""\s*,\s*(?:\(\)|Action\b)", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Literals the sinks match that are not prose a user reads. Each entry
    /// names the file and the exact text, and says why it stays a literal;
    /// anything else that reads as words must go through Strings.resx.
    /// </summary>
    private static readonly (string File, string Literal, string Reason)[] LiteralsThatStayInSource =
    [
        // The product name, shown as the brand rather than translated.
        ("AboutWindow.cs", "Basalt", "product name"),
        ("MainWindow.axaml", "Basalt", "product name"),
        // A Visual Basic type name that is the default for a new parameter.
        ("ChangeSignatureDialog.cs", "Integer", "Visual Basic type name, the default parameter type"),
        // SQL text the database panel puts into its query box.
        ("DatabasePanel.cs", "SELECT * FROM ", "SQL text, not interface text"),
        ("DatabasePanel.cs", "SELECT * FROM \\", "SQL text, not interface text"),
        // Visual Basic declaration syntax, "name As Type", in a parameter's label.
        ("ChangeSignatureDialog.cs", "{parameter.Name} As {parameter.Type}", "Visual Basic declaration syntax"),
        // The platform's name and version in the About text, not a translatable phrase.
        ("AboutWindow.cs", ".NET {Environment.Version}", "platform name and version"),
        // Sample Visual Basic expression shown as a placeholder.
        ("BreakpointConditionDialog.cs", "i = 4", "Visual Basic sample expression"),
        // A sample project name: a project name is an identifier, not prose.
        ("NewSolutionDialog.axaml", "MyApplication", "sample identifier"),
        ("NewSolutionDialog.axaml.cs", "MyApplication", "default project name, an identifier"),
    ];

    /// <summary>
    /// The shell files the literal check leaves alone: the WinForms designer
    /// surfaces, the settings, assistant, start page and Output panels, and
    /// the Razor glue. Another piece of work owns them, so they are not
    /// checked here until it lands.
    /// </summary>
    private static readonly string[] ExcludedFromLiteralCheck =
    [
        "WinFormsDesign", "WinFormsDesigner", "WinFormsComponentTray", "WinFormsPropertyPanel",
        "Settings", "Assistant", "StartPage", "OutputPanel", "Razor", "VbHtml",
    ];

    [Fact]
    public void NoUserVisibleLiteralIsWrittenInTheShellSource()
    {
        // The check the navigation test made for three files, applied to the
        // whole shell: a label, a menu entry, an Output sentence or a dialog
        // prompt written as a literal cannot be translated, and a Strings.resx
        // key that is missing shows the key itself. Comment lines are notes
        // for the maintainer and are not read.
        var found = new List<string>();

        foreach (var file in SourceFiles("Basalt.Shell")
                     .Where(f => !ExcludedFromLiteralCheck.Any(e => Path.GetFileName(f).Contains(e, StringComparison.Ordinal))))
        {
            var name = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

                foreach (var sink in UiLiteralSinks)
                foreach (Match match in sink.Matches(lines[i]))
                {
                    var literal = match.Groups["text"].Value;
                    if (!ReadsAsWords(literal)) continue;
                    if (LiteralsThatStayInSource.Any(a => a.File == name && a.Literal == literal)) continue;

                    found.Add($"{name}:{i + 1}: \"{literal}\"");
                }
            }
        }

        Assert.True(found.Count == 0,
            "User-visible literals that should be keys in Strings.resx:\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoUserVisibleLiteralIsWrittenInTheShellXaml()
    {
        // The same check for markup: Content="…", Title="…" and the rest as
        // attribute values. Bindings and markup extensions have a "{" and are
        // not literals; character references such as &#xE0A0; are not words.
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Basalt.Shell"));

        var attribute = new Regex(
            @"\b(?:Header|Content|Text|Title|Watermark|PlaceholderText|ToolTip\.Tip)=""(?<text>[^""{]*)""");

        var found = new List<string>();

        if (!Directory.Exists(root)) { Assert.Fail($"Basalt.Shell not found at {root}"); return; }

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))
                     .Where(f => !ExcludedFromLiteralCheck.Any(e => Path.GetFileName(f).Contains(e, StringComparison.Ordinal))))
        {
            var name = Path.GetFileName(file);

            foreach (Match match in attribute.Matches(File.ReadAllText(file)))
            {
                var literal = match.Groups["text"].Value;
                if (!ReadsAsWords(literal)) continue;
                if (LiteralsThatStayInSource.Any(a => a.File == name && a.Literal == literal)) continue;

                found.Add($"{name}: \"{literal}\"");
            }
        }

        Assert.True(found.Count == 0,
            "User-visible attribute literals that should be {l:LocalizedString} keys:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// True when the text has a run of two or more letters once interpolations,
    /// log tags such as "[test]" and character references are taken out. An
    /// interpolation the sink cut short at a nested quote has no closing brace,
    /// so it is taken to the end of the literal.
    /// </summary>
    private static bool ReadsAsWords(string literal)
    {
        var prose = Regex.Replace(literal, @"\{[^}]*\}?|\[[^\]]*\]|&#x?[0-9A-Fa-f]+;", " ");
        return Regex.IsMatch(prose, "[A-Za-z]{2,}");
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
