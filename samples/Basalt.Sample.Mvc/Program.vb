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

        ' No ResourcesPath: Visual Basic names an embedded .resx after the root
        ' namespace and the file name only, whatever folder it sits in, so
        ' Resources/Views.Home.Localized.resx is RootNamespace.Views.Home.Localized
        ' - which is where the localizer looks when no path is set.
        builder.Services.AddLocalization()
        builder.Services.AddControllersWithViews().AddViewLocalization()
        builder.Services.AddRazorPages()
        ' The single call that makes .vbhtml views work.
        builder.Services.AddVbViews()
        builder.Services.AddSingleton(Of IGreeter, Greeter)()
        ' Blazor beside MVC: /clicker is an interactive .vbrazor component.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents()

        Dim app = builder.Build()

        ' Areas first, as ASP.NET Core documents: the default route would
        ' otherwise read /Office as a controller named Office.
        app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}")
        app.MapDefaultControllerRoute()
        app.MapRazorPages()

        ' Blazor needs antiforgery for its endpoints, and the render mode
        ' its interactive components declare mapped.
        app.UseAntiforgery()
        app.MapRazorComponents(Of Global.Basalt.Sample.Mvc.Components.App)().AddInteractiveServerRenderMode()

        app.Run()
    End Sub

End Module
