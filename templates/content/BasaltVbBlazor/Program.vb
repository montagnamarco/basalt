Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting

''' <summary>
''' A Blazor Web App written entirely in Visual Basic.
''' </summary>
Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        ' Server interactivity: a component that asks for it with
        ' @rendermode InteractiveServer runs over a SignalR circuit.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents()

        Dim app = builder.Build()

        If Not app.Environment.IsDevelopment() Then
            app.UseExceptionHandler("/Error", createScopeForErrors:=True)
            app.UseHsts()
        End If

        app.UseHttpsRedirection()

        ' Blazor refuses to route without this: an endpoint carries antiforgery
        ' metadata and the middleware has to be there to honour it.
        app.UseAntiforgery()

        app.MapStaticAssets()

        ' App is the root: the page shell and the router, which finds every
        ' .vbrazor with an @Page directive in this assembly.
        app.MapRazorComponents(Of Global.BasaltVbBlazor.Components.App)() _
            .AddInteractiveServerRenderMode()

        app.Run()
    End Sub

End Module
