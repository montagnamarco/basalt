Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.Mvc.RazorPages

Namespace Pages

    Public Class ContactModel
        Inherits PageModel

        <BindProperty>
        Public Property Name As String = ""

        Public Property Message As String = ""

        Public Sub OnGet()
            Message = "not posted yet"
        End Sub

        Public Sub OnPost()
            Message = $"posted by {Name}"
        End Sub

    End Class

End Namespace
