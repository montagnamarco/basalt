namespace Basalt.Vb6;

/// <summary>
/// The startup module a project needs when it starts from a form.
/// </summary>
/// <remarks>
/// Visual Basic 6 had no Sub Main to write for a form. When a .vbp said
/// Startup="Form1", the program loaded Form1 and showed it, and that was the
/// whole of its startup. A .NET project starts from a Sub Main, so the
/// converter writes one: a module that stands for the startup form and hands
/// it to the runtime. The .vbp, the .frm and the .bas files are untouched; this
/// file is the only thing added, and it is written again on every conversion.
/// </remarks>
public static class StartupModule
{
    /// <summary>The file the module is written to, beside the .vbproj.</summary>
    public const string FileName = "Vb6Startup.vb";

    /// <summary>The module's name, which the .vbproj gives as its StartupObject.</summary>
    public const string Name = "Vb6Startup";

    /// <summary>
    /// The first line of every module this converter writes. A file that does
    /// not begin with it was not written here, and is never deleted.
    /// </summary>
    private const string Marker =
        "' Written by Basalt from the startup form of a Visual Basic 6 project.";

    /// <summary>
    /// Whether the project starts from a form. Startup="Sub Main" names a
    /// procedure the project already has, so nothing is generated for it.
    /// </summary>
    public static bool StartsFromForm(ProjectFile project) =>
        project.Startup is { Length: > 0 } startup
        && !string.Equals(startup, "Sub Main", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the module beside the project when it starts from a form. When
    /// it does not, a module an earlier conversion wrote is removed: it would
    /// be a second entry point beside the project's own Sub Main.
    /// </summary>
    public static void Write(ProjectFile project, string directory)
    {
        var path = Path.Combine(directory, FileName);

        if (project.Startup is { } startup && StartsFromForm(project))
        {
            File.WriteAllText(path, Text(startup));
            return;
        }

        if (File.Exists(path) && File.ReadAllText(path).StartsWith(Marker, StringComparison.Ordinal))
            File.Delete(path);
    }

    /// <summary>The module, for the form the project starts from.</summary>
    public static string Text(string form) => $"""
        {Marker}
        '
        ' Visual Basic 6 had no Sub Main for a project that starts from a form. When
        ' the program ran it loaded {form}, showed it, and ran until that form closed.
        ' This module stands for that startup form, so the .NET project has an entry
        ' point that does the same thing.
        '
        ' It is written again on every conversion, so change the .vbp rather than this file.

        Imports Basalt.Vb6.Runtime

        Module {Name}

            <STAThread>
            Public Sub Main(args As String())
                ' {form} is created on the UI thread once Avalonia is running, and
                ' Main returns when it has closed.
                Vb6Application.Run(Of {form})(Function() New {form}(), args)
            End Sub

        End Module

        """;
}
