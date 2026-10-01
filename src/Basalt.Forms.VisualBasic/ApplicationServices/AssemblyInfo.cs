using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// The assembly attributes and process facts My.Application.Info exposes to a
/// VB application: title, company, version and the assemblies currently loaded.
/// </summary>
public class AssemblyInfo
{
    private readonly Assembly _assembly;

    public AssemblyInfo(Assembly currentAssembly)
    {
        ArgumentNullException.ThrowIfNull(currentAssembly);

        _assembly = currentAssembly;
    }

    public string AssemblyName => _assembly.GetName().Name ?? string.Empty;

    public string CompanyName => _assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

    public string Copyright => _assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    public string Description => _assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;

    public string DirectoryPath => Path.GetDirectoryName(_assembly.Location) ?? string.Empty;

    /// <summary>Every assembly loaded in the current process, not only this one.</summary>
    public ReadOnlyCollection<Assembly> LoadedAssemblies => new(AppDomain.CurrentDomain.GetAssemblies());

    public string ProductName => _assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? string.Empty;

    public string StackTrace => Environment.StackTrace;

    public string Title => _assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? AssemblyName;

    public string Trademark => _assembly.GetCustomAttribute<AssemblyTrademarkAttribute>()?.Trademark ?? string.Empty;

    public Version Version => _assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>The current process's working set: the same number Task Manager shows for it.</summary>
    public long WorkingSet => Process.GetCurrentProcess().WorkingSet64;
}
