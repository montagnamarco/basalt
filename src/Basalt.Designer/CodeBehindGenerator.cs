using System.Text;
using Basalt.Core.Model;
using Basalt.Designer.Model;

namespace Basalt.Designer;

/// <summary>
/// Generates the code-behind skeleton for a designed window.
///
/// Unlike the old WinForms Designer.vb, here the code-behind does not build
/// the controls: the structure lives in the .axaml file and Avalonia loads it
/// with InitializeComponent. The code-behind contains only the partial class
/// and the event handlers, so it can be regenerated without any risk of
/// losing the user's work.
/// </summary>
public static class CodeBehindGenerator
{
    public static string FileExtension(SourceLanguage language) => language switch
    {
        SourceLanguage.VisualBasic => ".vb",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language.")
    };

    /// <summary>Conventional code-behind path: "MainWindow.axaml" → "MainWindow.axaml.vb".</summary>
    public static string CodeBehindPath(string xamlPath, SourceLanguage language) =>
        xamlPath + FileExtension(language);

    public static string Generate(XamlDocument document, SourceLanguage language)
    {
        var fullName = document.ClassName
            ?? throw new InvalidOperationException(
                "The document declares no x:Class, so no code-behind can be generated.");

        var lastDot = fullName.LastIndexOf('.');
        var namespaceName = lastDot < 0 ? null : fullName[..lastDot];
        var typeName = lastDot < 0 ? fullName : fullName[(lastDot + 1)..];
        var isWindow = document.Root.Name.LocalName == "Window";

        return language switch
        {
            SourceLanguage.VisualBasic => GenerateVisualBasic(namespaceName, typeName, isWindow),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language.")
        };
    }

    private static string GenerateVisualBasic(string? namespaceName, string typeName, bool isWindow)
    {
        var baseType = isWindow ? "Window" : "UserControl";
        var body = new StringBuilder();

        body.AppendLine("Imports Avalonia.Controls");
        body.AppendLine("Imports Avalonia.Markup.Xaml");
        body.AppendLine();

        var indent = namespaceName is null ? "" : "    ";
        if (namespaceName is not null)
        {
            body.AppendLine($"Namespace {namespaceName}");
            body.AppendLine();
        }

        body.AppendLine($"{indent}Partial Public Class {typeName}");
        body.AppendLine($"{indent}    Inherits {baseType}");
        body.AppendLine();
        body.AppendLine($"{indent}    Public Sub New()");
        body.AppendLine($"{indent}        InitializeComponent()");
        body.AppendLine($"{indent}    End Sub");
        body.AppendLine();
        body.AppendLine($"{indent}    Private Sub InitializeComponent()");
        body.AppendLine($"{indent}        AvaloniaXamlLoader.Load(Me)");
        body.AppendLine($"{indent}    End Sub");
        body.AppendLine();
        body.AppendLine($"{indent}End Class");

        if (namespaceName is not null)
        {
            body.AppendLine();
            body.AppendLine("End Namespace");
        }

        return body.ToString();
    }

    /// <summary>
    /// Event handler to insert into the existing code-behind when the user
    /// wires up an event from the property inspector.
    /// </summary>
    public static string GenerateEventHandler(
        string handlerName, string senderType, string eventArgsType, SourceLanguage language) =>
        language switch
        {
            SourceLanguage.VisualBasic =>
                $"""
                    Private Sub {handlerName}(sender As Object, e As {eventArgsType})
                        ' TODO: implementare
                    End Sub
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language.")
        };
}
