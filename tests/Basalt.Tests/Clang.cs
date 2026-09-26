using Basalt.QuickBasic;

namespace Basalt.Tests;

/// <summary>
/// Whether the tests that build native QuickBASIC programs can run here.
/// </summary>
/// <remarks>
/// clang is an external tool, not something the solution restores: a machine
/// without it cannot run those tests, and failing them there reports a missing
/// install as forty broken compilers. They are skipped instead, saying why.
/// Asked once per run, because asking starts a process.
/// </remarks>
internal static class Clang
{
    private static readonly Lazy<bool> Available = new(() => new NativeCompiler().IsAvailable);

    public static void SkipUnlessInstalled() =>
        Assert.SkipUnless(Available.Value,
            "clang was not found on the PATH; install LLVM to run the native QuickBASIC tests.");
}
