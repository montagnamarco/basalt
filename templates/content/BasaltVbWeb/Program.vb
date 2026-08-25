Imports Microsoft.AspNetCore.Builder

Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)
        Dim app = builder.Build()

        app.MapGet("/", Function() "Hello from Visual Basic")

        app.Run()
    End Sub

End Module
