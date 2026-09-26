Imports Microsoft.AspNetCore.Mvc

''' <summary>
''' A view component, so the tests can prove one renders from a Visual Basic
''' view and from a Visual Basic Razor Page.
''' </summary>
Public Class StockViewComponent
    Inherits ViewComponent

    Public Function Invoke(count As Integer) As IViewComponentResult
        Return View(count)
    End Function

End Class
