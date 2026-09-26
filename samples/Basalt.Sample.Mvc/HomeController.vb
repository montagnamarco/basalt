Imports Microsoft.AspNetCore.Mvc
Imports Basalt.Sample.Mvc.Models

Public Class HomeController
    Inherits Controller

    Public Function Index() As IActionResult
        TempData("note") = "from tempdata"
        Return View(New Customer With {.Name = "Ada", .Orders = 3})
    End Function

    Public Function Helpers(Optional id As Integer = 0) As IActionResult
        Return View(New Order With {.Customer = "Ada"})
    End Function

End Class
