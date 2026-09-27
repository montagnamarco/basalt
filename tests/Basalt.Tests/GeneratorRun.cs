using System.Collections.Immutable;
using System.Reflection;
using Basalt.Razor.Vb.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// Runs the real source generators over templates, the way the compiler does.
/// </summary>
/// <remarks>
/// The generator assembly is loaded from its build output rather than
/// referenced: it carries the parser as linked sources, and a reference would
/// put every parser type in the tests twice. Loading it is also exactly what
/// the compiler does, so a generator that only works when referenced cannot
/// pass here.
/// </remarks>
internal static class GeneratorRun
{
    private static readonly Lazy<Assembly> Generators = new(() =>
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Basalt.Razor.Vb.Generator", "bin",
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "netstandard2.0", "Basalt.Razor.Vb.Generator.dll"));

        return Assembly.LoadFrom(path);
    });

    private static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(VbHtmlView).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Basalt.Razor.Vb.AspNetCore.dll")),
    ];

    /// <summary>What one run produced.</summary>
    public sealed record Outcome(
        IReadOnlyList<Diagnostic> Diagnostics,
        IReadOnlyDictionary<string, string> Sources,
        Exception? Exception,
        IReadOnlyList<Diagnostic> CompilationErrors)
    {
        /// <summary>The compilation with the generated code in it, for a test that runs it.</summary>
        public Compilation? Compilation { get; init; }
    }

    /// <summary>
    /// Runs one generator, by type name, over templates given as path and text.
    /// </summary>
    public static Outcome Run(string generatorTypeName, params (string Path, string Text)[] templates) =>
        Run(generatorTypeName, projectDirectory: null, templates);

    /// <summary>
    /// Runs one generator with BasaltProjectDir set, as the package props set it.
    /// </summary>
    public static Outcome Run(
        string generatorTypeName, string? projectDirectory, params (string Path, string Text)[] templates) =>
        Run(generatorTypeName, projectDirectory, optionStrict: false, properties: null, templates);

    /// <summary>
    /// Runs one generator in a project with Option Strict as given and extra
    /// build properties, as MSBuild would pass them.
    /// </summary>
    public static Outcome Run(
        string generatorTypeName,
        string? projectDirectory,
        bool optionStrict,
        IReadOnlyDictionary<string, string>? properties,
        params (string Path, string Text)[] templates) =>
        Run(generatorTypeName, projectDirectory, optionStrict, properties, code: [], templates);

    /// <summary>
    /// Runs one generator in a project that also holds Visual Basic files of
    /// its own — a code-behind, a model.
    /// </summary>
    public static Outcome Run(
        string generatorTypeName,
        string? projectDirectory,
        bool optionStrict,
        IReadOnlyDictionary<string, string>? properties,
        string[] code,
        params (string Path, string Text)[] templates)
    {
        var type = Generators.Value.GetType($"Basalt.Razor.Vb.Generator.{generatorTypeName}", throwOnError: true)!;
        var generator = ((IIncrementalGenerator)Activator.CreateInstance(type)!).AsSourceGenerator();

        var compilation = VisualBasicCompilation.Create(
            "Generated",
            [
                VisualBasicSyntaxTree.ParseText("Module Program\nEnd Module"),
                .. code.Select(source => VisualBasicSyntaxTree.ParseText(source)),
            ],
            References,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithGlobalImports(GlobalImport.Parse("Microsoft.VisualBasic", "System"))
                .WithOptionStrict(optionStrict ? OptionStrict.On : OptionStrict.Off));

        var additional = templates
            .Select(t => (AdditionalText)new Template(t.Path, t.Text))
            .ToImmutableArray();

        var driver = VisualBasicGeneratorDriver.Create(
            [generator], additional, parseOptions: VisualBasicParseOptions.Default,
            analyzerConfigOptionsProvider: new Options(projectDirectory, properties));

        var ran = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        var result = ran.GetRunResult().Results.Single();

        var errors = updated.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return new Outcome(
            result.Diagnostics,
            result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString()),
            result.Exception,
            errors)
        {
            Compilation = updated,
        };
    }

    /// <summary>The build properties the generator reads, as MSBuild hands them over.</summary>
    private sealed class Options(string? projectDirectory, IReadOnlyDictionary<string, string>? properties)
        : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider
    {
        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GlobalOptions { get; } =
            new Values(projectDirectory, properties);

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
            new Values(null, null);

        public override Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            new Values(null, null);
    }

    private sealed class Values(string? projectDirectory, IReadOnlyDictionary<string, string>? properties)
        : Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = "";

            if (key == "build_property.BasaltProjectDir" && projectDirectory is not null)
            {
                value = projectDirectory;
                return true;
            }

            if (properties is not null && key.StartsWith("build_property.", StringComparison.Ordinal) &&
                properties.TryGetValue(key["build_property.".Length..], out var found))
            {
                value = found;
                return true;
            }

            return false;
        }
    }

    private sealed class Template(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, System.Text.Encoding.UTF8, SourceHashAlgorithm.Sha256);
    }
}
