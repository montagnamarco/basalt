Imports Microsoft.AspNetCore.Razor.TagHelpers

''' <summary>
''' A tag helper written in Visual Basic, in the site itself: &lt;shout text="hi"&gt;
''' becomes &lt;strong&gt;HI&lt;/strong&gt;. Proves the engine finds the user's own.
''' </summary>
<HtmlTargetElement("shout")>
Public Class ShoutTagHelper
    Inherits TagHelper

    Public Property Text As String

    ''' <summary>Written level-id="2": Razor's naming, not level-i-d.</summary>
    Public Property LevelID As Integer = 1

    Public Overrides Sub Process(context As TagHelperContext, output As TagHelperOutput)
        output.TagName = "strong"
        output.Content.SetContent(If(Text, "").ToUpperInvariant() & New String("!"c, LevelID))
    End Sub

End Class
