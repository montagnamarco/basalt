using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Lifting a run of statements into a method of their own.
///
/// The signature is not guessed: Roslyn's data flow analysis says which
/// locals and parameters the selection reads from outside (they become
/// parameters), which it writes that the rest of the method still needs (a
/// local comes back as the return value, a parameter is passed ByRef), and
/// which type parameters of a generic method it uses (they are declared on the
/// new method). A selection that needs to hand back more than one local is
/// refused rather than turned into ByRef parameters behind the user's back.
/// </summary>
public sealed class ExtractMethodRefactoring
{
    private const string Title = "Extract Method";

    private readonly Solution _solution;

    public ExtractMethodRefactoring(Solution solution) => _solution = solution;

    /// <summary>A parameter of the new method, and whether the caller's variable is passed by reference.</summary>
    private readonly record struct Parameter(ISymbol Symbol, bool ByRef);

    /// <summary>What the new method looks like, decided once from the analysis.</summary>
    private sealed record Signature(
        string Name,
        IReadOnlyList<Parameter> Parameters,
        ILocalSymbol? Returned,
        string? ReturnType,
        IReadOnlyList<ITypeParameterSymbol> TypeParameters,
        bool IsAsync);

    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int start, int length, string name, CancellationToken ct = default)
    {
        if (!RenameRefactoring.IsValidName(name))
            return RefactoringPreview.Refused(Title, $"'{name}' is not a valid name.");

        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(Title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                Title, "Extracting a method is only supported in Visual Basic so far.");
        }

        if (length <= 0)
            return RefactoringPreview.Refused(Title, "Select the statements to extract.");

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null || model is null)
            return RefactoringPreview.Refused(Title, "The file could not be read.");

        var span = new TextSpan(start, length);
        var statements = StatementsIn(root, span);

        if (statements.Count == 0)
        {
            return RefactoringPreview.Refused(
                Title, "That selection is not a whole statement.");
        }

        if (statements[0].FirstAncestorOrSelf<MethodBlockBaseSyntax>() is not { } source)
        {
            return RefactoringPreview.Refused(
                Title, "Those statements are not inside a method.");
        }

        // A Return inside the selection would return from the new method, not
        // from the one it was lifted out of: a change in meaning the user is
        // told about rather than handed silently.
        var control = model.AnalyzeControlFlow(statements[0], statements[^1]);

        if (control is null || !control.Succeeded)
            return RefactoringPreview.Refused(Title, "That selection could not be analysed.");

        if (control.ReturnStatements.Length > 0)
        {
            return RefactoringPreview.Refused(
                Title, "That selection returns from the method, so it cannot be lifted out of it.");
        }

        // A Yield makes the enclosing method an iterator; a method of its own would not be one.
        if (statements.Any(s => s.DescendantNodesAndSelf().OfType<YieldStatementSyntax>().Any()))
            return RefactoringPreview.Refused(Title, Localizer.Get(StringKeys.RefactorExtractMethodYield));

        var flow = model.AnalyzeDataFlow(statements[0], statements[^1]);

        if (flow is null || !flow.Succeeded)
            return RefactoringPreview.Refused(Title, "That selection could not be analysed.");

        // Written inside and still read afterwards: the value has to come back. A local is returned, whether it
        // was declared outside the selection or inside it (then the call declares it).
        var returned = flow.DataFlowsOut
            .OfType<ILocalSymbol>()
            .ToList();

        if (returned.Count > 1)
        {
            var names = string.Join(", ", returned.Select(r => $"'{r.Name}'"));

            return RefactoringPreview.Refused(
                Title, $"That selection would have to return {names}, which is more than one value.");
        }

        // A parameter of the enclosing method written inside and read afterwards cannot be returned
        // without losing the others: it is passed ByRef, so the write reaches the caller's variable.
        var writtenParameters = flow.DataFlowsOut
            .OfType<IParameterSymbol>()
            .Where(p => !flow.VariablesDeclared.Contains(p, SymbolEqualityComparer.Default))
            .ToList();

        // Read inside but coming from outside: they become parameters. DataFlowsIn covers the enclosing
        // method's own parameters too, since one read inside the selection flows in like any other variable.
        // Me is not a parameter to pass: the new method is an instance member and has its own Me. A selection
        // in a property getter that reads a field reads Me implicitly, so it reaches the flow analysis.
        var readFromOutside = flow.DataFlowsIn
            .Where(s => s is ILocalSymbol or IParameterSymbol)
            .Where(s => !string.Equals(s.Name, "Me", StringComparison.OrdinalIgnoreCase))
            .Where(s => !flow.VariablesDeclared.Contains(s, SymbolEqualityComparer.Default))
            .ToList();

        var parameters = readFromOutside
            .Select(s => new Parameter(s, ByRef: s is IParameterSymbol p && writtenParameters.Contains(p, SymbolEqualityComparer.Default)))
            .Concat(writtenParameters
                .Where(p => !readFromOutside.Contains(p, SymbolEqualityComparer.Default))
                .Select(p => new Parameter(p, ByRef: true)))
            .ToList();

        var isAsync = statements.Any(s => AwaitsOutsideLambda(s));

        // Visual Basic allows no ByRef parameter on an Async method (BC36927), nor the use of one inside a
        // lambda (BC36639): the write could not come back, so the selection is refused rather than miscompiled.
        if (parameters.Where(p => p.ByRef).Select(p => p.Symbol).FirstOrDefault() is { } byRef)
        {
            if (isAsync)
            {
                return RefactoringPreview.Refused(
                    Title, Localizer.Get(StringKeys.RefactorExtractMethodAsyncByRef, byRef.Name));
            }

            if (UsedInLambda(model, statements, byRef))
            {
                return RefactoringPreview.Refused(
                    Title, Localizer.Get(StringKeys.RefactorExtractMethodLambdaByRef, byRef.Name));
            }
        }

        var method = model.GetDeclaredSymbol(source) as IMethodSymbol;
        var typeParameters = TypeParametersUsed(model, method, statements, parameters, returned.FirstOrDefault());

        // A function returns the local, and an async one wraps it in a Task. A selection with no value is a Sub,
        // or a Task-returning Function when it awaits.
        var returnType = returned.FirstOrDefault()?.Type.ToDisplayString();
        if (isAsync)
            returnType = TaskTypeName(model, statements[0].SpanStart, returnType);

        var signature = new Signature(
            name, parameters, returned.FirstOrDefault(), returnType, typeParameters, isAsync);

        var updated = Rewrite(text, statements, source, signature);

        var kind = returnType is null ? "Sub" : "Function";

        return new RefactoringPreview(
            $"Extract {statements.Count} statement{(statements.Count == 1 ? "" : "s")} into {kind} {name}",
            [new FileChangePreview(filePath, text.ToString(), updated)]);
    }

    /// <summary>
    /// The statements the selection covers, in order.
    ///
    /// Empty when the selection cuts a statement in half: extracting part of
    /// one would produce code that does not compile.
    /// </summary>
    private static List<StatementSyntax> StatementsIn(SyntaxNode root, TextSpan span)
    {
        var all = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(s => s.Parent is MethodBlockBaseSyntax or MultiLineIfBlockSyntax or ElseBlockSyntax
                     || s.Parent is DoLoopBlockSyntax or ForBlockSyntax or ForEachBlockSyntax
                     || s.Parent is WhileBlockSyntax or SelectBlockSyntax or CaseBlockSyntax)
            .Where(s => span.Contains(s.Span))
            .ToList();

        // Keep only the outermost: a For block and the statements inside it
        // would otherwise both be listed, and the body would be extracted twice.
        var outermost = all
            .Where(s => !all.Any(other => other != s && other.Span.Contains(s.Span)))
            .OrderBy(s => s.SpanStart)
            .ToList();

        if (outermost.Count == 0) return [];

        // The selection has to cover them fully and start at the first one:
        // trailing text that is not a statement means a partial selection.
        var covered = TextSpan.FromBounds(outermost[0].SpanStart, outermost[^1].Span.End);

        return span.Contains(covered) ? outermost : [];
    }

    /// <summary>
    /// Whether the statement awaits, in the method being extracted rather than in a lambda inside it: an
    /// await in a lambda belongs to that lambda and does not make the new method async.
    /// </summary>
    private static bool AwaitsOutsideLambda(StatementSyntax statement) =>
        statement.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>()
            .Any(await => await.FirstAncestorOrSelf<LambdaExpressionSyntax>() is null);

    /// <summary>Whether a lambda inside the selection names the symbol.</summary>
    private static bool UsedInLambda(SemanticModel model, IReadOnlyList<StatementSyntax> statements, ISymbol symbol) =>
        statements
            .SelectMany(s => s.DescendantNodes().OfType<LambdaExpressionSyntax>())
            .SelectMany(lambda => lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Any(identifier => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol));

    /// <summary>
    /// The method type parameters the new method needs: those the selection names, the parameters carry, or the
    /// return type uses. Declared on the new method in the order the source declares them.
    /// </summary>
    private static IReadOnlyList<ITypeParameterSymbol> TypeParametersUsed(
        SemanticModel model, IMethodSymbol? method, List<StatementSyntax> statements,
        List<Parameter> parameters, ILocalSymbol? returned)
    {
        if (method is null || method.TypeParameters.Length == 0) return [];

        var used = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var identifier in statements.SelectMany(s => s.DescendantNodesAndSelf()).OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(identifier).Symbol is ITypeParameterSymbol typeParameter)
                AddTypeParameters(typeParameter, used);
        }

        foreach (var parameter in parameters)
            AddTypeParameters(TypeOf(parameter.Symbol), used);

        if (returned is not null) AddTypeParameters(returned.Type, used);

        return method.TypeParameters.Where(used.Contains).ToList();
    }

    /// <summary>Adds the type parameters a type uses, and those its constraints use.</summary>
    private static void AddTypeParameters(ITypeSymbol? type, HashSet<ISymbol> into)
    {
        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                AddTypeParameters(typeParameter, into);
                break;

            case IArrayTypeSymbol array:
                AddTypeParameters(array.ElementType, into);
                break;

            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments) AddTypeParameters(argument, into);
                break;
        }
    }

    private static void AddTypeParameters(ITypeParameterSymbol typeParameter, HashSet<ISymbol> into)
    {
        if (!into.Add(typeParameter)) return;

        foreach (var constraint in typeParameter.ConstraintTypes)
            AddTypeParameters(constraint, into);
    }

    /// <summary>
    /// The Task a new async method returns, as source names it: "Task" when Imports brings it into scope,
    /// otherwise the full name, so the method compiles either way.
    /// </summary>
    private static string TaskTypeName(SemanticModel model, int position, string? resultType)
    {
        var inScope = model.LookupNamespacesAndTypes(position, name: "Task")
            .OfType<INamedTypeSymbol>()
            .Any(type => type.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks");

        var task = inScope ? "Task" : "System.Threading.Tasks.Task";

        return resultType is null ? task : $"{task}(Of {resultType})";
    }

    /// <summary>
    /// Writes the call in place of the statements, and the method after the
    /// member they came from.
    ///
    /// Text rather than tree, as with extracting a variable: the rest of the
    /// file keeps the formatting it had.
    /// </summary>
    private static string Rewrite(
        SourceText text,
        List<StatementSyntax> statements,
        MethodBlockBaseSyntax source,
        Signature signature)
    {
        var lineEnding = text.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        // A property accessor is not a member of its own: the method goes after the property, not inside it,
        // at the property's indentation.
        SyntaxNode member = source.Parent is PropertyBlockSyntax property ? property : source;

        var methodIndent = new string(' ', IndentOf(text, member.SpanStart));
        var bodyIndent = methodIndent + "    ";

        var name = signature.Name;
        var typeArguments = signature.TypeParameters.Count == 0
            ? ""
            : $"(Of {string.Join(", ", signature.TypeParameters.Select(p => p.Name))})";

        // The replacement starts at the first statement, after the indentation
        // the line already has: the call must not add that indentation again,
        // or it lands at twice the depth it had.
        var arguments = string.Join(", ", signature.Parameters.Select(p => p.Symbol.Name));
        var invocation = $"{(signature.IsAsync ? "Await " : "")}{name}{typeArguments}({arguments})";

        var returned = signature.Returned;

        var call = returned is null
            ? invocation
            : $"{returned.Name} = {invocation}";

        // A local that is declared inside the selection and returned needs its
        // Dim at the call site, since the declaration moved into the method.
        if (returned is not null && statements.Any(s => Declares(s, returned)))
            call = $"Dim {call}";

        var parameterList = string.Join(", ", signature.Parameters.Select(
            p => $"{(p.ByRef ? "ByRef " : "")}{p.Symbol.Name} As {TypeNameOf(p.Symbol)}"));

        var typeParameterList = signature.TypeParameters.Count == 0
            ? ""
            : $"(Of {string.Join(", ", signature.TypeParameters.Select(ConstrainedDeclaration(source)))})";

        var body = string.Join(lineEnding, statements.Select(
            s => bodyIndent + Reindent(text, s, bodyIndent, lineEnding)));

        var modifiers = signature.IsAsync ? "Private Async" : "Private";

        // Two line endings in front: the first ends the member's End line,
        // the second leaves one blank line between the methods. None after the
        // last line, since the file already has the line ending that follows
        // the member's End line.
        var header = signature.ReturnType is null
            ? $"{modifiers} Sub {name}{typeParameterList}({parameterList})"
            : $"{modifiers} Function {name}{typeParameterList}({parameterList}) As {signature.ReturnType}";

        var ending = signature.ReturnType is null ? "End Sub" : "End Function";

        // The value is returned after the statements; a Task-returning function that returns nothing needs no Return.
        var returnLine = returned is null
            ? ""
            : $"{lineEnding}{bodyIndent}Return {returned.Name}";

        var methodText = $"{lineEnding}{lineEnding}{methodIndent}{header}{lineEnding}"
            + $"{body}{returnLine}{lineEnding}{methodIndent}{ending}";

        var extracted = TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End);

        // The method is inserted after the statements are replaced, so its
        // position is worked out against the text as it will then be.
        var afterMember = member.Span.End;
        var callDelta = call.Length - extracted.Length;

        var replaced = text.Replace(extracted, call);

        return replaced
            .Replace(new TextSpan(afterMember + callDelta, 0), methodText)
            .ToString();
    }

    /// <summary>
    /// Each type parameter as the source declares it, constraints included, so the new method
    /// constrains the type exactly as the original did.
    /// </summary>
    private static Func<ITypeParameterSymbol, string> ConstrainedDeclaration(MethodBlockBaseSyntax source)
    {
        var declarations = source.DescendantNodes().OfType<TypeParameterSyntax>().ToList();

        return symbol =>
        {
            var declaration = declarations.FirstOrDefault(d =>
                string.Equals(d.Identifier.ValueText, symbol.Name, StringComparison.OrdinalIgnoreCase));

            return declaration?.ToString().Trim() ?? symbol.Name;
        };
    }

    /// <summary>The type a parameter or local has.</summary>
    private static ITypeSymbol? TypeOf(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        _ => null
    };

    /// <summary>Whether a statement declares the given local.</summary>
    private static bool Declares(StatementSyntax statement, ILocalSymbol local) =>
        statement is LocalDeclarationStatementSyntax declaration
        && declaration.Declarators
            .SelectMany(d => d.Names)
            .Any(n => string.Equals(n.Identifier.ValueText, local.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>How far a line is indented.</summary>
    private static int IndentOf(SourceText text, int position) =>
        position - text.Lines.GetLineFromPosition(position).Start;

    /// <summary>
    /// A statement's text with its inner lines lined up under the new indent.
    ///
    /// The first line is left to the caller, which already wrote the indent.
    /// </summary>
    private static string Reindent(
        SourceText text, StatementSyntax statement, string bodyIndent, string lineEnding)
    {
        var lines = statement.ToString().Replace("\r\n", "\n").Split('\n');

        if (lines.Length == 1) return lines[0];

        var original = new string(' ', IndentOf(text, statement.SpanStart));

        var rest = lines.Skip(1).Select(line =>
            line.StartsWith(original, StringComparison.Ordinal)
                ? bodyIndent + line[original.Length..]
                : line);

        return string.Join(lineEnding, new[] { lines[0] }.Concat(rest));
    }

    /// <summary>The declared type of a parameter or local, as source writes it.</summary>
    private static string TypeNameOf(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type.ToDisplayString(),
        IParameterSymbol parameter => parameter.Type.ToDisplayString(),
        _ => "Object"
    };
}
