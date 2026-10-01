Public Class NameDialog
    Inherits Form

    Private ReadOnly _okButton As New Button()
    Private ReadOnly _cancelButton As New Button()
    Private ReadOnly _promptLabel As New Label()
    Private _name As String = "World"

    Public Sub New()
        Text = "Name"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(260, 90)

        _promptLabel.AutoSize = True
        _promptLabel.Location = New Point(12, 12)
        _promptLabel.Text = "Press OK to greet the world, Esc to cancel."

        _okButton.Text = "OK"
        _okButton.DialogResult = DialogResult.OK
        _okButton.Location = New Point(92, 55)

        _cancelButton.Text = "Cancel"
        _cancelButton.DialogResult = DialogResult.Cancel
        _cancelButton.Location = New Point(173, 55)

        Controls.AddRange({_promptLabel, _okButton, _cancelButton})
        AcceptButton = _okButton
        CancelButton = _cancelButton
    End Sub

    Public ReadOnly Property PersonName As String
        Get
            Return _name
        End Get
    End Property

End Class
