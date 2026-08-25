Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.Hosting
Imports Basalt.Web
Imports Basalt.Razor.Vb.Hosting

''' <summary>
''' A site of pages, in the Classic ASP style: no controllers, no models.
''' </summary>
Public Module Program

    Public Sub Main(args As String())
        Dim app = WebApplication.CreateBuilder(args).Build()

        If app.Environment.IsDevelopment() Then
            ' From disk, so saving a file is enough to see the change. Only
            ' this, not both: a page compiled into the assembly answers
            ' first and the edited file is never read, which looks exactly
            ' like hot reload not working.
            app.MapVbPagesFromDisk(IO.Path.Combine(app.Environment.ContentRootPath, "Pages"))
        Else
            ' Compiled into the assembly: no compiler ships to production,
            ' and the errors arrived before deployment.
            app.MapVbPages()
        End If

        app.Run()
    End Sub

End Module
