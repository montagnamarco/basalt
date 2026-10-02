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
        Me.mainMenu = New System.Windows.Forms.MenuStrip()
        Me.mainToolBar = New System.Windows.Forms.ToolStrip()
        Me.mainStatusBar = New System.Windows.Forms.StatusStrip()
        Me.statusLabel = New System.Windows.Forms.ToolStripStatusLabel()
        Me.clockLabel = New System.Windows.Forms.ToolStripStatusLabel()
        Me.pages = New System.Windows.Forms.TabControl()
        Me.basicsPage = New System.Windows.Forms.TabPage()
        Me.titleLabel = New System.Windows.Forms.Label()
        Me.counterLabel = New System.Windows.Forms.Label()
        Me.countButton = New System.Windows.Forms.Button()
        Me.dialogButton = New System.Windows.Forms.Button()
        Me.drawingPanel = New System.Windows.Forms.Panel()
        Me.clockTimer = New System.Windows.Forms.Timer(Me.components)
        Me.tips = New System.Windows.Forms.ToolTip(Me.components)
        Me.pages.SuspendLayout()
        Me.basicsPage.SuspendLayout()
        Me.mainStatusBar.SuspendLayout()
        Me.SuspendLayout()
        '
        'mainMenu
        '
        Me.mainMenu.Name = "mainMenu"
        '
        'mainToolBar
        '
        Me.mainToolBar.Name = "mainToolBar"
        '
        'mainStatusBar
        '
        Me.mainStatusBar.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.statusLabel, Me.clockLabel})
        Me.mainStatusBar.Name = "mainStatusBar"
        '
        'statusLabel
        '
        Me.statusLabel.Name = "statusLabel"
        Me.statusLabel.Spring = True
        Me.statusLabel.Text = "Ready"
        Me.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        'clockLabel
        '
        Me.clockLabel.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left
        Me.clockLabel.Name = "clockLabel"
        Me.clockLabel.Text = "--:--:--"
        '
        'pages
        '
        Me.pages.Controls.Add(Me.basicsPage)
        Me.pages.Dock = System.Windows.Forms.DockStyle.Fill
        Me.pages.Name = "pages"
        '
        'basicsPage
        '
        Me.basicsPage.Controls.Add(Me.drawingPanel)
        Me.basicsPage.Controls.Add(Me.dialogButton)
        Me.basicsPage.Controls.Add(Me.countButton)
        Me.basicsPage.Controls.Add(Me.counterLabel)
        Me.basicsPage.Controls.Add(Me.titleLabel)
        Me.basicsPage.Name = "basicsPage"
        Me.basicsPage.Padding = New System.Windows.Forms.Padding(3)
        Me.basicsPage.Text = "Basics"
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
        Me.drawingPanel.Size = New System.Drawing.Size(560, 260)
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
        Me.ClientSize = New System.Drawing.Size(600, 460)
        Me.Controls.Add(Me.pages)
        Me.Controls.Add(Me.mainStatusBar)
        Me.Controls.Add(Me.mainToolBar)
        Me.Controls.Add(Me.mainMenu)
        Me.MainMenuStrip = Me.mainMenu
        Me.Name = "MainForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "Basalt.Forms demo"
        Me.mainStatusBar.ResumeLayout(False)
        Me.basicsPage.ResumeLayout(False)
        Me.basicsPage.PerformLayout()
        Me.pages.ResumeLayout(False)
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents mainMenu As System.Windows.Forms.MenuStrip
    Friend WithEvents mainToolBar As System.Windows.Forms.ToolStrip
    Friend WithEvents mainStatusBar As System.Windows.Forms.StatusStrip
    Friend WithEvents statusLabel As System.Windows.Forms.ToolStripStatusLabel
    Friend WithEvents clockLabel As System.Windows.Forms.ToolStripStatusLabel
    Friend WithEvents pages As System.Windows.Forms.TabControl
    Friend WithEvents basicsPage As System.Windows.Forms.TabPage
    Friend WithEvents titleLabel As System.Windows.Forms.Label
    Friend WithEvents counterLabel As System.Windows.Forms.Label
    Friend WithEvents countButton As System.Windows.Forms.Button
    Friend WithEvents dialogButton As System.Windows.Forms.Button
    Friend WithEvents drawingPanel As System.Windows.Forms.Panel
    Friend WithEvents clockTimer As System.Windows.Forms.Timer
    Friend WithEvents tips As System.Windows.Forms.ToolTip

End Class
