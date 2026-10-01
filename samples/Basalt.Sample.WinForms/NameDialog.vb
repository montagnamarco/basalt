Public Class NameDialog
    Inherits Form

    Private ReadOnly _okButton As New Button()
    Private ReadOnly _cancelButton As New Button()
    Private ReadOnly _promptLabel As New Label()
    Private ReadOnly _nameBox As New TextBox()

    Public Sub New()
        Text = "Your name"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(280, 100)

        _promptLabel.AutoSize = True
        _promptLabel.Location = New Point(12, 12)
        _promptLabel.Text = "&Name:"

        _nameBox.Location = New Point(12, 32)
        _nameBox.Width = 256
        _nameBox.Text = "World"

        _okButton.Text = "OK"
        _okButton.DialogResult = DialogResult.OK
        _okButton.Location = New Point(112, 66)

        _cancelButton.Text = "Cancel"
        _cancelButton.DialogResult = DialogResult.Cancel
        _cancelButton.Location = New Point(193, 66)

        Controls.AddRange({_promptLabel, _nameBox, _okButton, _cancelButton})
        AcceptButton = _okButton
        CancelButton = _cancelButton
    End Sub

    Public ReadOnly Property PersonName As String
        Get
            Return _nameBox.Text
        End Get
    End Property

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _nameBox.SelectAll()
        _nameBox.Focus()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If DialogResult = DialogResult.OK AndAlso _nameBox.Text.Trim().Length = 0 Then
            MessageBox.Show(Me, "Please type a name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            e.Cancel = True
        End If

        MyBase.OnFormClosing(e)
    End Sub

End Class
