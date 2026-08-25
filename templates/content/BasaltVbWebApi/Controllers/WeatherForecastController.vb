Imports Microsoft.AspNetCore.Mvc

Namespace Controllers

    <ApiController>
    <Route("[controller]")>
    Public Class WeatherForecastController
        Inherits ControllerBase

        Private Shared ReadOnly Summaries As String() = {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild",
            "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        }

        <HttpGet>
        Public Function Get_() As IEnumerable(Of WeatherForecast)
            Return Enumerable.Range(1, 5).Select(
                Function(index) New WeatherForecast With {
                    .Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    .TemperatureC = Random.Shared.Next(-20, 55),
                    .Summary = Summaries(Random.Shared.Next(Summaries.Length))
                }).ToArray()
        End Function

    End Class

End Namespace
