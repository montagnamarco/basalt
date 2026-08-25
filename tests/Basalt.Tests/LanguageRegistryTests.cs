using Basalt.Extensibility;

namespace Basalt.Tests;

/// <summary>
/// The registry and the language contracts.
///
/// A language written entirely inside this test file has to work as well as one
/// shipped with the IDE. If it cannot, the abstraction is not an abstraction.
/// </summary>
public class LanguageRegistryTests
{
    /// <summary>A whole language, in a few lines, using nothing but the contracts.</summary>
    private sealed class ToyLanguage : ILanguageProvider
    {
        public LanguageIdentity Identity { get; } = new(
            id: "toy",
            displayName: "Toy",
            fileExtensions: [".toy"],
            isCaseSensitive: false);

        public ICompletionProvider? Completion { get; } = new ToyCompletion();
        public IDiagnosticProvider? Diagnostics { get; } = new ToyDiagnostics();
        public ISyntaxHighlightProvider? Highlighting => null;
        public INavigationProvider? Navigation => null;
        public IFormattingProvider? Formatting => null;
        public ICompilerBackend? Compiler => null;
    }

    private sealed class ToyCompletion : ICompletionProvider
    {
        public Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
            LanguageDocument document, int position, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CompletionItem>>(
            [
                new("PRINT", "PRINT", SymbolKind.Keyword),
                new("INPUT", "INPUT", SymbolKind.Keyword)
            ]);
    }

    private sealed class ToyDiagnostics : IDiagnosticProvider
    {
        public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
            LanguageDocument document, CancellationToken ct = default)
        {
            var problems = document.Text.Contains("BAD", StringComparison.Ordinal)
                ? new[]
                {
                    new Diagnostic("TOY001", "BAD is not allowed",
                        DiagnosticSeverity.Error, SourceRange.At(SourcePosition.Start))
                }
                : [];

            return Task.FromResult<IReadOnlyList<Diagnostic>>(problems);
        }
    }

    [Fact]
    public void ResolvesALanguageFromAFileExtension()
    {
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());

        var provider = registry.ForFile("/some/where/program.toy");

        Assert.NotNull(provider);
        Assert.Equal("toy", provider!.Identity.Id);
    }

    [Fact]
    public void ReturnsNothingForAnUnclaimedExtension()
    {
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());

        Assert.Null(registry.ForFile("/some/where/notes.txt"));
    }

    [Fact]
    public void MatchesExtensionsRegardlessOfCase()
    {
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());

        Assert.NotNull(registry.ForFile("/PROGRAM.TOY"));
    }

    [Fact]
    public void NormalisesExtensionsGivenWithoutADot()
    {
        var identity = new LanguageIdentity("x", "X", ["bas", ".BI"]);

        Assert.Equal([".bas", ".bi"], identity.FileExtensions);
    }

    [Fact]
    public void ReplacesAProviderRegisteredTwiceUnderTheSameId()
    {
        // Re-registering updates a language rather than duplicating it.
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());
        registry.Register(new ToyLanguage());

        Assert.Single(registry.Providers);
    }

    [Fact]
    public void UnregistersALanguage()
    {
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());

        Assert.True(registry.Unregister("toy"));
        Assert.Null(registry.ForFile("/program.toy"));
        Assert.False(registry.Unregister("toy"));
    }

    [Fact]
    public void SignalsWhenTheSetOfLanguagesChanges()
    {
        var registry = new LanguageRegistry();
        var raised = 0;
        registry.Changed += (_, _) => raised++;

        registry.Register(new ToyLanguage());
        registry.Unregister("toy");

        Assert.Equal(2, raised);
    }

    [Fact]
    public void ListsEveryKnownExtension()
    {
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());

        Assert.Contains(".toy", registry.KnownExtensions);
    }

    [Fact]
    public async Task RunsCompletionFromALanguageDefinedOutsideTheIde()
    {
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());

        var provider = registry.ForFile("/program.toy")!;
        var document = new LanguageDocument("/program.toy", "PR");

        var items = await provider.Completion!.GetCompletionsAsync(document, 2);

        Assert.Contains(items, i => i.DisplayText == "PRINT");
    }

    [Fact]
    public async Task RunsDiagnosticsFromALanguageDefinedOutsideTheIde()
    {
        var registry = new LanguageRegistry();
        registry.Register(new ToyLanguage());

        var provider = registry.ForFile("/program.toy")!;

        var clean = await provider.Diagnostics!.GetDiagnosticsAsync(
            new LanguageDocument("/program.toy", "PRINT 1"));
        Assert.Empty(clean);

        var broken = await provider.Diagnostics!.GetDiagnosticsAsync(
            new LanguageDocument("/program.toy", "BAD"));
        Assert.Contains(broken, d => d.Id == "TOY001");
    }

    [Fact]
    public void LetsALanguageOfferOnlySomeCapabilities()
    {
        // A language with a parser but no compiler says so by returning null,
        // rather than throwing from a method it cannot implement.
        var provider = new ToyLanguage();

        Assert.NotNull(provider.Completion);
        Assert.Null(provider.Compiler);
        Assert.Null(provider.Formatting);
    }

    [Fact]
    public async Task KeepsGoingWhenOneLanguageFailsToOpenASolution()
    {
        var registry = new LanguageRegistry();
        registry.Register(new FailingLanguage());
        registry.Register(new ToyLanguage());

        LanguageFailure? failure = null;
        registry.Failed += (_, f) => failure = f;

        // Must not throw: a language that cannot load a solution still leaves
        // its files editable, and the others unaffected.
        await registry.OpenSolutionAsync("/no/such/solution.sln");

        Assert.NotNull(failure);
        Assert.Equal("failing", failure!.Language.Id);
    }

    private sealed class FailingLanguage : ILanguageProvider
    {
        public LanguageIdentity Identity { get; } = new("failing", "Failing", [".fail"]);

        public ICompletionProvider? Completion => null;
        public IDiagnosticProvider? Diagnostics => null;
        public ISyntaxHighlightProvider? Highlighting => null;
        public INavigationProvider? Navigation => null;
        public IFormattingProvider? Formatting => null;
        public ICompilerBackend? Compiler => null;

        public Task OpenSolutionAsync(string solutionOrProjectPath, CancellationToken ct = default) =>
            throw new InvalidOperationException("cannot load");
    }
}
