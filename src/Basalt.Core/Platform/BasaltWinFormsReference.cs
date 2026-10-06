using System.Reflection;

namespace Basalt.Core.Platform;

/// <summary>
/// The NuGet packages that carry Basalt.WinForms and Basalt.Drawing, as the IDE
/// ships them: two .nupkg files beside the application, so nothing needs to be
/// published to nuget.org for a project to restore them.
/// </summary>
/// <param name="Version">The version of both packages (the IDE's own WinFormsVersion).</param>
/// <param name="Source">The folder that holds the .nupkg files, a NuGet package source.</param>
public sealed record BasaltWinFormsPackages(string Version, string Source)
{
    /// <summary>The package a project references.</summary>
    public const string PackageId = "Basalt.WinForms";

    /// <summary>Environment variable that points at another folder of .nupkg files.</summary>
    public const string SourceVariable = "BASALT_WINFORMS_PACKAGES";

    /// <summary>
    /// The version the IDE was built with, stamped into Basalt.Core from
    /// <c>$(WinFormsVersion)</c> (Directory.Build.props) as assembly metadata, so
    /// the project the IDE writes and the packages it ships cannot disagree.
    /// </summary>
    public static string BuiltVersion { get; } =
        typeof(BasaltWinFormsPackages).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BasaltWinFormsVersion")?.Value ?? "";

    /// <summary>
    /// The shipped packages when both files exist, otherwise null (an IDE built
    /// before <c>build/pack.sh --winforms-only</c> ran, so there was nothing to
    /// copy; <see cref="SourceVariable"/> can then point at a folder that has them).
    /// </summary>
    public static BasaltWinFormsPackages? TryFind()
    {
        var configured = Environment.GetEnvironmentVariable(SourceVariable);
        var source = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "WinFormsPackages")
            : configured;
        return TryFind(source, BuiltVersion);
    }

    /// <summary>Like <see cref="TryFind()"/>, for a given folder and version.</summary>
    public static BasaltWinFormsPackages? TryFind(string source, string version)
    {
        if (string.IsNullOrEmpty(version)) return null;
        var present = File.Exists(Path.Combine(source, $"{PackageId}.{version}.nupkg"))
            && File.Exists(Path.Combine(source, $"Basalt.Drawing.{version}.nupkg"));
        return present ? new BasaltWinFormsPackages(version, Path.GetFullPath(source)) : null;
    }
}

/// <summary>
/// How a project that uses Basalt.WinForms gets it. The one place that decides,
/// for the WinForms solution template and for the retargeting of Visual Studio
/// projects:
/// <list type="bullet">
/// <item>the Basalt sources are available (development): project references, as
/// before, so a change to Basalt.Forms is picked up by the next build;</item>
/// <item>otherwise the shipped packages: a PackageReference, restored from the
/// folder the IDE passes as <c>RestoreAdditionalProjectSources</c>;</item>
/// <item>neither: nothing to reference.</item>
/// </list>
/// </summary>
public sealed record BasaltWinFormsReference(string? SourceRoot, BasaltWinFormsPackages? Packages)
{
    /// <summary>Project references onto the sources, which win when both exist.</summary>
    public bool UsesSources => SourceRoot is not null;

    /// <summary>A package reference to the shipped packages.</summary>
    public bool UsesPackages => SourceRoot is null && Packages is not null;

    /// <summary>Neither the sources nor the packages are available.</summary>
    public bool IsAvailable => UsesSources || UsesPackages;

    /// <summary>The decision for this machine.</summary>
    public static BasaltWinFormsReference Current() =>
        Resolve(BasaltSourceRoot.TryFind(), BasaltWinFormsPackages.TryFind());

    /// <summary>Folders that do not hold Basalt.Forms are no source root.</summary>
    public static BasaltWinFormsReference Resolve(string? sourceRoot, BasaltWinFormsPackages? packages)
    {
        var hasSources = !string.IsNullOrWhiteSpace(sourceRoot)
            && File.Exists(Path.Combine(sourceRoot, "Basalt.Forms", "Basalt.Forms.csproj"));
        return new BasaltWinFormsReference(hasSources ? Path.GetFullPath(sourceRoot!) : null, packages);
    }

    /// <summary>Throws when neither the sources nor the packages exist, saying how to fix it.</summary>
    public BasaltWinFormsReference Require() =>
        IsAvailable
            ? this
            : throw new DirectoryNotFoundException(
                $"Basalt.WinForms is not available: set {BasaltSourceRoot.EnvironmentVariable} to the folder " +
                $"containing the Basalt.Forms source project, or {BasaltWinFormsPackages.SourceVariable} to a folder " +
                $"with the {BasaltWinFormsPackages.PackageId} and Basalt.Drawing .nupkg files.");
}
