using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>Shared compile-time regression check for generated/edited VB designer and companion
/// source: every editor test that claims a rewrite stays compilable uses this single helper
/// instead of its own copy, so the Roslyn reference set is defined once.</summary>
internal static class WinFormsCompilationAssert
{
    internal static void AssertCompiles(params string[] sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(System.Windows.Forms.Form).Assembly.Location, typeof(System.Drawing.Font).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = VisualBasicCompilation.Create("DesignerRegression",
            sources.Select(source => VisualBasicSyntaxTree.ParseText(source)),
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
