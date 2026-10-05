using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>One recognized designer resource or property operation in source order.</summary>
public sealed record WinFormsDesignerResourceOperation(
    string ControlName,
    string? PropertyName,
    string? ResourceKey,
    ExpressionSyntax? Expression);

public sealed partial class WinFormsDesignerDocument
{
    /// <summary>
    /// Direct property assignments and trusted ComponentResourceManager calls from InitializeComponent.
    /// An ApplyResources operation has a null property and expression; its resource key is the prefix.
    /// </summary>
    public IReadOnlyList<WinFormsDesignerResourceOperation> ResourceOperations { get; }

    private IReadOnlyList<WinFormsDesignerResourceOperation> ReadResourceOperations()
    {
        var controlNames = Controls.Select(control => control.Name)
            .Append("Me")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localNames = _initialize.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(declaration => declaration.Declarators)
            .SelectMany(declarator => declarator.Names)
            .Select(name => name.Identifier.ValueText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operations = new List<WinFormsDesignerResourceOperation>();

        foreach (var statement in _initialize.Statements)
        {
            if (statement is LocalDeclarationStatementSyntax declaration)
            {
                AddTrustedManagers(declaration, managers);
                continue;
            }

            if (statement is AssignmentStatementSyntax assignment)
            {
                if (assignment.Left is IdentifierNameSyntax local)
                    managers.Remove(local.Identifier.ValueText);

                if (assignment.Left is not MemberAccessExpressionSyntax property
                    || DirectControlName(property.Expression, localNames) is not { } owner
                    || !controlNames.Contains(owner))
                    continue;

                var propertyName = property.Name.Identifier.ValueText;
                if (owner.Equals("Me", StringComparison.OrdinalIgnoreCase)
                    && DeclaredNames.Contains(propertyName))
                    continue;

                operations.Add(new WinFormsDesignerResourceOperation(
                    owner, propertyName, ResourceLookupKey(assignment.Right, managers), assignment.Right));
                continue;
            }

            var invocation = statement switch
            {
                ExpressionStatementSyntax { Expression: InvocationExpressionSyntax expression } => expression,
                CallStatementSyntax { Invocation: InvocationExpressionSyntax call } => call,
                _ => null,
            };
            if (invocation is not null
                && TryApplyResources(invocation, managers, controlNames, localNames,
                    out var controlName, out var prefix))
            {
                operations.Add(new WinFormsDesignerResourceOperation(controlName, null, prefix, null));
            }
        }

        return operations;
    }

    private void AddTrustedManagers(LocalDeclarationStatementSyntax declaration, ISet<string> managers)
    {
        foreach (var declarator in declaration.Declarators)
        {
            var creation = declarator.Initializer?.Value as ObjectCreationExpressionSyntax
                ?? (declarator.AsClause as AsNewClauseSyntax)?.NewExpression as ObjectCreationExpressionSyntax;
            if (creation is null || !IsCurrentClassResourceManager(creation))
                continue;

            foreach (var name in declarator.Names)
                managers.Add(name.Identifier.ValueText);
        }
    }

    private bool IsCurrentClassResourceManager(ObjectCreationExpressionSyntax creation)
    {
        var type = StripGlobalPrefix(creation.Type.ToString());
        if (!type.Equals("System.ComponentModel.ComponentResourceManager", StringComparison.OrdinalIgnoreCase))
            return false;

        var arguments = creation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray();
        if (arguments is not [{ Expression: GetTypeExpressionSyntax targetType }])
            return false;

        var declaredNamespace = string.Join(".", _class.Ancestors().OfType<NamespaceBlockSyntax>()
            .Reverse().Select(block => block.NamespaceStatement.Name.ToString()));
        var declaredType = string.IsNullOrEmpty(declaredNamespace)
            ? ClassName : declaredNamespace + "." + ClassName;
        var target = StripGlobalPrefix(targetType.Type.ToString());
        return target.Equals(ClassName, StringComparison.OrdinalIgnoreCase)
            || target.Equals(declaredType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryApplyResources(InvocationExpressionSyntax invocation,
        IReadOnlySet<string> managers, IReadOnlySet<string> controlNames,
        IReadOnlySet<string> localNames,
        out string controlName, out string prefix)
    {
        controlName = "";
        prefix = "";
        if (invocation.Expression is not MemberAccessExpressionSyntax
            { Expression: IdentifierNameSyntax receiver } method
            || !managers.Contains(receiver.Identifier.ValueText)
            || !method.Name.Identifier.ValueText.Equals("ApplyResources", StringComparison.OrdinalIgnoreCase))
            return false;

        var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray();
        if (arguments is not [{ Expression: var control }, { Expression: var key }]
            || DirectControlName(control, localNames) is not { } name
            || !controlNames.Contains(name)
            || StringLiteral(key) is not { } resourcePrefix)
            return false;

        controlName = name;
        prefix = resourcePrefix;
        return true;
    }

    private static string? ResourceLookupKey(ExpressionSyntax expression, IReadOnlySet<string> managers)
    {
        var inner = expression switch
        {
            CTypeExpressionSyntax cast => cast.Expression,
            DirectCastExpressionSyntax cast => cast.Expression,
            ParenthesizedExpressionSyntax parentheses => parentheses.Expression,
            _ => expression,
        };
        if (!ReferenceEquals(inner, expression))
            return ResourceLookupKey(inner, managers);

        if (expression is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } method,
            } invocation
            || !managers.Contains(receiver.Identifier.ValueText)
            || !method.Name.Identifier.ValueText.Equals("GetObject", StringComparison.OrdinalIgnoreCase)
                && !method.Name.Identifier.ValueText.Equals("GetString", StringComparison.OrdinalIgnoreCase))
            return null;

        var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray();
        return arguments is [{ Expression: var key }] ? StringLiteral(key) : null;
    }

    private static string? DirectControlName(ExpressionSyntax expression,
        IReadOnlySet<string> localNames) => expression switch
        {
            MeExpressionSyntax => "Me",
            MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } field =>
                field.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier when !localNames.Contains(identifier.Identifier.ValueText) =>
                identifier.Identifier.ValueText,
            _ => null,
        };

    private static string? StringLiteral(ExpressionSyntax expression) => expression is LiteralExpressionSyntax literal
        && literal.Token.Value is string text ? text : null;

    private static string StripGlobalPrefix(string typeName) =>
        typeName.StartsWith("Global.", StringComparison.OrdinalIgnoreCase)
            ? typeName["Global.".Length..] : typeName;
}
