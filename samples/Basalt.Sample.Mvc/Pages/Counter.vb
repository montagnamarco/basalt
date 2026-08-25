Imports Microsoft.AspNetCore.Mvc.RazorPages

Namespace Pages

    ''' <summary>A hand-written page model, as a Razor Pages user writes one.</summary>
    Public Class CounterModel
        Inherits PageModel

        Public Property Total As Integer

        Public Sub OnGet()
            Total = 41 + 1
        End Sub

    End Class

End Namespace
