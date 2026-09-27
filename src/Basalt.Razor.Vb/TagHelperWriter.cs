using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Basalt.Razor.Vb;

/// <summary>
/// Writes an element bound to tag helpers as the C# Razor compiler writes it.
/// </summary>
/// <remarks>
/// The same runtime calls, in the same order, so every tag helper — the
/// framework's and the user's own — runs exactly as it does in a .cshtml:
/// Begin a scope with the element's content as a callback, create each tag
/// helper through the page, set its properties from the attributes, run
/// them, write the output, end the scope. Read from the code the .NET 10 SDK
/// generates for a view using form, label, input and asp-validation-for.
/// </remarks>
internal static class TagHelperWriter
{
    private const string Runtime = "Global.Microsoft.AspNetCore.Razor.Runtime.TagHelpers";
    private const string TagHelpers = "Global.Microsoft.AspNetCore.Razor.TagHelpers";
    private const string Values = "Global.Basalt.Razor.Vb.AspNetCore.TagHelperValues";

    /// <summary>The fields and properties the calls need, once per class.</summary>
    public static void WriteMembers(StringBuilder builder, VbHtmlDocument document)
    {
        builder.AppendLine($"        Private __tagHelperExecutionContext As {Runtime}.TagHelperExecutionContext");
        builder.AppendLine($"        Private ReadOnly __tagHelperRunner As New {Runtime}.TagHelperRunner()");
        builder.AppendLine($"        Private __backedTagHelperScopeManager As {Runtime}.TagHelperScopeManager");
        builder.AppendLine();

        // Created on first use and bound to the page's own writing scopes,
        // which is where a tag helper's content is captured.
        builder.AppendLine($"        Private ReadOnly Property __tagHelperScopeManager As {Runtime}.TagHelperScopeManager");
        builder.AppendLine("            Get");
        builder.AppendLine("                If __backedTagHelperScopeManager Is Nothing Then");
        builder.AppendLine($"                    __backedTagHelperScopeManager = New {Runtime}.TagHelperScopeManager(");
        builder.AppendLine("                        AddressOf StartTagHelperWritingScope, AddressOf EndTagHelperWritingScope)");
        builder.AppendLine("                End If");
        builder.AppendLine();
        builder.AppendLine("                Return __backedTagHelperScopeManager");
        builder.AppendLine("            End Get");
        builder.AppendLine("        End Property");
        builder.AppendLine();

        var used = TagHelperBinder.ElementsIn(document.Nodes)
            .SelectMany(e => e.Descriptors)
            .GroupBy(d => d.TypeName)
            .Select(g => g.First());

        foreach (var descriptor in used)
            builder.AppendLine($"        Private {descriptor.FieldName} As {descriptor.TypeName}");

        builder.AppendLine();

        // What asp-for is turned into a ModelExpression with, filled in by
        // the container like Html and Url.
        builder.AppendLine("        <Global.Microsoft.AspNetCore.Mvc.Razor.Internal.RazorInject>");
        builder.AppendLine(
            "        Public Property ModelExpressionProvider As Global.Microsoft.AspNetCore.Mvc.ViewFeatures.IModelExpressionProvider");
        builder.AppendLine();
    }

