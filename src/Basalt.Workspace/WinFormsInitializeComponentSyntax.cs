using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>
/// Locates the point inside InitializeComponent where generated statements belong,
/// shared by the editors that add controls, pasted controls and collection items.
///
/// The call to Me.ResumeLayout marks where the designer's own initialization ends and
/// the rest of the method (if any) resumes; new statements are inserted right before it
/// so they run as part of construction, not after layout has already resumed. The call is
/// recognized by its syntax shape (Me.ResumeLayout invoked on the form itself) rather than
/// by comparing statement text, so trivia or casing differences cannot hide it. Containers
/// such as Me.Panel1.ResumeLayout(False) come earlier and must not be mistaken for it.
/// </summary>
internal static class WinFormsInitializeComponentSyntax
{
    public static StatementSyntax? FindResumeLayout(MethodBlockSyntax initialize) =>
        initialize.Statements.FirstOrDefault(statement =>
            statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } }
                && member.Expression is MeExpressionSyntax
                && member.Name.Identifier.ValueText.Equals("ResumeLayout", StringComparison.OrdinalIgnoreCase));

    /// <summary>The offset where new statements should be inserted: before Me.ResumeLayout, or at the end of the method when there is none.</summary>
    public static int InsertionPoint(MethodBlockSyntax initialize) =>
        FindResumeLayout(initialize)?.FullSpan.Start ?? initialize.EndSubOrFunctionStatement.FullSpan.Start;
}
