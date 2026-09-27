using Basalt.Extensibility;
using Basalt.QuickBasic;
using Basalt.Workspace.Web;

namespace Basalt.Workspace.Languages;

/// <summary>
/// Every language the IDE knows, gathered in one place.
///
/// This is where a new language is added: implement the interfaces and
/// register it here. Nothing else in the IDE needs changing, which is what the
/// extensible architecture was for.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>
    /// Builds a registry with the languages that ship with the IDE.
    ///
    /// Visual Basic is the one Roslyn language now: C# was registered here
    /// and has been taken out, since Basalt is for Basic and its dialects.
    /// </summary>
    public static LanguageRegistry CreateDefault(
        RoslynLanguageService roslyn, RoslynFormattingService formatting)
    {
        var registry = new LanguageRegistry();

        registry.Register(RoslynLanguageProvider.CreateVisualBasic(roslyn, formatting));

        // Razor is given a way to ask about the Visual Basic it generates,
        // so the code half of a template offers what the compiler knows
        // rather than a guess. The generated code is handed over as the
        // current text, which is what Roslyn reads while the editor is ahead
        // of the workspace.
        WebLanguageProviders.RegisterAll(
            registry,
            async (generated, at, ct) =>
            {
                var items = await roslyn
                    .GetCompletionsAsync(GeneratedViewPath, at, generated, ct)
                    .ConfigureAwait(false);

                return (IReadOnlyList<Extensibility.CompletionItem>)
                    [.. items.Select(RoslynCompletionProvider.Convert)];
            },
            async (generated, at, ct) =>
            {
                // Hovering a model property in a template should say what
                // hovering it in a .vb file says: it is the same property.
                var info = await roslyn
                    .GetQuickInfoAsync(GeneratedViewPath, at, generated, ct)
                    .ConfigureAwait(false);

                return info is { Length: > 0 }
                    ? new Extensibility.QuickInfo(info, null)
                    : null;
            },
            (generated, ct) =>
                // What the compiler makes of the generated view. The provider
                // maps each error back onto the template and drops the ones
                // that will not map, so nothing lands on a line the author
                // never wrote.
                roslyn.GetDiagnosticsAsync(GeneratedViewPath, generated, ct),
            async (generated, at, ct) =>
            {
                // Jumping to a model property from a template should land in
                // the model, exactly as it would from a .vb file.
                var found = await roslyn
                    .GoToDefinitionAsync(GeneratedViewPath, at, generated, ct)
                    .ConfigureAwait(false);

                return found is { } place
                    ? new Extensibility.SourceLocation(
                        place.FilePath,
                        Extensibility.SourceRange.At(
                            new Extensibility.SourcePosition(place.Line, place.Column)))
                    : null;
            },
            (generated, at, ct) =>
                // Renaming a model property should list the views that use
                // it: without this they are quietly left out of the answer.
                roslyn.FindReferencesAsync(GeneratedViewPath, at, generated, ct),
            (generated, at, ct) =>
                // What the call under the caret expects. It used to be a
                // table of three runtime helpers, which said nothing about
                // the model's own methods.
                roslyn.GetSignatureHelpAsync(GeneratedViewPath, at, generated, ct),
            askCatalog: (templatePath, currentText, ct) =>
                // A .vbrazor's own component catalog, learned the way the
                // build learns it: without this a named RenderFragment
                // parameter was opened as a component that does not exist,
                // and @context inside a typed RenderFragment was undeclared —
                // both false errors, on code the build accepted.
                roslyn.GetComponentCatalogAsync(templatePath, currentText, ct));

        // QuickBASIC comes from its own project, which references only the
        // extensibility contracts: it is the proof that a language can be
        // added from outside the IDE.
        registry.Register(new QuickBasicLanguageProvider());

        return registry;
    }

    /// <summary>
    /// The path the generated view is asked about under.
    ///
    /// A .vb name, because Roslyn decides the language from the extension and
    /// generated Razor is Visual Basic.
    /// </summary>
    private const string GeneratedViewPath = "__BasaltGeneratedView.vb";

    /// <summary>The file extensions the IDE can do something useful with.</summary>
    public static IReadOnlyList<string> KnownExtensions(LanguageRegistry registry) =>
        registry.KnownExtensions;
}
