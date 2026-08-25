Namespace Services

    ''' <summary>Something for a view to ask the container for.</summary>
    Public Interface IGreeter
        Function Greet() As String
    End Interface

    Public Class Greeter
        Implements IGreeter

        Public Function Greet() As String Implements IGreeter.Greet
            Return "injected greeting"
        End Function

    End Class

End Namespace
