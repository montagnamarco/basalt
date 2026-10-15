using System.Diagnostics;

namespace Basalt.Tests;

/// <summary>
/// Small Visual Basic programs, built with the dotnet CLI, that the live debugger tests start, attach to or run
/// under the debugger. Debug symbols are what the adapter maps lines through, so a target has to be built.
/// </summary>
internal static class NetCoreDebugTargets
{
    public const string ExeProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace></RootNamespace>
            <DebugType>portable</DebugType>
          </PropertyGroup>
        </Project>
        """;

    /// <summary>
    /// Runs until killed, appending a line to the file named by its first argument every 200 ms: that file growing is
    /// the proof that the program is running. The breakpoint line is 7.
    /// </summary>
    public const string Looper = """
        Module Program
            Sub Main(args As String())
                Dim path As String = args(0)
                Dim i As Integer = 0
                While True
                    i += 1
                    IO.File.AppendAllText(path, i & vbLf)
                    Threading.Thread.Sleep(200)
                End While
            End Sub
        End Module
        """;

    public const int LooperBreakpointLine = 7;

    /// <summary>Catches an exception it throws itself, on line 5, and prints "done".</summary>
    public const string CatchingThrower = """
        Module Program
            Sub Main()
                Console.WriteLine("before")
                Try
                    Throw New InvalidOperationException("boom handled")
                Catch ex As InvalidOperationException
                    Console.WriteLine("caught " & ex.Message)
                End Try
                Console.WriteLine("basalt-finished")
            End Sub
        End Module
        """;

    public const int CatchingThrowLine = 5;

    /// <summary>
    /// Writes its arguments and one environment variable to the console. Line 3 is where the values are known.
    /// </summary>
    public const string EchoArguments = """
        Module Program
            Sub Main(args As String())
                Console.WriteLine("basalt-args=[" & String.Join("|", args) & "]")
                Console.WriteLine("basalt-env=[" & Environment.GetEnvironmentVariable("BASALT_PROBE") & "]")
                Console.WriteLine("basalt-cwd=[" & IO.Path.GetFileName(Environment.CurrentDirectory) & "]")
            End Sub
        End Module
        """;

    /// <summary>Writes the project and its source, builds it, and returns the paths.</summary>
    public static async Task<(string Project, string Source, string Assembly)> BuildAsync(
        string root, string name, string source, string project = ExeProject)
    {
        Directory.CreateDirectory(root);

        var projectPath = Path.Combine(root, name + ".vbproj");
        var sourcePath = Path.Combine(root, "Program.vb");

        await File.WriteAllTextAsync(projectPath, project);
        await File.WriteAllTextAsync(sourcePath, source);

        await BuildProjectAsync(projectPath);

        return (projectPath, sourcePath, Path.Combine(root, "bin", "Debug", "net10.0", name + ".dll"));
    }

    public static async Task BuildProjectAsync(string projectPath)
    {
        using var build = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "build", projectPath, "-c", "Debug", "--nologo", "-v", "q" },
            WorkingDirectory = Path.GetDirectoryName(projectPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        var output = build.StandardOutput.ReadToEndAsync();
        var errors = build.StandardError.ReadToEndAsync();

        await build.WaitForExitAsync();

        Assert.True(build.ExitCode == 0, "The target did not build:\n" + await output + await errors);
    }
}
