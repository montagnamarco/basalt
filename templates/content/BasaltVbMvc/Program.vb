Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Basalt.Razor.Vb.AspNetCore

Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        builder.Services.AddControllersWithViews()
        builder.Services.AddVbViews()

        Dim app = builder.Build()

        If Not app.Environment.IsDevelopment() Then
            app.UseExceptionHandler("/Home/Error")
            app.UseHsts()
        End If

        app.UseHttpsRedirection()
        app.UseStaticFiles()
        app.UseRouting()
        app.UseAuthorization()

        app.MapDefaultControllerRoute()

        app.Run()
    End Sub

End Module
