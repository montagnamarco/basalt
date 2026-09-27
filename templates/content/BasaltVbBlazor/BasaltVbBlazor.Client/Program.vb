Imports Microsoft.AspNetCore.Components.WebAssembly.Hosting

''' <summary>
''' The part of the app that runs in the browser, on WebAssembly.
''' </summary>
Public Module Program

    ' A Sub rather than an Async Main, which Visual Basic does not have. In the
    ' browser the runtime stays alive after Main returns, so the host keeps
    ' running once it is started.
    Public Sub Main(args As String())
        Dim builder = WebAssemblyHostBuilder.CreateDefault(args)

        Dim running = builder.Build().RunAsync()
    End Sub

End Module
