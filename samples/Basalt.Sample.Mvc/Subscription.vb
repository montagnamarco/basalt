Imports System.ComponentModel.DataAnnotations

''' <summary>What the statically rendered subscription form posts.</summary>
Public Class Subscription

    <Required(ErrorMessage:="An address, please.")>
    <EmailAddress(ErrorMessage:="That is not an e-mail address.")>
    Public Property Email As String = ""

End Class
