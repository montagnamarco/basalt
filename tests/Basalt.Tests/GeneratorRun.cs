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
    ];

    /// <summary>What one run produced.</summary>
    public sealed record Outcome(
        IReadOnlyList<Diagnostic> Diagnostics,
        IReadOnlyDictionary<string, string> Sources,
        Exception? Exception);

    /// <summary>
    /// Runs one generator, by type name, over templates given as path and text.
    /// </summary>
    public static Outcome Run(string generatorTypeName, params (string Path, string Text)[] templates)
    {
        var type = Generators.Value.GetType($"Basalt.Razor.Vb.Generator.{generatorTypeName}", throwOnError: true)!;
        var generator = ((IIncrementalGenerator)Activator.CreateInstance(type)!).AsSourceGenerator();

        var compilation = VisualBasicCompilation.Create(
            "Generated",
            [VisualBasicSyntaxTree.ParseText("Module Program\nEnd Module")],
            References,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithGlobalImports(GlobalImport.Parse("Microsoft.VisualBasic", "System")));

        var additional = templates
            .Select(t => (AdditionalText)new Template(t.Path, t.Text))
            .ToImmutableArray();

        var driver = VisualBasicGeneratorDriver.Create(
            [generator], additional, parseOptions: VisualBasicParseOptions.Default);

        var result = driver.RunGenerators(compilation).GetRunResult().Results.Single();

        return new Outcome(
            result.Diagnostics,
            result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString()),
            result.Exception);
    }

    private sealed class Template(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, System.Text.Encoding.UTF8, SourceHashAlgorithm.Sha256);
    }
}
