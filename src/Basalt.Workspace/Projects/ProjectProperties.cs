namespace Basalt.Workspace.Projects;

/// <summary>How strictly Visual Basic checks types.</summary>
public enum OptionStrict { Off, On, Custom }

/// <summary>How Visual Basic compares strings.</summary>
public enum OptionCompare { Binary, Text }

/// <summary>What a project produces.</summary>
public enum ProjectOutputType { Library, Exe, WinExe }

/// <summary>
/// The settings of a project, as the properties window shows them.
///
/// Reading and writing go through <see cref="VbProjectFile"/>, so a value the
/// window does not know about is left alone rather than dropped.
/// </summary>
public sealed class ProjectProperties
{
    private readonly VbProjectFile _file;

    public ProjectProperties(VbProjectFile file) => _file = file;

    public static ProjectProperties Load(string projectPath) =>
        new(VbProjectFile.Load(projectPath));

    public Task SaveAsync(CancellationToken ct = default) => _file.SaveAsync(ct);

    public string Path => _file.Path;

    // Application

    public string AssemblyName
    {
        get => _file.GetProperty("AssemblyName")
               ?? System.IO.Path.GetFileNameWithoutExtension(_file.Path);
        set => _file.SetProperty("AssemblyName", value);
    }

    /// <summary>
    /// Namespace prefixed to every declared namespace.
    ///
    /// An empty value is meaningful in Visual Basic and must be written rather
    /// than removed: without it the compiler prepends the project name, which
    /// turns `Namespace App` into `App.App`.
    /// </summary>
    public string RootNamespace
    {
        get => _file.GetProperty("RootNamespace") ?? "";
        set => _file.SetPropertyAllowingEmpty("RootNamespace", value);
    }

    public ProjectOutputType OutputType
    {
        get => _file.GetProperty("OutputType") switch
        {
            "Exe" => ProjectOutputType.Exe,
            "WinExe" => ProjectOutputType.WinExe,
            _ => ProjectOutputType.Library
        };
        set => _file.SetProperty("OutputType", value.ToString());
    }

    /// <summary>Type whose Main runs at startup, when several could.</summary>
    public string? StartupObject
    {
        get => _file.GetProperty("StartupObject");
        set => _file.SetProperty("StartupObject", value);
    }

    // Compilation

    public string TargetFramework
    {
        get => _file.GetProperty("TargetFramework") ?? "net10.0";
        set => _file.SetProperty("TargetFramework", value);
    }

    public OptionStrict OptionStrict
    {
        get => _file.GetProperty("OptionStrict") switch
        {
            "On" => OptionStrict.On,
            "Custom" => OptionStrict.Custom,
            _ => OptionStrict.Off
        };
        set => _file.SetProperty("OptionStrict", value.ToString());
    }

    public bool OptionExplicit
    {
        get => !string.Equals(_file.GetProperty("OptionExplicit"), "Off",
            StringComparison.OrdinalIgnoreCase);
        set => _file.SetProperty("OptionExplicit", value ? "On" : "Off");
    }

    public bool OptionInfer
    {
        get => !string.Equals(_file.GetProperty("OptionInfer"), "Off",
            StringComparison.OrdinalIgnoreCase);
        set => _file.SetProperty("OptionInfer", value ? "On" : "Off");
    }

    public OptionCompare OptionCompare
    {
        get => string.Equals(_file.GetProperty("OptionCompare"), "Text",
            StringComparison.OrdinalIgnoreCase)
            ? OptionCompare.Text
            : OptionCompare.Binary;
        set => _file.SetProperty("OptionCompare", value.ToString());
    }

    // Advanced compilation

    /// <summary>Symbols defined for conditional compilation.</summary>
    public string? DefineConstants
    {
        get => _file.GetProperty("DefineConstants");
        set => _file.SetProperty("DefineConstants", value);
    }

    public bool TreatWarningsAsErrors
    {
        get => string.Equals(_file.GetProperty("TreatWarningsAsErrors"), "true",
            StringComparison.OrdinalIgnoreCase);
        set => _file.SetProperty("TreatWarningsAsErrors", value ? "true" : null);
    }

    /// <summary>Warning numbers not to report.</summary>
    public string? NoWarn
    {
        get => _file.GetProperty("NoWarn");
        set => _file.SetProperty("NoWarn", value);
    }

    public bool GenerateDocumentationFile
    {
        get => string.Equals(_file.GetProperty("GenerateDocumentationFile"), "true",
            StringComparison.OrdinalIgnoreCase);
        set => _file.SetProperty("GenerateDocumentationFile", value ? "true" : null);
    }

    public bool Nullable
    {
        get => string.Equals(_file.GetProperty("Nullable"), "enable",
            StringComparison.OrdinalIgnoreCase);
        set => _file.SetProperty("Nullable", value ? "enable" : null);
    }

    // Publishing

    /// <summary>Runtime the output is built for, when one is fixed.</summary>
    public string? RuntimeIdentifier
    {
        get => _file.GetProperty("RuntimeIdentifier");
        set => _file.SetProperty("RuntimeIdentifier", value);
    }

    public bool SelfContained
    {
        get => string.Equals(_file.GetProperty("SelfContained"), "true",
            StringComparison.OrdinalIgnoreCase);
        set => _file.SetProperty("SelfContained", value ? "true" : null);
    }

    public bool PublishTrimmed
    {
        get => string.Equals(_file.GetProperty("PublishTrimmed"), "true",
            StringComparison.OrdinalIgnoreCase);
        set => _file.SetProperty("PublishTrimmed", value ? "true" : null);
    }

    // References

    public IReadOnlyList<(string Id, string? Version)> PackageReferences =>
        _file.GetPackageReferences();

    public IReadOnlyList<string> ProjectReferences => _file.GetProjectReferences();

    public void AddPackage(string id, string version) => _file.AddPackageReference(id, version);

    public bool RemovePackage(string id) => _file.RemovePackageReference(id);

    public void AddProject(string relativePath) => _file.AddProjectReference(relativePath);

    public bool RemoveProject(string relativePath) => _file.RemoveProjectReference(relativePath);

    /// <summary>The file as it now stands, for previewing before saving.</summary>
    public string ToXml() => _file.ToXml();
}
