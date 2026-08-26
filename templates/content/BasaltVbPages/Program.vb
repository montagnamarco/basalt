Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.Hosting
Imports Basalt.Web
Imports Basalt.Razor.Vb.Hosting

''' <summary>
''' A site where a file is a page.
''' </summary>
''' <remarks>
''' No controllers and no models: a .vbpage in the Pages folder answers on its
''' own path, and Pages/Shop/Cart.vbpage is /shop/cart. The shape Classic ASP and
''' PHP have, with the compiler kept.
''' </remarks>
Public Module Program

    Public Sub Main(args As String())
        Dim app = WebApplication.CreateBuilder(args).Build()

        app.UseStaticFiles()

        If app.Environment.IsDevelopment() Then
            ' Straight from disk: save a page, reload the browser. Only this,
            ' not both — a page compiled into the assembly would answer first
            ' and the edited file would never be read.
            app.MapVbPagesFromDisk(IO.Path.Combine(app.Environment.ContentRootPath, "Pages"))
        Else
            ' Compiled into the assembly: no compiler ships to production, and
            ' the errors arrived at build time.
            app.MapVbPages()
        End If

        app.Run()
    End Sub

End Module
