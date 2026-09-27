Namespace Components

    ' The code-behind of Routes.vbrazor: the .Client project's pages route as
    ' well. They are compiled for the browser, and prerendered here first.
    Partial Public Class Routes

        Private ReadOnly Property ClientAssemblies As Global.System.Reflection.Assembly() =
            {GetType(Global.BasaltVbBlazor.Client.Pages.Counter).Assembly}

    End Class

End Namespace
