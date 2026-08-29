Imports Basalt.Extensibility

''' <summary>
''' A plugin, as small as one can be while still doing something.
''' </summary>
''' <remarks>
''' It exists to be read: everything a plugin can do today is here, in the
''' order it happens. It is also the test that the loading path works end to
''' end, which a hand-written fixture would not be.
''' </remarks>
Public Class SamplePlugin
    Implements IPlugin

    Public Sub Initialize(host As IPluginHost) Implements IPlugin.Initialize
        ' A command, reachable from the palette and bindable to a key.
        host.AddCommand(
            "sample.hello",
            "Sample: say hello",
            Sub() host.Write("Hello from the sample plugin."))

        ' A control, which appears in the toolbox beside the built-in ones.
        host.AddToolboxItem(
            "Sample Banner",
            "Border",
            "Common",
            "<Border Background=""Gold"" Padding=""8"" />")

        host.Write("Sample plugin loaded.")
    End Sub
End Class
