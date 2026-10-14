using Basalt.Workspace.Immediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// A .NET startup hook that holds a program at its start until a debugger is attached, so a program run in the
/// IDE's terminal (where it can be typed to) is debugged from its first line.
/// </summary>
/// <remarks>
/// <para>
/// netcoredbg launches a program with its own pipes and, under the Debug Adapter Protocol, passes nothing typed
/// on to it: Console.ReadLine waited for ever. Measured on 3.2.0 over stdio and over TCP. Attaching to a program
/// already running in a terminal works, input and breakpoints alike, but the program would be past its first
/// lines by then. Starting it suspended through the runtime's diagnostic port and attaching before resuming did
/// not: the attach succeeded and nothing more came, no module, no breakpoint.
/// </para>
/// <para>
/// So the program is started with DOTNET_STARTUP_HOOKS naming this hook, which runs before Main and waits until
/// Debugger.IsAttached, or a time limit passes. The hook is compiled here against the installed runtime the
/// program runs on, as the Immediate window compiles its lines, and kept per runtime version: a hook built for a
/// newer System.Runtime does not load on an older one.
/// </para>
/// </remarks>
public static class DebuggerWaitHook
{
    /// <summary>The variable that turns the wait on, holding the most seconds to wait.</summary>
    public const string WaitVariable = "BASALT_WAIT_FOR_DEBUGGER";

    /// <summary>The runtime's variable naming the startup hooks to run.</summary>
    public const string StartupHooksVariable = "DOTNET_STARTUP_HOOKS";

    /// <summary>The user's own startup hooks, put back for the processes the program starts.</summary>
    public const string UserHooksVariable = "BASALT_USER_STARTUP_HOOKS";

    private const string AssemblyName = "Basalt.DebuggerWait";

    /// <summary>
    /// The hook's source. The runtime looks for a type named StartupHook, outside any namespace, with a
    /// static Initialize. The variables are reset before waiting: a process the program starts inherits its
    /// environment, and must not wait for a debugger nobody will attach, but keeps the user's own hooks.
    /// </summary>
    internal const string Source = """
        Friend Class StartupHook
            Public Shared Sub Initialize()
                Dim seconds As Integer
                If Not Integer.TryParse(System.Environment.GetEnvironmentVariable("BASALT_WAIT_FOR_DEBUGGER"), seconds) Then Return

                Dim userHooks As String = System.Environment.GetEnvironmentVariable("BASALT_USER_STARTUP_HOOKS")
                System.Environment.SetEnvironmentVariable("BASALT_WAIT_FOR_DEBUGGER", Nothing)
                System.Environment.SetEnvironmentVariable("BASALT_USER_STARTUP_HOOKS", Nothing)
                System.Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", If(String.IsNullOrEmpty(userHooks), Nothing, userHooks))

                Dim deadline As System.DateTime = System.DateTime.UtcNow.AddSeconds(seconds)

                Do While Not System.Diagnostics.Debugger.IsAttached AndAlso System.DateTime.UtcNow < deadline
                    System.Threading.Thread.Sleep(10)
                Loop
            End Sub
        End Class
        """;

    /// <summary>A short digest of <see cref="Source"/>, naming the folder its compiled form is kept in.</summary>
    private static readonly string SourceFingerprint = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Source)))[..12];

    /// <summary>Where the compiled hooks are kept, one folder per runtime version.</summary>
    public static string DefaultCacheFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".basalt", "debugger-wait");

    /// <summary>
    /// The hook compiled for a runtime, compiling it the first time. Throws when the compiler refuses it, which
    /// would be a defect here, not in the user's program.
    /// </summary>
    public static string EnsureFor(InstalledRuntime runtime, string? cacheFolder = null)
    {
        // Keyed by the source too, so a change to the hook is compiled again rather than read from an old copy.
        var folder = Path.Combine(cacheFolder ?? DefaultCacheFolder, runtime.Version + "-" + SourceFingerprint);
        var path = Path.Combine(folder, AssemblyName + ".dll");

        if (File.Exists(path)) return path;

        Directory.CreateDirectory(folder);

        var references = Directory.EnumerateFiles(runtime.Folder, "*.dll")
            .Where(IsManaged)
            .Select(file => (MetadataReference)MetadataReference.CreateFromFile(file));

        var compilation = VisualBasicCompilation.Create(
            AssemblyName,
            [VisualBasicSyntaxTree.ParseText(Source)],
            references,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, rootNamespace: ""));

        // Written beside, then moved: two IDEs starting a program at once must not load a half-written file.
        var temporary = Path.Combine(folder, $"{AssemblyName}.{Guid.NewGuid():N}.tmp");

        using (var output = File.Create(temporary))
        {
            var result = compilation.Emit(output);

            if (!result.Success)
            {
                output.Close();
                File.Delete(temporary);
                throw new InvalidOperationException(
                    "The debugger wait hook did not compile: " + string.Join("; ", ImmediateProgram.ErrorsOf(result.Diagnostics)));
            }
        }

        try
        {
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another start compiled it first; theirs is the same.
            File.Delete(temporary);
        }

        return path;
    }

    /// <summary>
    /// The environment that makes a program wait for the debugger, joined to any hooks the user's own
    /// environment already names.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EnvironmentFor(string hookPath, int waitSeconds, string? existingHooks)
    {
        var hooks = string.IsNullOrEmpty(existingHooks) ? hookPath : hookPath + Path.PathSeparator + existingHooks;

        var environment = new Dictionary<string, string>
        {
            [StartupHooksVariable] = hooks,
            [WaitVariable] = waitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(existingHooks)) environment[UserHooksVariable] = existingHooks;

        return environment;
    }

    private static bool IsManaged(string file)
    {
        try
        {
            System.Reflection.AssemblyName.GetAssemblyName(file);
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException)
        {
            return false;
        }
    }
}
