Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting

''' <summary>
''' A Blazor Web App written entirely in Visual Basic.
''' </summary>
Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        Dim razorComponents = builder.Services.AddRazorComponents()
'#if (UseServer)

        ' Server interactivity: a component with @rendermode InteractiveServer
        ' runs over a SignalR circuit.
        razorComponents.AddInteractiveServerComponents()
'#endif
'#if (UseWebAssembly)

        ' WebAssembly interactivity: the components in the .Client project are
        ' compiled for the browser and run there.
        razorComponents.AddInteractiveWebAssemblyComponents()
'#endif

        Dim app = builder.Build()

        If app.Environment.IsDevelopment() Then
'#if (UseWebAssembly)
            app.UseWebAssemblyDebugging()
'#endif
        Else
            app.UseExceptionHandler("/Error", createScopeForErrors:=True)
            app.UseHsts()
        End If

        app.UseHttpsRedirection()

        ' Blazor refuses to route without this: an endpoint carries antiforgery
        ' metadata and the middleware has to be there to honour it.
        app.UseAntiforgery()

        app.MapStaticAssets()

        ' App is the root: the page shell and the router, which finds every
        ' .vbrazor with an @Page directive.
        Dim components = app.MapRazorComponents(Of Global.BasaltVbBlazor.Components.App)()
'#if (UseServer)
        components.AddInteractiveServerRenderMode()
'#endif
'#if (UseWebAssembly)
        components.AddInteractiveWebAssemblyRenderMode()
        components.AddAdditionalAssemblies(GetType(Global.BasaltVbBlazor.Client.Pages.Counter).Assembly)
'#endif

        app.Run()
    End Sub

End Module
