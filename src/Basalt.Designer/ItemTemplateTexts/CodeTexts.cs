namespace Basalt.Designer;

/// <summary>The Visual Basic code items: one type per file, no Namespace (see <see cref="ItemTemplates"/>).</summary>
internal static class CodeTexts
{
    public static string Class(string name) => $"""
        Public Class {name}
        End Class

        """;

    public static string Module(string name) => $"""
        Module {name}
        End Module

        """;

    public static string Interface(string name) => $"""
        Public Interface {name}
        End Interface

        """;

    public static string Structure(string name) => $"""
        Public Structure {name}
        End Structure

        """;

    public static string Enum(string name) => $"""
        Public Enum {name}
            None = 0
        End Enum

        """;

    /// <summary>The three constructors every exception offers, as the framework guidelines ask.</summary>
    public static string Exception(string name) => $"""
        Public Class {name}
            Inherits Global.System.Exception

            Public Sub New()
                MyBase.New()
            End Sub

            Public Sub New(message As String)
                MyBase.New(message)
            End Sub

            Public Sub New(message As String, innerException As Global.System.Exception)
                MyBase.New(message, innerException)
            End Sub
        End Class

        """;

    /// <summary>
    /// A component that can be dropped into a container, as Visual Studio's
    /// does: the second constructor adds it to the container it is given.
    /// </summary>
    public static string Component(string name) => $"""
        Public Class {name}
            Inherits Global.System.ComponentModel.Component

            Public Sub New()
                MyBase.New()
            End Sub

            Public Sub New(container As Global.System.ComponentModel.IContainer)
                MyClass.New()

                If container IsNot Nothing Then
                    container.Add(Me)
                End If
            End Sub
        End Class

        """;
}
