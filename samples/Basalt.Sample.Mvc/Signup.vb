Imports System.ComponentModel.DataAnnotations

''' <summary>What the sign-up form edits, validated by its annotations.</summary>
Public Class Signup

    <Required(ErrorMessage:="Tell us your name.")>
    <StringLength(40, ErrorMessage:="Forty characters at most.")>
    Public Property Name As String = ""

    <Range(1, 120, ErrorMessage:="An age from 1 to 120.")>
    Public Property Age As Integer = 30

End Class
