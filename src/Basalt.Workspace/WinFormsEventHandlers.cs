using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>Edits VB Handles clauses and preserves the bodies of event handlers.</summary>
public static class WinFormsEventHandlers
{
    public static string RemoveControls(string source, string className, IReadOnlySet<string> controlNames)
    {
        var type = FindClass(source, className);
        var changes = new List<TextChange>();
        foreach (var method in type.Members.OfType<MethodBlockSyntax>())
        {
            if (method.SubOrFunctionStatement.HandlesClause is not { } clause) continue;
            var remaining = clause.Events.Where(item => !controlNames.Contains(item.EventContainer.ToString())).ToArray();
            if (remaining.Length != clause.Events.Count)
                changes.Add(new(clause.Span, remaining.Length == 0 ? "" : clause.WithEvents(SyntaxFactory.SeparatedList(remaining)).ToString()));
        }
        return SourceText.From(source).WithChanges(changes).ToString();
    }
    public static int HandlerOffset(string source, string className, string handlerName)
    {
        var method = FindClass(source, className).Members.OfType<MethodBlockSyntax>()
            .First(method => method.SubOrFunctionStatement.Identifier.ValueText.Equals(handlerName, StringComparison.OrdinalIgnoreCase));
        if (method.Statements.FirstOrDefault() is { } statement)
            return statement.SpanStart;

        var text = SourceText.From(source);
        var declarationLine = text.Lines.GetLineFromPosition(method.SubOrFunctionStatement.Span.End);
        var bodyLine = text.Lines.GetLineFromPosition(declarationLine.EndIncludingLineBreak);
        var position = bodyLine.Start;
        while (position < bodyLine.End && char.IsWhiteSpace(source[position]))
            position++;
        return position;
    }

    public static IReadOnlyDictionary<string, string> Read(string source, string className, string controlName)
    {
        var type = FindClass(source, className);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var method in type.Members.OfType<MethodBlockSyntax>())
        {
            var statement = method.SubOrFunctionStatement;
            if (statement.HandlesClause is not { } clause) continue;
            foreach (var item in clause.Events)
                if (item.EventContainer.ToString().Equals(controlName, StringComparison.OrdinalIgnoreCase))
                    result[item.EventMember.Identifier.ValueText] = statement.Identifier.ValueText;
        }
        return result;
    }

    public static string Set(string source, string className, string controlName, string eventName,
        string? handlerName, Type delegateType)
    {
        if (!SyntaxFacts.IsValidIdentifier(eventName) || (controlName != "Me" && !SyntaxFacts.IsValidIdentifier(controlName))
            || (handlerName is not null && !SyntaxFacts.IsValidIdentifier(handlerName)))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerIdentifierRequired));
        var type = FindClass(source, className);
        var changes = new List<TextChange>();
        foreach (var method in type.Members.OfType<MethodBlockSyntax>())
        {
            if (method.SubOrFunctionStatement.HandlesClause is not { } clause) continue;
            var remaining = clause.Events.Where(item => !(item.EventContainer.ToString().Equals(controlName, StringComparison.OrdinalIgnoreCase)
                && item.EventMember.Identifier.ValueText.Equals(eventName, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (remaining.Length != clause.Events.Count)
                changes.Add(new(clause.Span, remaining.Length == 0 ? "" : clause.WithEvents(SyntaxFactory.SeparatedList(remaining)).ToString()));
        }
        var updated = SourceText.From(source).WithChanges(changes).ToString();
        if (handlerName is null) return updated;
        type = FindClass(updated, className);
        var existing = type.Members.OfType<MethodBlockSyntax>().FirstOrDefault(method =>
            method.SubOrFunctionStatement.Identifier.ValueText.Equals(handlerName, StringComparison.OrdinalIgnoreCase));
        var binding = controlName + "." + eventName;
        var parameters = delegateType.GetMethod("Invoke")?.GetParameters()
            ?? throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerEventDelegateTypeRequired), nameof(delegateType));
        if (existing is not null)
        {
            var statement = existing.SubOrFunctionStatement;
            if (statement.ParameterList?.Parameters.Count != parameters.Length)
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerHandlerParameterCountMismatch));
            var position = statement.HandlesClause?.Span.End ?? statement.Span.End;
            var insertion = statement.HandlesClause is null ? " Handles " + binding : ", " + binding;
            return SourceText.From(updated).WithChanges(new TextChange(new TextSpan(position, 0), insertion)).ToString();
        }
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var arguments = string.Join(", ", parameters.Select((parameter, index) =>
            $"{(parameter.ParameterType.IsByRef ? "ByRef " : "")}{(SyntaxFacts.IsValidIdentifier(parameter.Name ?? "") ? parameter.Name : "arg" + index)} As {TypeName(parameter.ParameterType)}"));
        var methodSource = $"{newline}    Private Sub {handlerName}({arguments}) Handles {binding}{newline}        {newline}    End Sub{newline}";
        return SourceText.From(updated).WithChanges(new TextChange(new TextSpan(type.EndClassStatement.FullSpan.Start, 0), methodSource)).ToString();
    }

    private static ClassBlockSyntax FindClass(string source, string className)
    {
        var syntax = VisualBasicSyntaxTree.ParseText(source).GetRoot();
        if (syntax.ContainsDiagnostics) throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerEventHandlerSyntaxErrors));
        return syntax.DescendantNodes().OfType<ClassBlockSyntax>().SingleOrDefault(type =>
            type.ClassStatement.Identifier.ValueText.Equals(className, StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerPartialClassNotFound));
    }

    private static string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()!);
        if (type.IsArray) return TypeName(type.GetElementType()!) + "(" + new string(',', type.GetArrayRank() - 1) + ")";
        if (type.IsGenericType) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerGenericEventArgumentsUnsupported));
        return "Global." + type.FullName!.Replace('+', '.');
    }
}
