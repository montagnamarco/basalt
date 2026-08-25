Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Basalt.Razor.Vb.AspNetCore

Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        builder.Services.AddRazorPages()
        builder.Services.AddVbViews()

        Dim app = builder.Build()

        If Not app.Environment.IsDevelopment() Then
            app.UseExceptionHandler("/Error")
            app.UseHsts()
        End If

        app.UseHttpsRedirection()
        app.UseStaticFiles()
        app.UseRouting()
        app.UseAuthorization()

        app.MapRazorPages()

        app.Run()
    End Sub

End Module
