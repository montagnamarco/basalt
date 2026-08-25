Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection

Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        builder.Services.AddControllers()

        Dim app = builder.Build()

        app.UseHttpsRedirection()
        app.MapControllers()

        app.Run()
    End Sub

End Module
