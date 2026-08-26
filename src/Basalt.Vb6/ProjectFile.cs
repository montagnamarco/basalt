namespace Basalt.Vb6;

/// <summary>
/// A Visual Basic 6 project, read from its .vbp file.
/// </summary>
/// <remarks>
/// The format is a plain list of key=value lines, with a key repeated once per
/// file: Form=Form1.frm, Module=Modulo1; Modulo1.bas. Reading it needs nothing
/// but the file, which is what lets a project be opened on a machine that has
/// never had Visual Basic on it.
/// </remarks>
public sealed class ProjectFile
{
    private ProjectFile(
        string name,
        string? startup,
        IReadOnlyList<string> forms,
        IReadOnlyList<string> modules,
        IReadOnlyList<string> classes,
        IReadOnlyList<string> objects,
        IReadOnlyList<string> references)
    {
        Name = name;
        Startup = startup;
        Forms = forms;
        Modules = modules;
        Classes = classes;
        Objects = objects;
        References = references;
    }

    public string Name { get; }

    /// <summary>The form or procedure the project starts from.</summary>
    public string? Startup { get; }

    public IReadOnlyList<string> Forms { get; }
    public IReadOnlyList<string> Modules { get; }
    public IReadOnlyList<string> Classes { get; }

    /// <summary>The OCX files the project says it needs.</summary>
    public IReadOnlyList<string> Objects { get; }

    /// <summary>The type libraries it references.</summary>
    public IReadOnlyList<string> References { get; }

    /// <summary>Every source file, in the order the project lists them.</summary>
    public IEnumerable<string> Sources => Forms.Concat(Modules).Concat(Classes);

    public static ProjectFile Parse(string text)
    {
        var forms = new List<string>();
        var modules = new List<string>();
        var classes = new List<string>();
        var objects = new List<string>();
        var references = new List<string>();

        var name = "Project1";
        string? startup = null;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var at = line.IndexOf('=');

            if (at <= 0) continue;

            var key = line.Substring(0, at).Trim();
            var value = line.Substring(at + 1).Trim();

            switch (key)
            {
                case "Form":
                    forms.Add(FileIn(value));
                    break;

                // Written as "Name; File.bas", because Visual Basic keeps the
                // module's own name beside the path.
                case "Module":
                    modules.Add(FileIn(value));
                    break;

                case "Class":
                    classes.Add(FileIn(value));
                    break;

                case "Object":
                    objects.Add(FileIn(value));
                    break;

                case "Reference":
                    references.Add(value);
                    break;

                case "Name":
                    name = Unquoted(value);
                    break;

                case "Startup":
                    startup = Unquoted(value);
                    break;
            }
        }

        return new ProjectFile(name, startup, forms, modules, classes, objects, references);
    }

    /// <summary>The file a value names, after any name before the semicolon.</summary>
    private static string FileIn(string value)
    {
        var semicolon = value.LastIndexOf(';');

        return Unquoted(semicolon >= 0 ? value.Substring(semicolon + 1) : value).Trim();
    }

    private static string Unquoted(string value)
    {
        value = value.Trim();

        return value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"'
            ? value.Substring(1, value.Length - 2)
            : value;
    }
}
