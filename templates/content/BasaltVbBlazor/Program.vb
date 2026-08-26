Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection

''' <summary>
''' A Blazor application written entirely in Visual Basic.
''' </summary>
Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        builder.Services.AddRazorComponents()

        Dim app = builder.Build()

        ' Blazor refuses to route without this: an endpoint carries antiforgery
        ' metadata and the middleware has to be there to honour it.
        app.UseAntiforgery()

        ' The root component. Every .vbrazor with an @Page directive routes on
        ' its own path from here.
        '
        ' Components rather than Components.Pages: a folder called Pages is
        ' where a Razor Page's namespace starts from, so it is the root of the
        ' name rather than part of it — the same rule views follow.
        app.MapRazorComponents(Of Global.BasaltVbBlazor.Components.Home)()

        app.Run()
    End Sub

End Module
