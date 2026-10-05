using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    private object? EvaluatePreviewProperty(ExpressionSyntax expression, Type expectedType)
    {
        if (expression is MeExpressionSyntax && expectedType.IsInstanceOfType(Form))
        {
            return Form;
        }

        if (expression is MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } member)
        {
            var name = member.Name.Identifier.ValueText;
            object? instance = FindComponent(name) ?? (object?)FindControl(name);
            if (instance is not null && expectedType.IsInstanceOfType(instance)) return instance;
        }
        return WinFormsDesignerValues.Evaluate(expression, expectedType);
    }
}
