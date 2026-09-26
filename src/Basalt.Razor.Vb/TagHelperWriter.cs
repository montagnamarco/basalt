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

            var (value, code) = PropertyValue(property, attribute);

            WriteLine(builder, attribute, $"{pad}{target} = ", value, "", code, mappings, filePath);

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
                Part(only), $", {style})", code: true, mappings, filePath);
            return;
        }

        WriteLine(builder, attribute,
            $"{pad}__tagHelperExecutionContext.AddHtmlAttribute({Quote(attribute.Name)}, ",
            $"{Values}.Html({string.Join(", ", attribute.Value.Select(HtmlPart))})", $", {style})",
            code: true, mappings, filePath);
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
    private static void WriteLine(
        StringBuilder builder,
        TagHelperAttributeSyntax attribute,
        string before,
        string value,
        string after,
        bool code,
        List<SourceMapping> mappings,
        string? filePath)
    {
        var line = before + value + after;

        if (!code || attribute.Value.Count == 0)
        {
            builder.AppendLine(line);
            return;
        }

        var first = attribute.Value[0];
        var length = attribute.Value.Sum(part => part switch
        {
            HtmlNode html => html.Text.Length,
            ExpressionNode expression => expression.Expression.Length + 1,
            _ => 0,
        });

        // Where the author's own text begins in the line: the value itself,
        // or inside it where the value wraps it ("__model." + path).
        var authored = first switch
        {
            HtmlNode html => html.Text.Trim(),
            ExpressionNode expression => expression.Expression,
            _ => "",
        };

        var inValue = authored.Length > 0 ? value.IndexOf(authored, StringComparison.Ordinal) : -1;
        var offset = before.Length + Math.Max(0, inValue);

        // An expression's position is its "@"; the text starts after it.
        var position = first is ExpressionNode ? first.Position + 1 : first.Position;

        ExternalSourceWriter.WriteMapped(
            builder, mappings, filePath, position, length, first.Line,
            () => builder.AppendLine(line), offset);
    }

    /// <summary>
    /// The value an attribute gives a property, as Visual Basic, and whether
    /// it holds the author's code rather than only text.
    /// </summary>
    private static (string Value, bool Code) PropertyValue(TagHelperProperty property, TagHelperAttributeSyntax attribute)
    {
        switch (property.Kind)
        {
            case TagHelperPropertyKind.ModelExpression:
            {
                // An expression is the lambda's body as written, as in C#:
                // asp-for="@item.Name" is item.Name, not a member of the model.
                if (SingleExpression(attribute) is { } only)
                    return ($"ModelExpressionProvider.CreateModelExpression(ViewData, Function(__model) {only.Expression})", true);

                // Text is a path on the model. C# indexes with brackets,
                // Visual Basic with parentheses.
                var path = attribute.Text.Trim().Replace('[', '(').Replace(']', ')');
                var body = path.Length == 0 ? "__model" : $"__model.{path}";

                return ($"ModelExpressionProvider.CreateModelExpression(ViewData, Function(__model) {body})", true);
            }

            case TagHelperPropertyKind.Enum:
                return attribute.IsLiteral
                    ? ($"{property.TypeName}.{attribute.Text.Trim()}", true)
                    : (CodeText(attribute), true);

            case TagHelperPropertyKind.Code:
            case TagHelperPropertyKind.CodeDictionary:
                return attribute.Style == TagHelperQuoteStyle.Minimized
                    ? ("True", false)
                    : (CodeText(attribute), true);

            default:
                return attribute.IsLiteral
                    ? (Quote(attribute.Text), false)
                    : ($"{Values}.Text({string.Join(", ", attribute.Value.Select(TextPart))})", true);
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
