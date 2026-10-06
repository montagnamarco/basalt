using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Workspace.Immediate;

/// <summary>
/// Turns a line from the Immediate window into the tiny Visual Basic program
/// that evaluates it, and compiles that program.
///
/// Compiled in this process and run in another: compiling is CPU work on
/// text and nothing the user wrote runs here, while running executes their
/// code (and their project's) and so belongs in a child process the IDE can
/// kill.
/// </summary>
public static class ImmediateProgram
{
    /// <summary>Process exit code of a program whose line threw an exception.</summary>
    public const int ExceptionExitCode = 3;

    /// <summary>Where the program looks for the project's assemblies.</summary>
    public const string ProbeVariable = "BASALT_IMMEDIATE_PROBE";

    /// <summary>
    /// The source of the program for a line.
    ///
    /// The user's text sits on a line of its own, and nothing else shares
    /// it, so a diagnostic's line number says whether it is about the user's
    /// text or about the scaffolding.
    /// </summary>
    public static string BuildSource(ImmediateInput input)
    {
        var line = input.Kind == ImmediateKind.Print
            ? $"System.Console.Out.WriteLine(ImmediateShow({input.Text}))"
            : input.Text;

        var source = new StringBuilder();
        source.AppendLine("Module BasaltImmediate");
        source.AppendLine("    Function Main() As Integer");
        // Installed before the method that touches the project's types runs:
        // the runtime resolves them when that method is compiled, not when the
        // line is reached.
        source.AppendLine("        AddHandler System.AppDomain.CurrentDomain.AssemblyResolve, AddressOf ResolveFromProject");
        source.AppendLine("        Return Evaluate()");
        source.AppendLine("    End Function");
        source.AppendLine();
        source.AppendLine("    <System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>");
        source.AppendLine("    Private Function Evaluate() As Integer");
        source.AppendLine("        Try");
        source.AppendLine(line);
        source.AppendLine("            Return 0");
        source.AppendLine("        Catch ex As System.Exception");
        source.AppendLine("            System.Console.Error.WriteLine(ex.GetType().FullName & \": \" & ex.Message)");
        source.AppendLine($"            Return {ExceptionExitCode}");
        source.AppendLine("        End Try");
        source.AppendLine("    End Function");
        source.AppendLine();
        source.AppendLine("    Private Function ResolveFromProject(sender As Object, e As System.ResolveEventArgs) As System.Reflection.Assembly");
        source.AppendLine($"        Dim folder = System.Environment.GetEnvironmentVariable(\"{ProbeVariable}\")");
        source.AppendLine("        If String.IsNullOrEmpty(folder) Then Return Nothing");
        source.AppendLine("        Dim file = System.IO.Path.Combine(folder, New System.Reflection.AssemblyName(e.Name).Name & \".dll\")");
        source.AppendLine("        If System.IO.File.Exists(file) Then Return System.Reflection.Assembly.LoadFrom(file)");
        source.AppendLine("        Return Nothing");
        source.AppendLine("    End Function");
        source.AppendLine();
        // What Visual Basic 6 printed for "?": a string without its quotes,
        // Nothing spelled out, and a sequence between braces.
        source.AppendLine("    Private Function ImmediateShow(value As Object) As String");
        source.AppendLine("        If value Is Nothing Then Return \"Nothing\"");
        source.AppendLine("        If TypeOf value Is String Then Return DirectCast(value, String)");
        source.AppendLine("        Dim sequence = TryCast(value, System.Collections.IEnumerable)");
        source.AppendLine("        If sequence IsNot Nothing Then");
        source.AppendLine("            Dim parts As New System.Collections.Generic.List(Of String)");
        source.AppendLine("            For Each item In sequence");
        source.AppendLine("                If parts.Count = 200 Then parts.Add(\"...\") : Exit For");
        source.AppendLine("                parts.Add(ImmediateShow(item))");
        source.AppendLine("            Next");
        source.AppendLine("            Return \"{\" & String.Join(\", \", parts) & \"}\"");
        source.AppendLine("        End If");
        source.AppendLine("        Return value.ToString()");
        source.AppendLine("    End Function");
        source.AppendLine("End Module");

        return source.ToString();
    }

    /// <summary>The compilation of a line against the given references and project options.</summary>
    public static VisualBasicCompilation CreateCompilation(
        ImmediateInput input,
        ImmediateProjectOptions options,
        IEnumerable<MetadataReference> references)
    {
        var imports = ImmediateProjectOptions.DefaultImports
            .Concat(options.Imports)
            .Distinct(StringComparer.Ordinal)
            .Select(GlobalImport.Parse);

        var compilationOptions = new VisualBasicCompilationOptions(
            OutputKind.ConsoleApplication,
            moduleName: "BasaltImmediate",
            rootNamespace: options.RootNamespace,
            globalImports: imports,
            optionExplicit: options.OptionExplicit ?? true,
            optionInfer: options.OptionInfer ?? true,
            optionStrict: options.OptionStrict == true ? OptionStrict.On : OptionStrict.Off,
            optionCompareText: options.OptionCompareText ?? false);

        var tree = VisualBasicSyntaxTree.ParseText(BuildSource(input));

        return VisualBasicCompilation.Create(
            "BasaltImmediate_" + Guid.NewGuid().ToString("N"),
            [tree],
            references,
            compilationOptions);
    }

    /// <summary>
    /// The errors of a compilation, as "BC30451: message" lines, in the
    /// compiler's English so the same line reads the same everywhere.
    /// </summary>
    public static IReadOnlyList<string> ErrorsOf(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .ToList();
}
