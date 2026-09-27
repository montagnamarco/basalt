using System.Security;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>Text inserted at the supplied caret, with a UTF-16 caret offset within that text.</summary>
public sealed record DocumentationCommentInsertion(string Text, int CaretOffset);

/// <summary>Creates documentation for Visual Basic methods, constructors, properties and events using Roslyn syntax.</summary>
public static class VisualBasicDocumentationCommentService
{
    /// <summary>
    /// Examines the current editor snapshot after the third apostrophe was typed.
    /// The caret and returned offset count UTF-16 code units. Returns null unless
    /// the caret ends a standalone, undocumented triple-apostrophe line immediately
    /// before a method, constructor, property or event at type scope. Parsing runs off the caller thread.
    /// The insertion preserves the trigger line's indentation and line ending;
    /// the returned caret sits on the blank summary line.
    /// </summary>
    public static Task<DocumentationCommentInsertion?> GenerateAsync(
        string source, int caretPosition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.Run(() => Generate(source, caretPosition, cancellationToken), cancellationToken);
    }

    private static DocumentationCommentInsertion? Generate(
        string source, int caretPosition, CancellationToken cancellationToken)
    {
        if (caretPosition < 3 || caretPosition > source.Length)
            return null;

        var text = SourceText.From(source);
        var line = text.Lines.GetLineFromPosition(caretPosition);
        var prefix = source[line.Start..caretPosition];
        if (!prefix.EndsWith("'''", StringComparison.Ordinal)
            || prefix[..^3].Any(character => character is not (' ' or '\t'))
            || caretPosition != line.End)
            return null;

        var tree = VisualBasicSyntaxTree.ParseText(text,
            new VisualBasicParseOptions(documentationMode: DocumentationMode.Parse),
            cancellationToken: cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        var declaration = root.DescendantNodes()
            .FirstOrDefault(node => node.SpanStart >= caretPosition
                && node is MethodStatementSyntax or SubNewStatementSyntax
                    or PropertyStatementSyntax or EventStatementSyntax);
        if (declaration is null || declaration.ContainsDiagnostics
            || source[caretPosition..declaration.SpanStart].Any(character => !char.IsWhiteSpace(character)))
            return null;

        var container = declaration.Parent switch
        {
            MethodBlockBaseSyntax block => block.Parent,
            PropertyBlockSyntax block => block.Parent,
            EventBlockSyntax block => block.Parent,
            _ => declaration.Parent
        };
        if (container is not TypeBlockSyntax)
            return null;

        // Documentation trivia may combine several adjacent lines. Only the newly
        // typed trigger is allowed, so an existing summary is never duplicated.
        var documentation = declaration.GetLeadingTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.DocumentationCommentTrivia))
            .ToArray();
        if (documentation.Length != 1
            || !documentation[0].FullSpan.Contains(caretPosition - 1)
            || documentation[0].ToFullString().Trim() != "'''")
            return null;

        var indentation = prefix[..^3];
        var newline = line.EndIncludingLineBreak > line.End
            ? source[line.End..line.EndIncludingLineBreak] : Environment.NewLine;
        var continuation = newline + indentation + "''' ";
        var builder = new StringBuilder(" <summary>");
        builder.Append(continuation);
        var caretOffset = builder.Length;
        builder.Append(continuation).Append("</summary>");

        ParameterListSyntax? parameters;
        if (declaration is MethodStatementSyntax method)
        {
            foreach (var parameter in method.TypeParameterList?.Parameters
                ?? default(SeparatedSyntaxList<TypeParameterSyntax>))
            {
                builder.Append(continuation).Append("<typeparam name=\"")
                    .Append(SecurityElement.Escape(parameter.Identifier.ValueText))
                    .Append("\"></typeparam>");
            }
            parameters = method.ParameterList;
        }
        else
        {
            parameters = declaration switch
            {
                SubNewStatementSyntax constructor => constructor.ParameterList,
                PropertyStatementSyntax property => property.ParameterList,
                EventStatementSyntax eventDeclaration => eventDeclaration.ParameterList,
                _ => null
            };
        }

        foreach (var parameter in parameters?.Parameters
            ?? default(SeparatedSyntaxList<ParameterSyntax>))
        {
            builder.Append(continuation).Append("<param name=\"")
                .Append(SecurityElement.Escape(parameter.Identifier.Identifier.ValueText))
                .Append("\"></param>");
        }

        if (declaration.IsKind(SyntaxKind.FunctionStatement))
            builder.Append(continuation).Append("<returns></returns>");
        else if (declaration is PropertyStatementSyntax)
            builder.Append(continuation).Append("<value></value>");

        return new DocumentationCommentInsertion(builder.ToString(), caretOffset);
    }
}
