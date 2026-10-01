using System.Globalization;
using System.Reflection;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// The root of My.Application: the running assembly's information and the
/// culture the application reads and writes dates, numbers and text with.
/// </summary>
/// <remarks>
/// Info.Log (My.Application.Log) is not implemented here: it returns
/// Microsoft.VisualBasic.Logging.Log, a type this task does not own and that
/// does not exist yet in Basalt.Forms.VisualBasic, so the property stays out
/// rather than being stubbed against a type that is not there.
/// </remarks>
public class ApplicationBase
{
    public ApplicationBase()
    {
        Info = new AssemblyInfo(Assembly.GetEntryAssembly() ?? GetType().Assembly);
    }

    public CultureInfo Culture => CultureInfo.CurrentCulture;

    public AssemblyInfo Info { get; }

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