    /// <summary>Writes one element and, through <paramref name="writeChildren"/>, its content.</summary>
    /// <param name="idPrefix">
    /// What makes the element's id unique in the application, not only in
    /// its file: the class it is written into.
    /// </param>
    public static void WriteElement(
        StringBuilder builder,
        TagHelperElementNode element,
        string pad,
        string idPrefix,
        List<SourceMapping> mappings,
        string? filePath,
        Action writeChildren)
    {
        // The runtime keys cached output on it — CacheTagHelper builds its
        // cache key from it — so an id unique only within one file let two
        // views each starting with <cache> serve each other's HTML. C# puts
        // the file's checksum in it; the class name does the same job.
        var id = $"{idPrefix}-{element.Position}";

        builder.AppendLine(
            $"{pad}__tagHelperExecutionContext = __tagHelperScopeManager.Begin(" +
            $"{Quote(element.Name)}, {TagHelpers}.TagMode.{element.Mode}, {Quote(id)}, " +
            "Async Function() As Global.System.Threading.Tasks.Task");

        writeChildren();

        builder.AppendLine($"{pad}    Await Global.System.Threading.Tasks.Task.CompletedTask");
        builder.AppendLine($"{pad}End Function)");

        foreach (var descriptor in element.Descriptors)
        {
            builder.AppendLine($"{pad}{descriptor.FieldName} = CreateTagHelper(Of {descriptor.TypeName})()");
            builder.AppendLine($"{pad}__tagHelperExecutionContext.Add({descriptor.FieldName})");
        }

        foreach (var attribute in element.Attributes)
            WriteAttribute(builder, element, attribute, pad, mappings, filePath);

        builder.AppendLine($"{pad}Await __tagHelperRunner.RunAsync(__tagHelperExecutionContext)");
        builder.AppendLine($"{pad}If Not __tagHelperExecutionContext.Output.IsContentModified Then");
        builder.AppendLine($"{pad}    Await __tagHelperExecutionContext.SetOutputContentAsync()");
        builder.AppendLine($"{pad}End If");
        builder.AppendLine($"{pad}Write(__tagHelperExecutionContext.Output)");
        builder.AppendLine($"{pad}__tagHelperExecutionContext = __tagHelperScopeManager.End()");
    }

    /// <summary>
    /// An attribute: the properties it sets on each tag helper that binds it,
    /// or an HTML attribute passed through for the output.
    /// </summary>
    private static void WriteAttribute(
        StringBuilder builder, TagHelperElementNode element, TagHelperAttributeSyntax attribute, string pad,
        List<SourceMapping> mappings, string? filePath)
    {
        var style = $"{TagHelpers}.HtmlAttributeValueStyle.{attribute.Style}";

        var bound = element.Descriptors
            .Select(descriptor => (Descriptor: descriptor, Property: descriptor.PropertyFor(attribute.Name)))
            .Where(pair => pair.Property is not null)
            .ToList();

        if (bound.Count == 0)
        {
            WriteHtmlAttribute(builder, attribute, pad, style, mappings, filePath);
            return;
        }

        string? first = null;

        foreach (var (descriptor, property) in bound)
        {
            var target = property!.Kind is TagHelperPropertyKind.StringDictionary or TagHelperPropertyKind.CodeDictionary
                ? $"{descriptor.FieldName}.{property.PropertyName}({Quote(attribute.Name.Substring(property.DictionaryPrefix!.Length))})"
                : $"{descriptor.FieldName}.{property.PropertyName}";

            var (value, code, authoredAt, trimmed) = PropertyValue(property, attribute);

            WriteLine(builder, attribute, $"{pad}{target} = ", value, "", code, authoredAt, mappings, filePath, trimmed);

            first ??= target;
        }

        builder.AppendLine(
            $"{pad}__tagHelperExecutionContext.AddTagHelperAttribute({Quote(attribute.Name)}, {first}, {style})");
    }

    /// <summary>An attribute no tag helper binds, passed through for the output.</summary>
    private static void WriteHtmlAttribute(
        StringBuilder builder, TagHelperAttributeSyntax attribute, string pad, string style,
        List<SourceMapping> mappings, string? filePath)
    {
        if (attribute.Style == TagHelperQuoteStyle.Minimized)
        {
            builder.AppendLine(
                $"{pad}__tagHelperExecutionContext.AddHtmlAttribute(New {TagHelpers}.TagHelperAttribute({Quote(attribute.Name)}))");
            return;
        }

        if (attribute.IsLiteral)
        {
            builder.AppendLine(
                $"{pad}__tagHelperExecutionContext.AddHtmlAttribute({Quote(attribute.Name)}, " +
                $"New Global.Microsoft.AspNetCore.Html.HtmlString({Quote(attribute.Text)}), {style})");
            return;
        }

        // A value that is one expression is conditional, as in a .cshtml:
        // Nothing and False leave the attribute out, True writes its name.
        if (SingleExpression(attribute) is { IsRaw: false } only)
        {
            WriteLine(builder, attribute,
                $"{pad}{Values}.AddConditional(__tagHelperExecutionContext, {Quote(attribute.Name)}, ",
                Part(only), $", {style})", code: true, authoredAt: PartOffset(only), mappings, filePath);
            return;
        }

        var html = $"{Values}.Html(";

        WriteLine(builder, attribute,
            $"{pad}__tagHelperExecutionContext.AddHtmlAttribute({Quote(attribute.Name)}, ",
            $"{html}{string.Join(", ", attribute.Value.Select(HtmlPart))})", $", {style})",
            code: true, authoredAt: html.Length + FirstPartOffset(attribute, HtmlPart), mappings, filePath);
    }

