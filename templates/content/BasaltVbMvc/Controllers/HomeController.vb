Imports Microsoft.AspNetCore.Mvc
Imports BasaltVbMvc.Models

Namespace Controllers

    Public Class HomeController
        Inherits Controller

        Public Function Index() As IActionResult
            Return View()
        End Function

        Public Function Privacy() As IActionResult
            Return View()
        End Function

        <ResponseCache(Duration:=0, Location:=ResponseCacheLocation.None, NoStore:=True)>
        Public Function [Error]() As IActionResult
            Return View(New ErrorViewModel With {
                .RequestId = System.Diagnostics.Activity.Current?.Id})
        End Function

    End Class

End Namespace
