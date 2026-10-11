using System.Security;
using System.Text;

namespace Basalt.Vb6;

/// <summary>
/// Opens a Visual Basic 6 project by writing a .NET one beside it.
/// </summary>
/// <remarks>
/// The .frm and .bas files are left exactly where they are and are not
/// rewritten: they go into the new project as AdditionalFiles, the way a
/// .vbhtml does, and a generator turns them into Visual Basic during the
/// build. Someone opening a twenty-year-old project can still open it in
/// Visual Basic 6 afterwards, which they will want to do at least once to
/// check what we made of it.
/// </remarks>
public static class ProjectConversion
{
    /// <summary>What converting a project produced.</summary>
    public sealed record Result(
        string ProjectPath,
        IReadOnlyList<string> Sources,
        IReadOnlyList<string> MissingObjects);

    /// <summary>
    /// Writes a .vbproj for a .vbp, and reports what it could not bring over.
    /// </summary>
    public static Result Convert(string vbpPath, string? intoDirectory = null)
    {
        var directory = intoDirectory ?? Path.GetDirectoryName(vbpPath) ?? ".";
        var project = ProjectFile.Parse(File.ReadAllText(vbpPath));

        var sources = project.Sources
            .Where(file => File.Exists(Path.Combine(directory, file)))
            .ToList();

        var target = Path.Combine(directory, project.Name + ".vbproj");

        File.WriteAllText(target, Markup(project, sources));
        StartupModule.Write(project, directory);

        return new Result(target, sources, project.Objects);
    }

    private static string Markup(ProjectFile project, IReadOnlyList<string> sources)
    {
        var builder = new StringBuilder();

        builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        builder.AppendLine();
        builder.AppendLine("  <!--");
        builder.AppendLine($"    Written by Basalt from {project.Name}.vbp.");
        builder.AppendLine();
        builder.AppendLine("    The Visual Basic 6 sources are not compiled directly: they are");
        builder.AppendLine("    read by a generator during the build, which leaves the .frm and");
        builder.AppendLine("    .bas files exactly as Visual Basic 6 wrote them.");
        builder.AppendLine("  -->");
        builder.AppendLine();
        builder.AppendLine("  <PropertyGroup>");
        builder.AppendLine("    <OutputType>WinExe</OutputType>");
        builder.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        builder.AppendLine($"    <RootNamespace>{Identifier(project.Name)}</RootNamespace>");

        // Off, because Visual Basic 6 had no such thing: a project written
        // against it assigns freely between types and would not compile under
        // Option Strict at all.
        builder.AppendLine("    <OptionStrict>Off</OptionStrict>");
        builder.AppendLine("    <OptionExplicit>On</OptionExplicit>");

        // A form cannot be a startup object: it has no Sub Main. The module the
        // converter writes beside the project stands for VB6's startup form, and
        // is the entry point instead.
        //
        // Startup="Sub Main" writes no StartupObject at all. A StartupObject names
        // a type, and "Sub Main" is a procedure: naming it as Project.Sub Main
        // fails the build. Left unset, the compiler takes the one Sub Main in the
        // project, whichever module it is in; with two, it reports the ambiguity.
        if (StartupModule.StartsFromForm(project))
            builder.AppendLine($"    <StartupObject>{Identifier(project.Name)}.{StartupModule.Name}</StartupObject>");

        builder.AppendLine("  </PropertyGroup>");
        builder.AppendLine();

        // The forms are written against the runtime and compiled by the
        // generator, and neither is on NuGet: both come from the sources, the
        // same way the Razor template reaches its generator.
        builder.AppendLine("  <!--");
        builder.AppendLine("    The runtime the forms are written against, and the generator that");
        builder.AppendLine("    writes them. Point BasaltVb6Path at the src folder of Basalt if this");
        builder.AppendLine("    project is moved; an empty path is reported by the build.");
        builder.AppendLine("  -->");
        builder.AppendLine("  <PropertyGroup>");
        builder.AppendLine($"    <BasaltVb6Path>{SecurityElement.Escape(BasaltSourceFolder() ?? "")}</BasaltVb6Path>");
        builder.AppendLine("  </PropertyGroup>");
        builder.AppendLine();
        builder.AppendLine("  <ItemGroup>");
        builder.AppendLine("    <ProjectReference Include=\"$(BasaltVb6Path)/Basalt.Vb6/Basalt.Vb6.csproj\" />");
        builder.AppendLine("    <ProjectReference Include=\"$(BasaltVb6Path)/Basalt.Vb6.Generator/Basalt.Vb6.Generator.csproj\"");
        builder.AppendLine("                      OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />");
        builder.AppendLine("  </ItemGroup>");
        builder.AppendLine();
        builder.AppendLine("  <ItemGroup>");

        foreach (var source in sources)
            builder.AppendLine($"    <AdditionalFiles Include=\"{source}\" />");

        builder.AppendLine("  </ItemGroup>");

        if (project.Objects.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("  <!--");
            builder.AppendLine("    Controls this project used that came out of an OCX. They are");
            builder.AppendLine("    named in the forms and drawn as placeholders: the form opens");
            builder.AppendLine("    with the gap visible rather than not at all.");
            builder.AppendLine();

            foreach (var ocx in project.Objects)
                builder.AppendLine($"      {ocx}");

            builder.AppendLine("  -->");
        }

        builder.AppendLine();
        builder.AppendLine("</Project>");

        return builder.ToString();
    }

    /// <summary>
    /// The src folder that holds Basalt.Vb6 and its generator, found from where
    /// this build runs. Null when the build has no sources (an installed IDE),
    /// and the converted project then names the empty path in its build error.
    /// </summary>
    private static string? BasaltSourceFolder()
    {
        var directory = AppContext.BaseDirectory;

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "src", "Basalt.Vb6", "Basalt.Vb6.csproj")))
                return Path.Combine(directory, "src");

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    /// <summary>A project name as a namespace.</summary>
    private static string Identifier(string name)
    {
        var builder = new StringBuilder();

        foreach (var character in name)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');

        // A namespace cannot start with a digit, and a project called "2000"
        // is not unusual in code this old.
        return builder.Length > 0 && char.IsDigit(builder[0])
            ? "_" + builder
            : builder.ToString();
    }
}