    /// <summary>
    /// How far into the text written for an attribute's first piece the
    /// template's own text starts: past the quote of a literal, the
    /// HtmlString around one, or the parenthesis around an expression.
    /// </summary>
    private static int FirstPartOffset(TagHelperAttributeSyntax attribute, Func<VbHtmlNode, string> written)
    {
        var first = attribute.Value[0];
        var text = written(first);

        return first switch
        {
            HtmlNode html => text.IndexOf('"') + 1,
            ExpressionNode expression => 1 + PartOffset(expression),
            _ => 0,
        };
    }

    /// <summary>Where an expression's text starts in what <see cref="Part"/> writes for it.</summary>
    private static int PartOffset(ExpressionNode expression)
    {
        var offset = 0;

        if (expression.IsRaw)
            offset += "New Global.Microsoft.AspNetCore.Html.HtmlString(Global.System.Convert.ToString(".Length;

        // "(Await x)": the mapping covers the Await, as a view's does.
        // "(Await x)": the expression starts past the keyword, as a view's does.
        if (expression.IsAwaited) offset += "(Await ".Length;

        return offset;
    }

    /// <summary>
    /// Writes a line holding an attribute's value, mapped to where the value
    /// was written when it holds the author's code.
    /// </summary>
    /// <remarks>
    /// asp-items="Model.Colours" is an expression and asp-for a member path:
    /// an error in them belongs on the template, and a caret there must reach
    /// the matching token. The mapping starts at the author's own text inside
    /// the line, past what is written in front of it, so hover on "Customer"
    /// in asp-for="Customer" finds Customer and not the tag helper's field.
    /// Plain text needs no mapping: nothing in it can fail to compile.
    /// </remarks>
    /// <param name="authoredAt">
    /// Where the template's own text starts inside <paramref name="value"/>,
    /// counted from what this class writes around it — never searched for,
    /// since a one-letter name is also inside "ModelExpressionProvider".
    /// </param>
    /// <param name="trimmed">
    /// Whether the value was written without the leading spaces of its first
    /// piece, so the template side must skip them too.
    /// </param>
    private static void WriteLine(
        StringBuilder builder,
        TagHelperAttributeSyntax attribute,
        string before,
        string value,
        string after,
        bool code,
        int authoredAt,
        List<SourceMapping> mappings,
        string? filePath,
        bool trimmed = false)
    {
        var line = before + value + after;

        if (!code || attribute.Value.Count == 0)
        {
            builder.AppendLine(line);
            return;
        }

        // A value written without its leading spaces starts at its first
        // piece that is not only spaces: in " @Model.List" that is the
        // expression, not the space in front of it.
        var first = trimmed
            ? attribute.Value.FirstOrDefault(part => part is not HtmlNode { Text: var text } || text.Trim().Length > 0)
              ?? attribute.Value[0]
            : attribute.Value[0];

        var position = first switch
        {
            ExpressionNode expression => expression.ExpressionPosition,
            HtmlNode html when trimmed => html.Position + html.Text.Length - html.Text.TrimStart().Length,
            _ => first.Position,
        };

        var end = attribute.Value[attribute.Value.Count - 1] switch
        {
            ExpressionNode expression => expression.ExpressionPosition + expression.Expression.Length,
            HtmlNode html => html.Position + html.Text.Length,
            _ => position,
        };

        ExternalSourceWriter.WriteMapped(
            builder, mappings, filePath, position, Math.Max(0, end - position), first.Line,
            () => builder.AppendLine(line), before.Length + authoredAt);
    }

