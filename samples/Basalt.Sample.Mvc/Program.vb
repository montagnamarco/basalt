Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports Basalt.Razor.Vb.AspNetCore
Imports Basalt.Sample.Mvc.Services

''' <summary>
''' An ASP.NET Core MVC site written entirely in Visual Basic.
''' </summary>
Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        builder.Services.AddControllersWithViews()
        builder.Services.AddRazorPages()
        ' The single call that makes .vbhtml views work.
        builder.Services.AddVbViews()
        builder.Services.AddSingleton(Of IGreeter, Greeter)()

        Dim app = builder.Build()

        app.MapDefaultControllerRoute()
        app.MapRazorPages()

        app.Run()
    End Sub

End Module
