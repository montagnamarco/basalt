Imports System.Diagnostics
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.Mvc.RazorPages

Namespace Pages

    ''' <summary>
    ''' The page Program.vb sends unhandled exceptions to outside Development.
    ''' </summary>
    <ResponseCache(Duration:=0, Location:=ResponseCacheLocation.None, NoStore:=True)>
    <IgnoreAntiforgeryToken>
    Public Class ErrorModel
        Inherits PageModel

        Public Property RequestId As String

        Public ReadOnly Property ShowRequestId As Boolean
            Get
                Return Not String.IsNullOrEmpty(RequestId)
            End Get
        End Property

        Public Sub OnGet()
            RequestId = If(Activity.Current?.Id, HttpContext.TraceIdentifier)
        End Sub

    End Class

End Namespace