    /// <summary>
    /// The value an attribute gives a property, as Visual Basic, and whether
    /// it holds the author's code rather than only text.
    /// </summary>
    private static (string Value, bool Code, int AuthoredAt, bool Trimmed) PropertyValue(
        TagHelperProperty property, TagHelperAttributeSyntax attribute)
    {
        const string Lambda = "ModelExpressionProvider.CreateModelExpression(ViewData, Function(__model) ";

        switch (property.Kind)
        {
            case TagHelperPropertyKind.ModelExpression:
            {
                // An expression is the lambda's body as written, as in C#:
                // asp-for="@item.Name" is item.Name, not a member of the model.
                if (SingleExpression(attribute) is { } only)
                    return ($"{Lambda}{only.Expression})", true, Lambda.Length, false);

                // Text is a path on the model. C# indexes with brackets,
                // Visual Basic with parentheses.
                var path = attribute.Text.Trim().Replace('[', '(').Replace(']', ')');

                return path.Length == 0
                    ? ($"{Lambda}__model)", true, Lambda.Length, true)
                    : ($"{Lambda}__model.{path})", true, Lambda.Length + "__model.".Length, true);
            }

            case TagHelperPropertyKind.Enum:
                return attribute.IsLiteral
                    ? ($"{property.TypeName}.{attribute.Text.Trim()}", true, property.TypeName.Length + 1, true)
                    : (CodeText(attribute), true, 0, true);

            case TagHelperPropertyKind.Code:
            case TagHelperPropertyKind.CodeDictionary:
                return attribute.Style == TagHelperQuoteStyle.Minimized
                    ? ("True", false, 0, false)
                    : (CodeText(attribute), true, 0, true);

            default:
            {
                if (attribute.IsLiteral) return (Quote(attribute.Text), false, 0, false);

                var text = $"{Values}.Text(";

                return ($"{text}{string.Join(", ", attribute.Value.Select(TextPart))})", true,
                    text.Length + FirstPartOffset(attribute, TextPart), false);
            }
        }
    }

    /// <summary>
    /// A value that is code, as written: text and expressions together, the
    /// "@" only marking where an expression starts.
    /// </summary>
    private static string CodeText(TagHelperAttributeSyntax attribute) =>
        string.Concat(attribute.Value.Select(part => part switch
        {
            HtmlNode html => html.Text,
            ExpressionNode expression => Part(expression),
            _ => "",
        })).Trim();

    /// <summary>The expression a value consists of, when it is exactly one.</summary>
    private static ExpressionNode? SingleExpression(TagHelperAttributeSyntax attribute) =>
        attribute.Value.Count == 1 ? attribute.Value[0] as ExpressionNode : null;

    /// <summary>An expression's value, awaited or raw as it was written.</summary>
    private static string Part(ExpressionNode expression)
    {
        var value = expression.IsAwaited ? $"(Await {expression.Expression})" : expression.Expression;

        return expression.IsRaw
            ? $"New Global.Microsoft.AspNetCore.Html.HtmlString(Global.System.Convert.ToString({value}))"
            : value;
    }

    /// <summary>A piece of an HTML attribute value: text as markup, expressions as values.</summary>
    private static string HtmlPart(VbHtmlNode part) => part switch
    {
        HtmlNode html => $"New Global.Microsoft.AspNetCore.Html.HtmlString({Quote(html.Text)})",
        ExpressionNode expression => $"({Part(expression)})",
        _ => "Nothing",
    };

    /// <summary>A piece of a string value: text as written, expressions as values.</summary>
    private static string TextPart(VbHtmlNode part) => part switch
    {
        HtmlNode html => Quote(html.Text),
        ExpressionNode expression => $"({Part(expression)})",
        _ => "Nothing",
    };

    private static string Quote(string text) => VbHtmlCodeWriter.Quote(text);
}
