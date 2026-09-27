using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Basalt.Core.Model;
using Basalt.Core.Services;
using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Generator;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using RoslynCompletionItem = Microsoft.CodeAnalysis.Completion.CompletionItem;
using IdeCompletionItem = Basalt.Core.Model.CompletionItem;

namespace Basalt.Workspace;

/// <summary>
/// In-process Roslyn-based language service, for Visual Basic. One
/// MSBuildWorkspace serves the whole solution, and Roslyn reads the language
/// from the project that owns each document, so the completion, diagnostic
/// and navigation operations need no per-language branches.
/// </summary>
public sealed class RoslynLanguageService : ILanguageService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MSBuildWorkspace? _workspace;

    public Solution? CurrentSolution => _workspace?.CurrentSolution;

    /// <summary>Warnings reported by MSBuild while loading the solution.</summary>
    public IReadOnlyList<string> LoadWarnings { get; private set; } = [];

    public event EventHandler? SolutionChanged;

    public async Task OpenSolutionAsync(string solutionOrProjectPath, CancellationToken ct = default)
    {
        MsBuildEnvironment.EnsureInitialized();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _workspace?.Dispose();
            _workspace = MSBuildWorkspace.Create(CreateHostServices());

            // A load failure on a single project must not prevent the others
            // from opening: they are collected and surfaced.
            var warnings = new List<string>();
            _workspace.RegisterWorkspaceFailedHandler(e => warnings.Add(e.Diagnostic.Message));

            if (Path.GetExtension(solutionOrProjectPath).Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(solutionOrProjectPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                await _workspace.OpenSolutionAsync(solutionOrProjectPath, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                await _workspace.OpenProjectAsync(solutionOrProjectPath, cancellationToken: ct).ConfigureAwait(false);
            }

            LoadWarnings = warnings;
        }
        finally
        {
            _gate.Release();
        }

        SolutionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateDocumentAsync(string filePath, string text, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_workspace is null) return;

            var documentId = FindDocumentId(filePath);
            if (documentId is null) return;

            var updated = _workspace.CurrentSolution.WithDocumentText(
                documentId, SourceText.From(text), PreservationMode.PreserveIdentity);

            // TryApplyChanges can fail if another change arrived in the
            // meantime; in that case the next keystroke will resend the text.
            _workspace.TryApplyChanges(updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<IdeDiagnostic>> GetDiagnosticsAsync(
        string filePath, CancellationToken ct = default) =>
        GetDiagnosticsAsync(filePath, currentText: null, ct);

    /// <summary>
    /// Diagnostics for a document, optionally against text the workspace does
    /// not hold — generated Razor, for instance.
    /// </summary>
    public async Task<IReadOnlyList<IdeDiagnostic>> GetDiagnosticsAsync(
        string filePath, string? currentText, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return [];

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        // On a thread of its own, deliberately.
        //
        // GetSemanticModelAsync returns a completed task whenever Roslyn has
        // the model already, so the await does not yield and everything after
        // it runs on the caller. GetDiagnostics is then synchronous and, on a
        // file that has just been edited, costs about 22ms — measured. That
        // is 22ms of a frozen interface after every typing pause, which is
        // exactly the kind of stall that reads as the editor being heavy.
        return await Task.Run(
            () =>
            {
                var model = document.GetSemanticModelAsync(ct).GetAwaiter().GetResult();

                if (model is null) return (IReadOnlyList<IdeDiagnostic>)[];

                return model.GetDiagnostics(cancellationToken: ct)
                    .Select(ToIdeDiagnostic)
                    .ToList();
            },
            ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<IdeCompletionItem>> GetCompletionsAsync(
        string filePath, int position, CancellationToken ct = default) =>
        GetCompletionsAsync(filePath, position, currentText: null, ct);

    /// <summary>
    /// Completions at a position, optionally against text newer than the
    /// workspace's copy.
    ///
    /// While typing, the editor is ahead of the workspace. Asking Roslyn about
    /// a position that only exists in the newer text throws, so the caller's
    /// text is applied first and the position clamped to it.
    /// </summary>
    /// <param name="typed">
    /// The character whose typing opened the list, when one did: Roslyn's
    /// providers answer an insertion differently from an explicit request,
    /// and it is how "New " gets the declared type preselected.
    /// </param>
    public async Task<IReadOnlyList<IdeCompletionItem>> GetCompletionsAsync(
        string filePath, int position, string? currentText, CancellationToken ct = default, char? typed = null)
    {
        // A file the workspace does not know — generated Razor, most of all —
        // still deserves an answer, so it is put in a throwaway project with
        // the solution's references. Without this the caller silently got
        // nothing back and had no way to tell why.
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return [];

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        // On a thread of its own, as the diagnostics are and for the same
        // reason: Roslyn's async methods return completed tasks whenever the
        // work is already done, so awaiting them does not yield and the
        // synchronous parts run on the caller. Measured at 59ms of the caller
        // held on a file just edited — on every keystroke after a dot, which
        // is precisely when the editor must not stutter.
        return await Task.Run(
            async () =>
            {
                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                var caret = Math.Clamp(position, 0, text.Length);

                var service = CompletionService.GetService(document);

                if (service is null) return (IReadOnlyList<IdeCompletionItem>)[];

                var trigger = typed is { } character
                    ? CompletionTrigger.CreateInsertionTrigger(character)
                    : CompletionTrigger.Invoke;

                var completions = await service
                    .GetCompletionsAsync(document, caret, trigger, cancellationToken: ct)
                    .ConfigureAwait(false);

                return completions.ItemsList
                    .Select(item => ToCompletionItem(item) with
                    {
                        ResolveCommit = token => CommitAsync(service, document, item, token),
                    })
                    .ToList();
            },
            ct).ConfigureAwait(false);
    }

    public Task<(string FilePath, int Line, int Column)?> GoToDefinitionAsync(
        string filePath, int position, CancellationToken ct = default) =>
        GoToDefinitionAsync(filePath, position, currentText: null, ct);

    /// <summary>
    /// The definition of the symbol at a position, optionally in text the
    /// workspace does not hold — generated Razor, for instance.
    /// </summary>
    public async Task<(string FilePath, int Line, int Column)?> GoToDefinitionAsync(
        string filePath, int position, string? currentText, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return null;

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, ct).ConfigureAwait(false);
        if (symbol is null) return null;

        // Source is preferred; for symbols coming from metadata there is no
        // file to open (a decompiler would be needed).
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null) return null;

        var lineSpan = location.GetLineSpan();
        return (lineSpan.Path,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1);
    }

    /// <summary>
    /// The symbol at a position, for whoever wants to describe it.
    ///
    /// Handed over rather than described here: how a symbol reads is a
    /// question about presentation, and this class is about the compiler.
    /// </summary>
    internal async Task<ISymbol?> FindSymbolAsync(
        string filePath, int position, string? currentText, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return null;

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        return await SymbolFinder
            .FindSymbolAtPositionAsync(document, position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What a call at a position could be, and which argument is being written.</summary>
    internal record struct CallAtPosition(
        IReadOnlyList<IMethodSymbol> Methods, int ActiveParameter);

    /// <summary>
    /// The call the caret sits inside, with its overloads.
    /// </summary>
    internal async Task<CallAtPosition?> FindCallAsync(
        string filePath, int position, string? currentText, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return null;

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        if (root is null || model is null) return null;

        var caret = Math.Clamp(position, 0, root.FullSpan.End);

        if (FindInvocation(root, caret, document.Project.Language) is not { } invocation)
            return null;

        var (target, arguments, listStart) = invocation;

        var candidates = model.GetSymbolInfo(target, ct);

        var methods = candidates.Symbol is IMethodSymbol single
            ? [single]
            : candidates.CandidateSymbols.OfType<IMethodSymbol>().ToList();

        if (methods.Count == 0
            && model.GetSymbolInfo(target, ct).Symbol is INamedTypeSymbol type)
            methods = [.. type.InstanceConstructors];

        if (methods.Count == 0) return null;

        return new CallAtPosition(methods, CountArgumentsBefore(arguments, caret, listStart));
    }

    public Task<string?> GetQuickInfoAsync(
        string filePath, int position, CancellationToken ct = default) =>
        GetQuickInfoAsync(filePath, position, currentText: null, ct);

    /// <summary>
    /// Quick info at a position, optionally against text the workspace does
    /// not hold — generated Razor, for instance.
    /// </summary>
    public async Task<string?> GetQuickInfoAsync(
        string filePath, int position, string? currentText, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return null;

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, ct).ConfigureAwait(false);
        if (symbol is null) return null;

        var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var docComment = symbol.GetDocumentationCommentXml(cancellationToken: ct);

        return string.IsNullOrWhiteSpace(docComment)
            ? signature
            : $"{signature}\n\n{ExtractSummary(docComment)}";
    }

    /// <summary>
    /// A template's component catalog, learned from the project's own
    /// compilation the way the build learns it: every .vbrazor is declared —
    /// its class, base type and the members its @Functions and @Code blocks
    /// declare, with no render tree — before any of them is written, so a
    /// page using &lt;Card&gt;&lt;Header&gt; sees Header as Card's
    /// RenderFragment parameter rather than as an unknown component, and a
    /// RenderFragment(Of T) declares its own @context.
    /// </summary>
    /// <remarks>
    /// Without a solution, or a project the template does not sit under, this
    /// answers null and the writer falls back to what it did before catalogs
    /// existed — not an empty catalog, which would claim a component that
    /// simply is not there.
    ///
    /// Cached per <see cref="Compilation"/> instance: editing one .vbrazor
    /// never touches the workspace's own Documents, since a .vbrazor is not a
    /// Visual Basic file the workspace tracks, so the project's compilation
    /// stays the very same object between keystrokes. Reading every other
    /// .vbrazor from disk and declaring it is the expensive part, and it is
    /// done once for that compilation rather than on every key. The one
    /// component being edited is always re-read from <paramref
    /// name="currentText"/> — a stale catalog for that one file would answer
    /// about what used to be on disk rather than what the author is typing.
    /// </remarks>
    public async Task<IComponentCatalog?> GetComponentCatalogAsync(
        string templatePath, string currentText, CancellationToken ct = default)
    {
        if (_workspace is null || string.IsNullOrEmpty(templatePath)) return null;

        var project = FindProjectContaining(templatePath);
        if (project?.FilePath is null) return null;

        Compilation? compilation;

        try
        {
            compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        if (compilation is null || compilation.Language != LanguageNames.VisualBasic) return null;

        var cache = _catalogCaches.GetValue(compilation, _ => new CatalogCache());

        // Read once per compilation: another component changing on disk
        // while this one is being typed in is not seen until the compilation
        // itself changes, the same staleness every keystroke-driven cache
        // accepts.
        cache.Components ??= DiscoverComponents(project.FilePath, ct);

        var projectDirectory = Path.GetDirectoryName(project.FilePath);
        var current = PrepareComponent(templatePath, currentText, projectDirectory);

        var declarations = new List<ComponentDeclaration>(cache.Components.Count + 1);
        var replacedCurrent = false;

        foreach (var component in cache.Components)
        {
            if (string.Equals(component.Path, templatePath, PathComparison))
            {
                declarations.Add(ToDeclaration(current));
                replacedCurrent = true;
            }
            else
            {
                declarations.Add(ToDeclaration(component));
            }
        }

        if (!replacedCurrent) declarations.Add(ToDeclaration(current));

        try
        {
            var catalogs = ComponentCatalogBuilder.Build(compilation, declarations, inferences: null, ct);

            if (!catalogs.TryGetValue(templatePath, out var catalog)) return null;

            // A generic component written without type arguments needs the
            // compiler to infer them, exactly as the build does: a dry run
            // records what is asked and answers none, and the catalog is
            // then rebuilt with the compiler's answers.
            var requests = new List<TypeInference>();

            try
            {
                VbComponentWriter.WriteWithMap(
                    current.Document, current.ClassName, current.Namespace, filePath: null,
                    catalog: new RecordingCatalog(catalog, requests));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Written again for real by the caller, where a failure is
                // something the editor can show rather than swallow.
                return catalog;
            }

            if (requests.Count == 0) return catalog;

            var inferences = new Dictionary<string, List<TypeInference>>(StringComparer.Ordinal)
            {
                [templatePath] = requests
            };

            var withInference = ComponentCatalogBuilder.Build(compilation, declarations, inferences, ct);

            return withInference.TryGetValue(templatePath, out var inferred) ? inferred : catalog;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A catalog that could not be built leaves the writer exactly as
            // it behaved before catalogs existed, rather than taking every
            // question about the template down with it.
            return null;
        }
    }

    /// <summary>Every component catalog built for one compilation, kept until it changes.</summary>
    private sealed class CatalogCache
    {
        public IReadOnlyList<PreparedComponent>? Components;
    }

    /// <summary>A .vbrazor read and ready to be declared: its path, parsed markup, class name and namespace.</summary>
    private sealed record PreparedComponent(string Path, VbHtmlDocument Document, string ClassName, string Namespace);

    private static ComponentDeclaration ToDeclaration(PreparedComponent component) =>
        new(component.Path, component.Document, component.ClassName, component.Namespace);

    /// <summary>A catalog that answers lookups and writes down the inferences asked of it.</summary>
    /// <remarks>
    /// The generator's own probe, mirrored here rather than reused: it is a
    /// three-line adapter over <see cref="IComponentCatalog"/>, and the
    /// generator's copy is a private nested class of a type this project does
    /// not otherwise reach into.
    /// </remarks>
    private sealed class RecordingCatalog(IComponentCatalog inner, List<TypeInference> requests) : IComponentCatalog
    {
        public ComponentShape? Find(string tagName, int typeArgumentCount) => inner.Find(tagName, typeArgumentCount);

        public IReadOnlyList<string>? InferTypeArguments(TypeInference request)
        {
            requests.Add(request);
            return null;
        }
    }

    /// <summary>
    /// The project whose folder is the nearest ancestor of a template's path.
    /// </summary>
    /// <remarks>
    /// The nearest rather than any: a project nested inside another's folder
    /// must not have its own templates claimed by the outer one.
    /// </remarks>
    private Project? FindProjectContaining(string templatePath)
    {
        if (_workspace is null) return null;

        string full;

        try
        {
            full = Path.GetFullPath(templatePath);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or NotSupportedException)
        {
            return null;
        }

        return _workspace.CurrentSolution.Projects
            .Where(p => p.Language == LanguageNames.VisualBasic && p.FilePath is { Length: > 0 })
            .Select(p => (Project: p, Directory: Path.GetDirectoryName(p.FilePath)))
            .Where(x => x.Directory is { Length: > 0 } &&
                        full.StartsWith(x.Directory, PathComparison) &&
                        (full.Length == x.Directory.Length || full[x.Directory.Length] is '/' or '\\'))
            .OrderByDescending(x => x.Directory!.Length)
            .Select(x => x.Project)
            .FirstOrDefault();
    }

    /// <summary>
    /// Every .vbrazor under a project's folder, parsed and ready to be
    /// declared — its _Imports.vbrazor already applied, the way the build
    /// applies it.
    /// </summary>
    private static IReadOnlyList<PreparedComponent> DiscoverComponents(string projectPath, CancellationToken ct)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDirectory)) return [];

        string[] files;

        try
        {
            files = Directory.GetFiles(projectDirectory, "*.vbrazor", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var prepared = new List<PreparedComponent>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            // _Imports.vbrazor is not a component itself: it lends its
            // directives to every component in its folder and below.
            if (string.Equals(Path.GetFileName(file), "_Imports.vbrazor", StringComparison.OrdinalIgnoreCase))
                continue;

            prepared.Add(PrepareComponent(file, ReadOrEmpty(file), projectDirectory));
        }

        return prepared;
    }

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A component that cannot be read contributes nothing to the
            // catalog; it does not stop every other one from being learned.
            return "";
        }
    }

    /// <summary>
    /// A .vbrazor's text, parsed and with its _Imports.vbrazor applied, named
    /// and namespaced the way the build names and namespaces it.
    /// </summary>
    private static PreparedComponent PrepareComponent(string path, string text, string? projectDirectory)
    {
        var document = VbHtmlParser.Parse(text);

        Web.TemplateGeneration.ApplySharedFiles(document, path, "_Imports.vbrazor");

        return new PreparedComponent(
            path,
            document,
            ViewNaming.MakeClassName(Path.GetFileNameWithoutExtension(path)),
            ComponentNamespaceFor(path, projectDirectory));
    }

    /// <summary>
    /// A component's namespace: its folders from the project, exactly as
    /// <c>VbComponentGenerator.NamespaceFor</c> decides it for the build —
    /// Components/Layout/MainLayout.vbrazor is in Components.Layout.
    /// </summary>
    private static string ComponentNamespaceFor(string path, string? projectDirectory)
    {
        if (ViewNaming.ProjectFolderNamespaceFor(path, projectDirectory) is { } folders)
            return folders;

        var folder = ViewNaming.FolderNamespaceFor(path);

        return string.IsNullOrWhiteSpace(folder) ? "Components" : $"Components.{folder}";
    }

    /// <summary>Every catalog built so far, one per live compilation.</summary>
    private readonly ConditionalWeakTable<Compilation, CatalogCache> _catalogCaches = new();

    /// <summary>
    /// The overloads callable at a position, and which argument is being typed.
    ///
    /// Built from the semantic model rather than Roslyn's own signature help,
    /// whose API is internal. Working from the syntax tree also keeps the two
    /// languages on one path: an argument list is an argument list whether it
    /// is written with parentheses in C# or in Visual Basic.
    /// </summary>
    public Task<SignatureHelp?> GetSignatureHelpAsync(
        string filePath, int position, CancellationToken ct = default) =>
        GetSignatureHelpAsync(filePath, position, currentText: null, ct);

    /// <summary>
    /// The call the caret is inside, optionally in text the workspace does
    /// not hold — generated Razor, for instance.
    /// </summary>
    public async Task<SignatureHelp?> GetSignatureHelpAsync(
        string filePath, int position, string? currentText, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return null;

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);

        if (root is null || model is null) return null;

        var caret = Math.Clamp(position, 0, root.FullSpan.End);

        if (FindInvocation(root, caret, document.Project.Language) is not { } invocation)
            return null;

        var (target, arguments, listStart) = invocation;

        var candidates = model.GetSymbolInfo(target, ct);

        var methods = candidates.Symbol is IMethodSymbol single
            ? [single]
            : candidates.CandidateSymbols.OfType<IMethodSymbol>().ToList();

        if (methods.Count == 0)
        {
            // A constructor is reached through the type, not through a method.
            if (model.GetSymbolInfo(target, ct).Symbol is INamedTypeSymbol type)
                methods = [.. type.InstanceConstructors];
            else
                return null;
        }

        if (methods.Count == 0) return null;

        var signatures = methods
            .Select(method => new SignatureInfo(
                method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                [.. method.Parameters.Select(p =>
                    p.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))])
            {
                Documentation = ExtractSummaryOrNull(
                    method.GetDocumentationCommentXml(cancellationToken: ct))
            })
            .ToList();

        return new SignatureHelp(
            signatures,
            ActiveSignature: 0,
            ActiveParameter: CountArgumentsBefore(arguments, caret, listStart));
    }

    /// <summary>
    /// The call the caret sits inside, or null when it is not inside one.
    ///
    /// The nearest enclosing call wins, so in "Outer(Inner(x))" the caret in
    /// "x" describes Inner rather than Outer.
    /// </summary>
    private static (SyntaxNode Target, SyntaxNode ArgumentList, int ListStart)? FindInvocation(
        SyntaxNode root, int caret, string language)
    {
        var token = root.FindToken(Math.Max(0, caret - 1));

        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var kind = KindName(node);

            if (kind is not ("InvocationExpression" or "ObjectCreationExpression")) continue;

            var children = node.ChildNodes().ToList();

            var argumentList = children.FirstOrDefault(c => KindName(c) == "ArgumentList");
            if (argumentList is null) continue;

            // Outside the brackets there is no call being typed. The end is
            // inclusive because a call still being typed has no closing
            // bracket yet, so its argument list ends exactly at the caret —
            // which is the usual case while the user is writing the call.
            if (caret <= argumentList.SpanStart || caret > argumentList.Span.End) continue;

            // A closed call ends after its bracket; the caret sitting on that
            // bracket is past the arguments, not in them.
            if (caret == argumentList.Span.End && EndsWithClosingBracket(argumentList)) continue;

            var target = children.FirstOrDefault(c => c != argumentList) ?? node;

            return (target, argumentList, argumentList.SpanStart);
        }

        return null;
    }

    /// <summary>
    /// Works out what a quick action would change.
    ///
    /// The same shape as a rename, so the user reviews both the same way: a
    /// fix that imports a namespace edits the file just as surely as a rename
    /// does.
    /// </summary>
    public async Task<Refactoring.RefactoringPreview> PreviewQuickActionAsync(
        string filePath, QuickAction action, CancellationToken ct = default)
    {
        var document = GetDocument(filePath);

        if (document is null)
        {
            return Refactoring.RefactoringPreview.Refused(
                action.Title, "That file is not part of the solution.");
        }

        Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation? applied;

        try
        {
            var operations = await action.Action.GetOperationsAsync(ct).ConfigureAwait(false);

            applied = operations
                .OfType<Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation>()
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return Refactoring.RefactoringPreview.Refused(action.Title, ex.Message);
        }

        if (applied is null)
        {
            return Refactoring.RefactoringPreview.Refused(
                action.Title, "That action does not change any files.");
        }

        var changes = new List<Refactoring.FileChangePreview>();

        foreach (var projectChange in applied.ChangedSolution
                     .GetChanges(document.Project.Solution).GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                ct.ThrowIfCancellationRequested();

                var before = document.Project.Solution.GetDocument(documentId);
                var after = applied.ChangedSolution.GetDocument(documentId);

                if (before?.FilePath is not { Length: > 0 } path || after is null) continue;

                var originalText = await before.GetTextAsync(ct).ConfigureAwait(false);
                var newText = await after.GetTextAsync(ct).ConfigureAwait(false);

                if (originalText.ContentEquals(newText)) continue;

                changes.Add(new Refactoring.FileChangePreview(
                    path, originalText.ToString(), newText.ToString()));
            }
        }

        return changes.Count == 0
            ? Refactoring.RefactoringPreview.Refused(action.Title, "Nothing would change.")
            : new Refactoring.RefactoringPreview(action.Title, changes);
    }

    /// <summary>
    /// Works out what renaming a symbol would change.
    ///
    /// Nothing is written: what comes back describes the change so the user
    /// can look before it happens.
    /// </summary>
    public async Task<Refactoring.RefactoringPreview> PreviewRenameAsync(
        string filePath, int position, string newName, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Rename", "No solution is open.");
        }

        return await new Refactoring.RenameRefactoring(solution)
            .PreviewAsync(filePath, position, newName, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Works out what naming a selected expression would change.
    ///
    /// Nothing is written, as with the other previews.
    /// </summary>
    public async Task<Refactoring.RefactoringPreview> PreviewExtractVariableAsync(
        string filePath, int start, int length, string name, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Extract Variable", "No solution is open.");
        }

        return await new Refactoring.ExtractVariableRefactoring(solution)
            .PreviewAsync(filePath, start, length, name, asConstant: false, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What generating a constructor from the type's fields would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewGenerateConstructorAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Generate Constructor", "No solution is open.");
        }

        return await new Refactoring.GenerateMemberRefactoring(solution)
            .PreviewConstructorAsync(filePath, position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What generating a property in front of a field would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewGeneratePropertyAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Generate Property", "No solution is open.");
        }

        return await new Refactoring.GenerateMemberRefactoring(solution)
            .PreviewPropertyAsync(filePath, position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What moving the type the caret is in to its own file would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewMoveTypeToFileAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Move Type to File", "No solution is open.");
        }

        return await new Refactoring.MoveTypeToFileRefactoring(solution)
            .PreviewAsync(filePath, position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The parameters of the method the caret is in.</summary>
    public async Task<IReadOnlyList<(string Name, string Type)>> GetParametersAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution) return [];

        return await new Refactoring.ChangeSignatureRefactoring(solution)
            .ParametersAsync(filePath, position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What changing a method's parameters, and its calls, would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewChangeSignatureAsync(
        string filePath, int position, Refactoring.SignatureChange change,
        CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Change Signature", "No solution is open.");
        }

        return await new Refactoring.ChangeSignatureRefactoring(solution)
            .PreviewAsync(filePath, position, change, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What converting an If to a Select Case, or back, would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewConvertConditionalAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Convert If and Select Case", "No solution is open.");
        }

        return await new Refactoring.ConvertConditionalRefactoring(solution)
            .PreviewAsync(filePath, position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What putting a variable's value back into its uses would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewInlineVariableAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Inline Variable", "No solution is open.");
        }

        return await new Refactoring.InlineVariableRefactoring(solution)
            .PreviewAsync(filePath, position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What naming the selected expression as a constant would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewExtractConstantAsync(
        string filePath, int start, int length, string name, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Extract Constant", "No solution is open.");
        }

        return await new Refactoring.ExtractVariableRefactoring(solution)
            .PreviewAsync(filePath, start, length, name, asConstant: true, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Works out what lifting the selected statements into a method would
    /// change.
    /// </summary>
    public async Task<Refactoring.RefactoringPreview> PreviewExtractMethodAsync(
        string filePath, int start, int length, string name, CancellationToken ct = default)
    {
        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Extract Method", "No solution is open.");
        }

        return await new Refactoring.ExtractMethodRefactoring(solution)
            .PreviewAsync(filePath, start, length, name, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Works out what tidying the file's imports would change.</summary>
    public async Task<Refactoring.RefactoringPreview> PreviewTidyImportsAsync(
        string filePath, CancellationToken ct = default)
    {
        // A .vbhtml is not a document Roslyn holds, so asking it would refuse
        // for the wrong reason. The template's own @Imports are sorted by the
        // Razor side, and the result is described the same way.
        if (filePath.EndsWith(".vbhtml", StringComparison.OrdinalIgnoreCase))
            return await TidyViewImportsAsync(filePath, ct).ConfigureAwait(false);

        if (_workspace?.CurrentSolution is not { } solution)
        {
            return Refactoring.RefactoringPreview.Refused(
                "Sort and Remove Imports", "No solution is open.");
        }

        return await new Refactoring.ImportsRefactoring(solution)
            .PreviewAsync(filePath, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adding the @Imports that would make a name in a view resolve.
    ///
    /// A template names a type by its short name and the compiler cannot find
    /// it; the namespace that declares it is somewhere in the solution, and
    /// the fix is one line at the top. Doing it by hand means knowing which
    /// namespace, which is the part the IDE can answer and the author cannot
    /// without going to look.
    /// </summary>
    public async Task<Refactoring.RefactoringPreview> PreviewAddImportAsync(
        string filePath, string typeName, CancellationToken ct = default)
    {
        const string title = "Add Imports";

        if (_workspace?.CurrentSolution is not { } solution)
            return Refactoring.RefactoringPreview.Refused(title, "No solution is open.");

        if (string.IsNullOrWhiteSpace(typeName))
            return Refactoring.RefactoringPreview.Refused(title, "There is no name here.");

        var declarations = await SymbolFinder
            .FindSourceDeclarationsAsync(
                solution,
                name => string.Equals(name, typeName, StringComparison.Ordinal),
                ct)
            .ConfigureAwait(false);

        // Only types: importing a namespace to reach a method is not how
        // Visual Basic resolves one.
        var namespaces = declarations
            .OfType<INamedTypeSymbol>()
            .Select(symbol => symbol.ContainingNamespace)
            .Where(space => space is { IsGlobalNamespace: false })
            .Select(space => space.ToDisplayString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (namespaces.Count == 0)
        {
            return Refactoring.RefactoringPreview.Refused(
                title, $"No namespace in this solution declares '{typeName}'.");
        }

        string original;

        try
        {
            original = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refactoring.RefactoringPreview.Refused(title, ex.Message);
        }

        // The first alphabetically when several would do: picking one is
        // better than refusing, and the preview shows what it chose.
        var chosen = namespaces[0];

        if (original.Contains($"@Imports {chosen}", StringComparison.OrdinalIgnoreCase))
        {
            return Refactoring.RefactoringPreview.Refused(
                title, $"'{chosen}' is already imported.");
        }

        var updated = Web.VbHtmlFormattingProvider.WithImport(original, chosen);

        return new Refactoring.RefactoringPreview($"Add '@Imports {chosen}'",
            [new Refactoring.FileChangePreview(filePath, original, updated)]);
    }

    /// <summary>Sorting the @Imports at the top of a Razor view.</summary>
    private static async Task<Refactoring.RefactoringPreview> TidyViewImportsAsync(
        string filePath, CancellationToken ct)
    {
        const string title = "Sort and Remove Imports";

        string original;

        try
        {
            original = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refactoring.RefactoringPreview.Refused(title, ex.Message);
        }

        var sorted = Web.VbHtmlFormattingProvider.SortImports(original, caret: 0);

        return sorted.Changed
            ? new Refactoring.RefactoringPreview(title,
                [new Refactoring.FileChangePreview(filePath, original, sorted.Text)])
            : Refactoring.RefactoringPreview.Refused(
                title, "The imports are already in order.");
    }

    /// <summary>
    /// Writes what a preview described.
    ///
    /// The files are written directly rather than through the workspace: the
    /// editor reloads from disk, and a workspace that disagreed with the files
    /// would be worse than one that follows them.
    /// </summary>
    public static async Task<bool> ApplyAsync(
        Refactoring.RefactoringPreview preview, CancellationToken ct = default)
    {
        if (!preview.CanApply) return false;

        foreach (var change in preview.Changes)
        {
            try
            {
                await File.WriteAllTextAsync(change.FilePath, change.NewText, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The fixes offered for the problems at a position.
    ///
    /// These come from Roslyn's own code fix providers, so "import the missing
    /// namespace" and "remove the unused import" behave as they do in Visual
    /// Studio rather than being reimplemented approximately here.
    /// </summary>
    public async Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        var document = GetDocument(filePath);
        if (document is null) return [];

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var caret = Math.Clamp(position, 0, text.Length);

        var diagnostics = await GetDiagnosticsAtAsync(document, caret, ct).ConfigureAwait(false);
        if (diagnostics.Count == 0) return [];

        var actions = new List<QuickAction>();

        foreach (var provider in _codeFixProviders.Value)
        {
            var fixable = diagnostics
                .Where(d => provider.FixableDiagnosticIds.Contains(d.Id))
                .ToList();

            if (fixable.Count == 0) continue;

            foreach (var diagnostic in fixable)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var context = new CodeFixContext(
                        document,
                        diagnostic,
                        (action, _) => actions.Add(new QuickAction(action.Title, action)),
                        ct);

                    await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A provider that fails should not deny the user the fixes
                    // the others are offering.
                }
            }
        }

        // Two providers can offer the same fix for overlapping diagnostics.
        return [.. actions
            .GroupBy(a => a.Title, StringComparer.Ordinal)
            .Select(group => group.First())];
    }

    /// <summary>Applies a quick action, returning the new text of the file.</summary>
    public async Task<string?> ApplyQuickActionAsync(
        string filePath, QuickAction action, CancellationToken ct = default)
    {
        var operations = await action.Action
            .GetOperationsAsync(ct)
            .ConfigureAwait(false);

        var applied = operations
            .OfType<ApplyChangesOperation>()
            .FirstOrDefault();

        if (applied is null) return null;

        var document = applied.ChangedSolution.GetDocument(GetDocument(filePath)?.Id);
        if (document is null) return null;

        return (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
    }

    /// <summary>The diagnostics whose span covers a position.</summary>
    private static async Task<IReadOnlyList<RoslynDiagnostic>> GetDiagnosticsAtAsync(
        Document document, int position, CancellationToken ct)
    {
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model is null) return [];

        return [.. model.GetDiagnostics(cancellationToken: ct)
            .Where(d => d.Location.SourceSpan.Start <= position
                     && position <= d.Location.SourceSpan.End)];
    }

    /// <summary>
    /// Roslyn's code fix providers, discovered once.
    ///
    /// They are found by reflection over the Features assemblies because the
    /// export attribute they use is internal, so MEF cannot be asked for them
    /// from outside Roslyn.
    /// </summary>
    private readonly Lazy<IReadOnlyList<CodeFixProvider>> _codeFixProviders =
        new(DiscoverCodeFixProviders);

    private static IReadOnlyList<CodeFixProvider> DiscoverCodeFixProviders()
    {
        var providers = new List<CodeFixProvider>();

        foreach (var assembly in new[]
                 {
                     LoadFeatureAssembly("Microsoft.CodeAnalysis.Features"),
                     LoadFeatureAssembly("Microsoft.CodeAnalysis.VisualBasic.Features")
                 })
        {
            if (assembly is null) continue;

            IEnumerable<Type> types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                // A partly loadable assembly still yields the types that did load.
                types = ex.Types.OfType<Type>();
            }

            foreach (var type in types)
            {
                if (!typeof(CodeFixProvider).IsAssignableFrom(type)) continue;
                if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null) continue;

                try
                {
                    if (Activator.CreateInstance(type) is CodeFixProvider provider)
                        providers.Add(provider);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Some providers need services we are not supplying.
                }
            }
        }

        return providers;
    }

    /// <summary>
    /// The documentation of a completion entry, fetched when it is selected.
    ///
    /// Kept apart from the completion list itself: fetching the XML comment of
    /// every candidate would mean reading the documentation of a whole
    /// namespace to show a dozen rows.
    /// </summary>
    public Task<string?> GetCompletionDescriptionAsync(
        string filePath, int position, string displayText, CancellationToken ct = default) =>
        GetCompletionDescriptionAsync(filePath, position, displayText, currentText: null, ct);

    /// <summary>Describes a completion in an unsaved or generated document.</summary>
    public async Task<string?> GetCompletionDescriptionAsync(
        string filePath, int position, string displayText, string? currentText,
        CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));
        if (document is null) return null;

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        var service = CompletionService.GetService(document);
        if (service is null) return null;

        var caret = Math.Clamp(position, 0, (await document.GetTextAsync(ct)
            .ConfigureAwait(false)).Length);

        var completions = await service
            .GetCompletionsAsync(document, caret, cancellationToken: ct)
            .ConfigureAwait(false);

        var item = completions.ItemsList
            .FirstOrDefault(i => i.DisplayText == displayText);

        if (item is null) return null;

        var description = await service
            .GetDescriptionAsync(document, item, ct)
            .ConfigureAwait(false);

        return description is null || description.TaggedParts.Length == 0
            ? null
            : string.Concat(description.TaggedParts.Select(p => p.Text));
    }

    /// <summary>Whether an argument list has been closed.</summary>
    private static bool EndsWithClosingBracket(SyntaxNode argumentList)
    {
        var last = argumentList.ChildNodesAndTokens().LastOrDefault();

        return last.IsToken && last.AsToken().Text is ")" or "]";
    }

    /// <summary>
    /// Which argument the caret is in, counted by the separators before it.
    ///
    /// Commas inside a nested call belong to that call, so only separators
    /// that are direct children of this list are counted.
    /// </summary>
    private static int CountArgumentsBefore(SyntaxNode argumentList, int caret, int listStart)
    {
        if (caret <= listStart) return 0;

        return argumentList
            .ChildNodesAndTokens()
            .Count(child => child.IsToken
                         && child.Span.End <= caret
                         && child.AsToken().Text == ",");
    }

    private static string? ExtractSummaryOrNull(string? xml) =>
        string.IsNullOrWhiteSpace(xml) ? null : ExtractSummary(xml);

        /// <summary>
    /// Composes the host services including the Features assemblies.
    ///
    /// Without this step MSBuildWorkspace registers only the Workspaces
    /// services: CompletionService.GetService returns null and there is no
    /// completion at all, neither in C# nor in VB.
    /// </summary>
    private static MefHostServices CreateHostServices()
    {
        var assemblies = MSBuildMefHostServices.DefaultAssemblies
            .Concat(new[]
            {
                typeof(Microsoft.CodeAnalysis.Completion.CompletionService).Assembly,

                LoadFeatureAssembly("Microsoft.CodeAnalysis.VisualBasic.Features")
            }.OfType<System.Reflection.Assembly>())
            .Distinct();

        return MefHostServices.Create(assemblies);
    }

    /// <summary>
    /// The per-language Features assemblies are not statically referenced by
    /// the code, so they have to be loaded by name.
    /// </summary>
    private static System.Reflection.Assembly? LoadFeatureAssembly(string name)
    {
        try
        {
            return System.Reflection.Assembly.Load(name);
        }
        catch (Exception)
        {
            // A missing language degrades completion without preventing the
            // solution from opening.
            return null;
        }
    }

    /// <summary>
    /// Corrects the casing of identifiers on one line to match the symbols they
    /// refer to, so "console.readline" becomes "Console.ReadLine".
    ///
    /// Unlike keyword casing this needs the semantic model, and therefore a
    /// project that resolves: it returns nothing for a file the workspace does
    /// not know, or while the surrounding code does not compile.
    /// </summary>
    public async Task<IReadOnlyList<Microsoft.CodeAnalysis.Text.TextChange>>
        GetIdentifierCasingChangesAsync(
            string filePath, string text, int line, CancellationToken ct = default)
    {
        var document = GetDocument(filePath);
        if (document is null) return [];

        // The in-memory text is ahead of the workspace while the user types.
        var updated = document.WithText(SourceText.From(text));

        var source = await updated.GetTextAsync(ct).ConfigureAwait(false);
        if (line < 0 || line >= source.Lines.Count) return [];

        return await VisualBasicCaseCorrector
            .GetIdentifierChangesAsync(updated, source.Lines[line].Span, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Symbols declared in a document, nested as they are written.
    ///
    /// Built from the syntax tree rather than the semantic model: an outline
    /// has to keep working while the file does not compile, which is most of
    /// the time while typing.
    /// </summary>
    public async Task<IReadOnlyList<Extensibility.DocumentSymbol>> GetDocumentSymbolsAsync(
        string filePath, CancellationToken ct = default)
    {
        var document = GetDocument(filePath);
        if (document is null) return [];

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        if (root is null) return [];

        return BuildSymbols(root, text);
    }

    /// <summary>Everywhere the symbol at a position is used.</summary>
    public Task<IReadOnlyList<Extensibility.SourceLocation>> FindReferencesAsync(
        string filePath, int position, CancellationToken ct = default) =>
        FindReferencesAsync(filePath, position, currentText: null, ct);

    /// <summary>
    /// Every use of the symbol at a position, optionally in text the
    /// workspace does not hold — generated Razor, for instance.
    /// </summary>
    public async Task<IReadOnlyList<Extensibility.SourceLocation>> FindReferencesAsync(
        string filePath, int position, string? currentText, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return [];

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, ct)
            .ConfigureAwait(false);
        if (symbol is null) return [];

        // The document's own solution, not the workspace's: a scratch document
        // lives in a fork the workspace does not know about, and searching the
        // workspace's solution for a symbol from the fork finds nothing.
        var found = await SymbolFinder
            .FindReferencesAsync(symbol, document.Project.Solution, ct)
            .ConfigureAwait(false);

        var locations = new List<Extensibility.SourceLocation>();

        // The declaration itself is a reference the user expects to see listed.
        foreach (var declaration in symbol.Locations.Where(l => l.IsInSource))
            locations.Add(ToLocation(declaration));

        foreach (var reference in found)
            foreach (var location in reference.Locations.Where(l => !l.Location.IsInMetadata))
                locations.Add(ToLocation(location.Location));

        return locations
            .DistinctBy(l => (l.FilePath, l.Range.Start.Line, l.Range.Start.Column))
            .OrderBy(l => l.FilePath)
            .ThenBy(l => l.Range.Start.Line)
            .ToList();
    }

    /// <summary>Symbols across the solution whose name contains the query.</summary>
    public async Task<IReadOnlyList<Extensibility.SourceLocation>> SearchSymbolsAsync(
        string query, CancellationToken ct = default)
    {
        if (_workspace is null || string.IsNullOrWhiteSpace(query)) return [];

        var found = await SymbolFinder
            .FindSourceDeclarationsAsync(
                _workspace.CurrentSolution,
                name => name.Contains(query, StringComparison.OrdinalIgnoreCase),
                ct)
            .ConfigureAwait(false);

        return found
            .SelectMany(symbol => symbol.Locations.Where(l => l.IsInSource))
            .Select(ToLocation)
            .Take(200)
            .ToList();
    }

    private static Extensibility.SourceLocation ToLocation(Location location)
    {
        var span = location.GetLineSpan();

        var start = new Extensibility.SourcePosition(
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
        var end = new Extensibility.SourcePosition(
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);

        return new Extensibility.SourceLocation(span.Path, new Extensibility.SourceRange(start, end));
    }

    /// <summary>
    /// Walks the syntax tree collecting declarations.
    ///
    /// Both languages are handled by kind name rather than by typed nodes: the
    /// C# and Visual Basic syntax trees have no common base for declarations,
    /// and matching names keeps one implementation instead of two.
    /// </summary>
    private static List<Extensibility.DocumentSymbol> BuildSymbols(SyntaxNode node, SourceText text)
    {
        var symbols = new List<Extensibility.DocumentSymbol>();

        foreach (var child in node.ChildNodes())
        {
            var kind = ClassifyDeclaration(child);

            if (kind is null)
            {
                // Not a declaration itself, but may contain some.
                symbols.AddRange(BuildSymbols(child, text));
                continue;
            }

            var name = DeclarationName(child);
            if (name is null) continue;

            symbols.Add(new Extensibility.DocumentSymbol(name, kind.Value, RangeOf(child, text))
            {
                Children = BuildSymbols(child, text)
            });
        }

        return symbols;
    }

    /// <summary>
    /// Name of a node's kind, for either language.
    ///
    /// The typed Kind() extensions are per-language, so the shared raw value is
    /// resolved through whichever language's SyntaxFacts owns the node.
    /// </summary>
    private static string KindName(SyntaxNode node) => KindName(node.RawKind, node.Language);

    private static string KindName(SyntaxToken token, string language) =>
        KindName(token.RawKind, language);

    /// <summary>
    /// The name of a syntax kind.
    ///
    /// Visual Basic only: it is the one Roslyn language Basalt registers, so
    /// the C# branch that was here could never be reached.
    /// </summary>
    private static string KindName(int rawKind, string language) =>
        ((Microsoft.CodeAnalysis.VisualBasic.SyntaxKind)rawKind).ToString();

    private static Extensibility.SymbolKind? ClassifyDeclaration(SyntaxNode node) =>
        KindName(node) switch
        {
            "NamespaceDeclaration" or "FileScopedNamespaceDeclaration" or "NamespaceBlock"
                => Extensibility.SymbolKind.Namespace,
            "ClassDeclaration" or "ClassBlock" => Extensibility.SymbolKind.Class,
            "StructDeclaration" or "StructureBlock" => Extensibility.SymbolKind.Structure,
            "InterfaceDeclaration" or "InterfaceBlock" => Extensibility.SymbolKind.Interface,
            "EnumDeclaration" or "EnumBlock" => Extensibility.SymbolKind.Enum,
            "ModuleBlock" => Extensibility.SymbolKind.Module,
            "DelegateDeclaration" => Extensibility.SymbolKind.Delegate,
            "MethodDeclaration" or "SubBlock" or "ConstructorDeclaration" or "ConstructorBlock"
                => Extensibility.SymbolKind.Method,
            "FunctionBlock" => Extensibility.SymbolKind.Function,
            "PropertyDeclaration" or "PropertyBlock" or "PropertyStatement"
                => Extensibility.SymbolKind.Property,
            "FieldDeclaration" => Extensibility.SymbolKind.Field,
            "EventDeclaration" or "EventBlock" or "EventStatement"
                => Extensibility.SymbolKind.Event,
            _ => null
        };

    /// <summary>
    /// The declared name, found by looking for the identifier the declaration
    /// carries. Fields declare several names at once; the first stands for the
    /// group.
    /// </summary>
    private static string? DeclarationName(SyntaxNode node)
    {
        // A block-bodied declaration in Visual Basic keeps its name on the
        // statement that opens it.
        var language = node.Language;

        var carrier = node.ChildNodes().FirstOrDefault(c =>
            KindName(c).EndsWith("Statement", StringComparison.Ordinal)) ?? node;

        var identifier = carrier.DescendantTokens()
            .FirstOrDefault(t => KindName(t, language) == "IdentifierToken");

        if (identifier.ValueText.Length > 0) return identifier.ValueText;

        identifier = node.DescendantTokens()
            .FirstOrDefault(t => KindName(t, language) == "IdentifierToken");

        return identifier.ValueText.Length > 0 ? identifier.ValueText : null;
    }

    private static Extensibility.SourceRange RangeOf(SyntaxNode node, SourceText text)
    {
        var span = text.Lines.GetLinePositionSpan(node.Span);

        return new Extensibility.SourceRange(
            new Extensibility.SourcePosition(span.Start.Line + 1, span.Start.Character + 1),
            new Extensibility.SourcePosition(span.End.Line + 1, span.End.Character + 1));
    }

    /// <summary>
    /// A Visual Basic document in a throwaway project, for text the open
    /// solution does not contain.
    ///
    /// It carries the references of a project in the solution where there is
    /// one, so completion on generated Razor sees the same types the real
    /// code sees; failing that, the framework alone.
    /// </summary>
    /// <summary>
    /// Every assembly the running framework offers.
    ///
    /// Read once: there are a couple of hundred, and loading them again for
    /// each keystroke would be felt.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> FrameworkReferences =
        new(() =>
        {
            var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";

            var references = new List<MetadataReference>();

            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (path.Length == 0) continue;

                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException)
                {
                    // A file that cannot be read contributes nothing and must
                    // not cost the rest.
                }
            }

            return references.Count > 0
                ? references
                : [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];
        });

    /// <summary>
    /// How a scratch project is compiled.
    ///
    /// The imports a Visual Basic project gets from its SDK. Without them
    /// "Console" is an undeclared name: it lives in System, and a project
    /// file supplies that import through MSBuild, which a project built by
    /// hand does not have. Typing "Console." offered nothing, and hovering
    /// it said nothing — both because the name did not resolve.
    /// </summary>
    private static VisualBasicCompilationOptions ScratchOptions { get; } =
        new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithGlobalImports(GlobalImport.Parse(
                "System",
                "System.Collections",
                "System.Collections.Generic",
                "System.Diagnostics",
                "System.Linq",
                "Microsoft.VisualBasic"))
            .WithOptionExplicit(true)
            .WithOptionInfer(true)

            // Off, as a project template has it: a scratch file being written
            // is full of things that are not yet declared, and refusing to
            // answer for them helps nobody.
            .WithOptionStrict(OptionStrict.Off);

    private Document? ScratchDocument(string text)
    {
        // Inside the user's own project when there is one. A separate
        // workspace can resolve String and Integer, because those come from
        // metadata, but not the model class in the file next door: its symbol
        // lives in source this workspace does not contain, so go-to-definition
        // finds no location to open and silently does nothing. Adding the
        // document to the real project is what makes the view's own types
        // visible.
        var host = _workspace?.CurrentSolution.Projects
            .FirstOrDefault(p => p.Language == LanguageNames.VisualBasic);

        if (host is not null)
        {
            return host
                .AddDocument("__BasaltScratch.vb", SourceText.From(text))
                .WithFilePath(Path.Combine(
                    Path.GetDirectoryName(host.FilePath) ?? ".", "__BasaltScratch.vb"));
        }

        // No solution open: the whole framework, so a file opened on its own
        // still knows what Console is. Referencing only the assembly holding
        // Object answered for String and Integer and for nothing else —
        // typing "Console." offered an empty list, which reads exactly like
        // completion being broken.
        var workspace = new AdhocWorkspace();

        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "BasaltScratch",
            "BasaltScratch",
            LanguageNames.VisualBasic,
            metadataReferences: FrameworkReferences.Value)
            .WithCompilationOptions(ScratchOptions));

        return workspace.AddDocument(project.Id, "Scratch.vb", SourceText.From(text));
    }

    private Document? GetDocument(string filePath)
    {
        var id = FindDocumentId(filePath);
        return id is null ? null : _workspace?.CurrentSolution.GetDocument(id);
    }

    private DocumentId? FindDocumentId(string filePath)
    {
        if (_workspace is null) return null;

        var full = Path.GetFullPath(filePath);
        return _workspace.CurrentSolution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath is not null
                                 && string.Equals(Path.GetFullPath(d.FilePath), full, PathComparison))
            ?.Id;
    }

    /// <summary>On Windows and macOS paths are case-insensitive, on Linux they are not.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static IdeDiagnostic ToIdeDiagnostic(RoslynDiagnostic d)
    {
        var span = d.Location.GetLineSpan();
        return new IdeDiagnostic(
            d.Id,
            d.GetMessage(),
            d.Severity switch
            {
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error => Basalt.Core.Model.DiagnosticSeverity.Error,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => Basalt.Core.Model.DiagnosticSeverity.Warning,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Info => Basalt.Core.Model.DiagnosticSeverity.Info,
                _ => Basalt.Core.Model.DiagnosticSeverity.Hidden
            },
            d.Location.IsInSource ? span.Path : null,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
    }

    private static IdeCompletionItem ToCompletionItem(RoslynCompletionItem item)
    {
        // Roslyn describes the item kind through textual tags shared between C# and VB.
        var kind = item.Tags.Length == 0 ? CompletionKind.Other : item.Tags[0] switch
        {
            WellKnownTags.Class => CompletionKind.Class,
            WellKnownTags.Structure => CompletionKind.Structure,
            WellKnownTags.Interface => CompletionKind.Interface,
            WellKnownTags.Enum => CompletionKind.Enum,
            WellKnownTags.Method => CompletionKind.Method,
            WellKnownTags.Property => CompletionKind.Property,
            WellKnownTags.Field => CompletionKind.Field,
            WellKnownTags.Event => CompletionKind.Event,
            WellKnownTags.Local => CompletionKind.Local,
            WellKnownTags.Parameter => CompletionKind.Parameter,
            WellKnownTags.Namespace => CompletionKind.Namespace,
            WellKnownTags.Keyword => CompletionKind.Keyword,
            WellKnownTags.Snippet => CompletionKind.Snippet,
            _ => CompletionKind.Other
        };

        return new IdeCompletionItem(
            item.DisplayText,
            string.IsNullOrEmpty(item.FilterText) ? item.DisplayText : item.FilterText,
            kind)
        {
            IsPreselected = item.Rules.MatchPriority == MatchPriority.Preselect,
        };
    }

    /// <summary>
    /// The edit committing an entry makes, as Roslyn computes it for Visual
    /// Studio: the change, not the text the list filtered on.
    /// </summary>
    private static Task<CompletionCommit?> CommitAsync(
        CompletionService service, Document document, RoslynCompletionItem item, CancellationToken ct) =>
        Task.Run(async () =>
        {
            var change = await service.GetChangeAsync(document, item, commitCharacter: null, ct).ConfigureAwait(false);
            var edit = change.TextChange;

            return (CompletionCommit?)new CompletionCommit(edit.Span.Start, edit.Span.Length, edit.NewText ?? "", change.NewPosition);
        }, ct);

    /// <summary>
    /// Whether typing a character here should open the completion list, as
    /// Roslyn decides it for Visual Studio: a space after As, New, Of,
    /// Implements, Inherits, Imports and the rest opens it; a space in the
    /// middle of a statement does not. Asked rather than listed here, so
    /// the editor holds no second opinion about Visual Basic.
    /// </summary>
    public async Task<bool> ShouldTriggerCompletionAsync(
        string filePath, int position, string? currentText, char typed, CancellationToken ct = default)
    {
        var document = GetDocument(filePath)
                    ?? (currentText is null ? null : ScratchDocument(currentText));

        if (document is null) return false;

        if (currentText is not null)
            document = document.WithText(SourceText.From(currentText));

        return await Task.Run(
            async () =>
            {
                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                var service = CompletionService.GetService(document);

                return service is not null && service.ShouldTriggerCompletion(
                    text, Math.Clamp(position, 0, text.Length), CompletionTrigger.CreateInsertionTrigger(typed));
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>Extracts the text of the summary tag from the XML documentation comment.</summary>
    private static string ExtractSummary(string xml)
    {
        const string open = "<summary>";
        const string close = "</summary>";

        var start = xml.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return xml.Trim();

        start += open.Length;
        var end = xml.IndexOf(close, start, StringComparison.Ordinal);
        if (end < 0) return xml[start..].Trim();

        return string.Join(' ',
            xml[start..end].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _gate.Dispose();
    }
}
