Imports Microsoft.AspNetCore.Mvc

Namespace Areas.Office

    ''' <summary>
    ''' A controller in an MVC area, named like the one at the root.
    ''' </summary>
    ''' <remarks>
    ''' Both have a Home/Index view. They used to generate the same class, so a
    ''' site with an area and a root view of the same name did not build.
    ''' </remarks>
    <Area("Office")>
    Public Class HomeController
        Inherits Controller

        Public Function Index() As IActionResult
            Return View()
        End Function

    End Class

End Namespace
