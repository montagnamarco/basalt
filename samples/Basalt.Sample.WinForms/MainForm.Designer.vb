<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MainForm
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()
        Me.titleLabel = New System.Windows.Forms.Label()
        Me.counterLabel = New System.Windows.Forms.Label()
        Me.countButton = New System.Windows.Forms.Button()
        Me.dialogButton = New System.Windows.Forms.Button()
        Me.drawingPanel = New System.Windows.Forms.Panel()
        Me.statusPanel = New System.Windows.Forms.Panel()
        Me.statusLabel = New System.Windows.Forms.Label()
        Me.clockTimer = New System.Windows.Forms.Timer(Me.components)
        Me.statusPanel.SuspendLayout()
        Me.SuspendLayout()
        '
        'titleLabel
        '
        Me.titleLabel.AutoSize = True
        Me.titleLabel.Font = New System.Drawing.Font("Microsoft Sans Serif", 12.0!, System.Drawing.FontStyle.Bold)
        Me.titleLabel.Location = New System.Drawing.Point(12, 9)
        Me.titleLabel.Name = "titleLabel"
        Me.titleLabel.Text = "Windows Forms on Basalt"
        '
        'counterLabel
        '
        Me.counterLabel.AutoSize = True
        Me.counterLabel.Location = New System.Drawing.Point(12, 45)
        Me.counterLabel.Name = "counterLabel"
        Me.counterLabel.Text = "Clicks: 0"
        '
        'countButton
        '
        Me.countButton.Location = New System.Drawing.Point(12, 70)
        Me.countButton.Name = "countButton"
        Me.countButton.Size = New System.Drawing.Size(90, 23)
        Me.countButton.TabIndex = 0
        Me.countButton.Text = "&Count"
        '
        'dialogButton
        '
        Me.dialogButton.Location = New System.Drawing.Point(108, 70)
        Me.dialogButton.Name = "dialogButton"
        Me.dialogButton.Size = New System.Drawing.Size(110, 23)
        Me.dialogButton.TabIndex = 1
        Me.dialogButton.Text = "&Open dialog..."
        '
        'drawingPanel
        '
        Me.drawingPanel.Anchor = CType(System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left Or System.Windows.Forms.AnchorStyles.Right, System.Windows.Forms.AnchorStyles)
        Me.drawingPanel.BackColor = System.Drawing.Color.White
        Me.drawingPanel.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D
        Me.drawingPanel.Location = New System.Drawing.Point(12, 105)
        Me.drawingPanel.Name = "drawingPanel"
        Me.drawingPanel.Size = New System.Drawing.Size(400, 180)
        '
        'statusPanel
        '
        Me.statusPanel.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D
        Me.statusPanel.Controls.Add(Me.statusLabel)
        Me.statusPanel.Dock = System.Windows.Forms.DockStyle.Bottom
        Me.statusPanel.Name = "statusPanel"
        Me.statusPanel.Size = New System.Drawing.Size(424, 24)
        '
        'statusLabel
        '
        Me.statusLabel.Dock = System.Windows.Forms.DockStyle.Fill
        Me.statusLabel.Name = "statusLabel"
        Me.statusLabel.Text = "Ready"
        Me.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        'clockTimer
        '
        Me.clockTimer.Enabled = True
        Me.clockTimer.Interval = 1000
        '
        'MainForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(424, 321)
        Me.Controls.Add(Me.drawingPanel)
        Me.Controls.Add(Me.dialogButton)
        Me.Controls.Add(Me.countButton)
        Me.Controls.Add(Me.counterLabel)
        Me.Controls.Add(Me.titleLabel)
        Me.Controls.Add(Me.statusPanel)
        Me.Name = "MainForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "Basalt.Forms demo"
        Me.statusPanel.ResumeLayout(False)
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents titleLabel As System.Windows.Forms.Label
    Friend WithEvents counterLabel As System.Windows.Forms.Label
    Friend WithEvents countButton As System.Windows.Forms.Button
    Friend WithEvents dialogButton As System.Windows.Forms.Button
    Friend WithEvents drawingPanel As System.Windows.Forms.Panel
    Friend WithEvents statusPanel As System.Windows.Forms.Panel
    Friend WithEvents statusLabel As System.Windows.Forms.Label
    Friend WithEvents clockTimer As System.Windows.Forms.Timer

End Class
