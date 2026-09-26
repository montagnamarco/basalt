Imports System.ComponentModel.DataAnnotations
Imports Microsoft.AspNetCore.Mvc.Rendering

Namespace Models

    ''' <summary>A form model exercising the tag helpers that read metadata.</summary>
    Public Class Order

        <Required>
        <Display(Name:="Customer name")>
        Public Property Customer As String

        <Range(1, 10)>
        Public Property Quantity As Integer = 2

        Public Property Colour As String = "green"

        Public ReadOnly Property Colours As IEnumerable(Of SelectListItem) = {
            New SelectListItem("Red", "red"),
            New SelectListItem("Green", "green")
        }

    End Class

End Namespace
