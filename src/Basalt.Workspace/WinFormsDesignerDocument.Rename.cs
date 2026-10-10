using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed partial class WinFormsDesignerDocument
{
    /// <summary>
    /// Rewrites the resource keys a renamed control owns in InitializeComponent: the ApplyResources key
    /// ("Old" becomes "New") and the GetObject/GetString keys under "Old." ("Old.Image" becomes "New.Image").
    /// Only the trusted ComponentResourceManager calls are rewritten, so a Text literal that happens to equal
    /// the old name stays as the user wrote it.
    /// </summary>
    public string RenameResourceKeys(string oldName, string newName)
    {
        ValidateIdentifier(newName);
        var managers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in _initialize.Statements.OfType<LocalDeclarationStatementSyntax>())
            AddTrustedManagers(declaration, managers);

        var changes = new List<TextChange>();
        foreach (var invocation in _initialize.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax receiver,
                } method
                || !managers.Contains(receiver.Identifier.ValueText))
                continue;

            var methodName = method.Name.Identifier.ValueText;
            var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray() ?? [];
            ExpressionSyntax? key = null;
            if (methodName.Equals("ApplyResources", StringComparison.OrdinalIgnoreCase) && arguments is [_, { Expression: var second }])
                key = second;
            else if ((methodName.Equals("GetObject", StringComparison.OrdinalIgnoreCase)
                    || methodName.Equals("GetString", StringComparison.OrdinalIgnoreCase))
                && arguments is [{ Expression: var first }])
                key = first;

            if (key is not LiteralExpressionSyntax { Token.Value: string text } literal) continue;
            if (text.Equals(oldName, StringComparison.Ordinal))
                changes.Add(new TextChange(literal.Span, "\"" + newName + "\""));
            else if (text.StartsWith(oldName + ".", StringComparison.Ordinal))
                changes.Add(new TextChange(literal.Span, "\"" + newName + text[oldName.Length..] + "\""));
        }
        return _text.WithChanges(changes).ToString();
    }

    public int FieldPosition(string name)
    {
        return _class.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declarators).SelectMany(declarator => declarator.Names)
            .Single(identifier => identifier.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Identifier.SpanStart;
    }

    public void ValidateFieldRename(string oldName, string newName)
    {
        if (!SyntaxFacts.IsValidIdentifier(newName))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerRenameMustBeIdentifier), nameof(newName));
        if (DeclaredNames.Any(name => name.Equals(newName, StringComparison.OrdinalIgnoreCase)
            && !name.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerRenameAlreadyUsed), nameof(newName));
        _ = FieldPosition(oldName);
    }
}
