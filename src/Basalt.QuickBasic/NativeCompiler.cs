using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Basalt.QuickBasic;

/// <summary>What to build, and for which machine.</summary>
public sealed record CompilationRequest(
    string Source,
    string OutputPath)
{
    /// <summary>The architecture to build for; null means this machine's.</summary>
    public string? Architecture { get; init; }

    /// <summary>The operating system to build for; null means this one.</summary>
    public string? OperatingSystem { get; init; }

    /// <summary>Where the headers and libraries of the target live, when cross-compiling.</summary>
    public string? Sysroot { get; init; }

    /// <summary>Whether to keep the generated C beside the binary.</summary>
    public bool KeepIntermediate { get; init; }
}

/// <summary>What came of a build.</summary>
public sealed record CompilationOutcome(
    bool Succeeded,
    string? OutputPath,
    IReadOnlyList<Diagnostic> Diagnostics,
    string CompilerOutput)
{
    /// <summary>The generated C, kept for looking at when asked for.</summary>
    public string? GeneratedCode { get; init; }
}

/// <summary>
/// Compiles QuickBASIC to a native executable.
///
/// The route is QuickBASIC to C to machine code, with clang doing the second
/// half. That buys every architecture clang supports and a real optimiser,
/// for the cost of writing text rather than an instruction selector.
/// </summary>
public sealed class NativeCompiler
{
    private readonly string _clang;

    public NativeCompiler(string? clangPath = null) =>
        _clang = clangPath ?? "clang";

    /// <summary>Whether a C compiler can be found.</summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(_clang, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });

                if (process is null) return false;

                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>The architecture this machine runs.</summary>
    public static string HostArchitecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X64 => "x86_64",
        _ => "x86_64"
    };

    /// <summary>Translates a program to C without compiling it.</summary>
    public static string ToC(string source, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var program = Parser.Parse(source);
        var symbols = SymbolTable.Build(program);

        diagnostics = [.. program.Diagnostics, .. symbols.Diagnostics];

        return CodeWriter.Generate(program, symbols);
    }

    public async Task<CompilationOutcome> CompileAsync(
        CompilationRequest request, CancellationToken ct = default)
    {
        var program = Parser.Parse(request.Source);
        var symbols = SymbolTable.Build(program);

        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(program.Diagnostics);
        diagnostics.AddRange(symbols.Diagnostics);

        // Generating code from a program known to be wrong would produce C
        // that fails to compile, and the user would read clang's complaint
        // about generated code instead of ours about theirs.
        if (diagnostics.Count > 0)
            return new CompilationOutcome(false, null, diagnostics, "");

        var c = CodeWriter.Generate(program, symbols);

        var cPath = request.KeepIntermediate
            ? Path.ChangeExtension(request.OutputPath, ".c")
            : Path.Combine(Path.GetTempPath(), $"basalt-qb-{Guid.NewGuid():N}.c");

        try
        {
            var directory = Path.GetDirectoryName(request.OutputPath);
            if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(cPath, c, ct).ConfigureAwait(false);

            var (exitCode, output) = await RunClangAsync(cPath, request, ct).ConfigureAwait(false);

            if (exitCode != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "QB100", "The C compiler rejected the generated code.", 1, 1));
            }

            return new CompilationOutcome(
                exitCode == 0,
                exitCode == 0 ? request.OutputPath : null,
                diagnostics,
                output)
            {
                GeneratedCode = c
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new Diagnostic("QB101", ex.Message, 1, 1));

            return new CompilationOutcome(false, null, diagnostics, ex.Message)
            {
                GeneratedCode = c
            };
        }
        finally
        {
            if (!request.KeepIntermediate)
            {
                try { if (File.Exists(cPath)) File.Delete(cPath); }
                catch (IOException) { }
            }
        }
    }

    private async Task<(int ExitCode, string Output)> RunClangAsync(
        string cPath, CompilationRequest request, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(_clang)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in Arguments(cPath, request)) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The C compiler could not be started.");

        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return (process.ExitCode,
                (await output.ConfigureAwait(false) + await error.ConfigureAwait(false)).Trim());
    }

    /// <summary>
    /// The arguments clang is given.
    ///
    /// Cross-compiling needs a target triple and, for anything but macOS's own
    /// architectures, a sysroot: clang can emit code for a target it has no
    /// headers or libraries for, and fails at the link step rather than the
    /// compile one.
    /// </summary>
    internal static IReadOnlyList<string> Arguments(string cPath, CompilationRequest request)
    {
        var arguments = new List<string> { cPath, "-o", request.OutputPath, "-O2", "-lm" };

        var architecture = request.Architecture ?? HostArchitecture;
        var operatingSystem = request.OperatingSystem ?? HostOperatingSystem;

        // On macOS, -arch selects between the architectures the system ships
        // for; a different operating system needs a full triple.
        if (operatingSystem == "macos" && System.OperatingSystem.IsMacOS())
        {
            arguments.Add("-arch");
            arguments.Add(architecture);
        }
        else
        {
            arguments.Add("-target");
            arguments.Add(Triple(architecture, operatingSystem));
        }

        if (request.Sysroot is { Length: > 0 } sysroot)
        {
            arguments.Add("--sysroot");
            arguments.Add(sysroot);
        }

        return arguments;
    }

    internal static string HostOperatingSystem =>
        System.OperatingSystem.IsMacOS() ? "macos"
        : System.OperatingSystem.IsWindows() ? "windows"
        : "linux";

    /// <summary>The target triple clang names a platform by.</summary>
    internal static string Triple(string architecture, string operatingSystem) =>
        operatingSystem switch
        {
            "macos" => $"{architecture}-apple-macos11",
            "windows" => $"{architecture}-pc-windows-msvc",
            _ => $"{architecture}-unknown-linux-gnu"
        };
}
