using System.Globalization;
using System.Reflection;
using Microsoft.VisualBasic.Logging;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// The root of My.Application: the running assembly's information and the
/// culture the application reads and writes dates, numbers and text with.
/// </summary>
public class ApplicationBase
{
    private Log? _log;

    public ApplicationBase()
    {
        Info = new AssemblyInfo(Assembly.GetEntryAssembly() ?? GetType().Assembly);
    }

    public CultureInfo Culture => CultureInfo.CurrentCulture;

    public AssemblyInfo Info { get; }

    /// <summary>My.Application.Log: lazily created with this application's assembly name as its TraceSource name, so every call shares the one default file listener.</summary>
    public Log Log => _log ??= new Log(Info.AssemblyName);

    public CultureInfo UICulture => CultureInfo.CurrentUICulture;

    public void ChangeCulture(string cultureName) => CultureInfo.CurrentCulture = new CultureInfo(cultureName);

    public void ChangeUICulture(string cultureName) => CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

    /// <summary>As VB's Environ function: throws when the variable is not set, rather than returning null.</summary>
    public string GetEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (value is null) throw new ArgumentException($"Environment variable '{name}' is not found.", nameof(name));

        return value;
    }
}
