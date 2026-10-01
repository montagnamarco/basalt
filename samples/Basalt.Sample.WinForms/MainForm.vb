Imports System.Drawing.Drawing2D

Public Class MainForm

    Private _clicks As Integer
    Private ReadOnly _points As New List(Of Point)

    Private Sub countButton_Click(sender As Object, e As EventArgs) Handles countButton.Click
        _clicks += 1
        counterLabel.Text = $"Clicks: {_clicks}"
    End Sub

    Private Sub dialogButton_Click(sender As Object, e As EventArgs) Handles dialogButton.Click
        Using dialog As New NameDialog()
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                statusLabel.Text = $"Hello, {dialog.PersonName}!"
            Else
                statusLabel.Text = "Dialog cancelled"
            End If
        End Using
    End Sub

    Private Sub drawingPanel_MouseDown(sender As Object, e As MouseEventArgs) Handles drawingPanel.MouseDown
        _points.Clear()
        _points.Add(e.Location)
    End Sub

    Private Sub drawingPanel_MouseMove(sender As Object, e As MouseEventArgs) Handles drawingPanel.MouseMove
        If e.Button = MouseButtons.Left Then
            _points.Add(e.Location)
            drawingPanel.Invalidate()
        End If

        statusLabel.Text = $"X={e.X} Y={e.Y}"
    End Sub

    Private Sub drawingPanel_Paint(sender As Object, e As PaintEventArgs) Handles drawingPanel.Paint
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Using brush As New LinearGradientBrush(New Rectangle(10, 10, 160, 80), Color.SteelBlue, Color.LightSkyBlue, LinearGradientMode.ForwardDiagonal)
            g.FillEllipse(brush, 10, 10, 160, 80)
        End Using

        Using pen As New Pen(Color.DarkRed, 3)
            g.DrawRectangle(pen, 190, 20, 120, 60)
        End Using

        g.DrawString("Draw here with the mouse", Me.Font, Brushes.Gray, 10, 100)

        If _points.Count > 1 Then
            Using pen As New Pen(Color.Black, 2)
                g.DrawLines(pen, _points.ToArray())
            End Using
        End If
    End Sub

    Private Sub clockTimer_Tick(sender As Object, e As EventArgs) Handles clockTimer.Tick
        Me.Text = $"Basalt.Forms demo - {DateTime.Now:HH:mm:ss}"
    End Sub

End Class
