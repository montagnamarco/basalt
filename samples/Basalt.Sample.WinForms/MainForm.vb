Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing

Public Class MainForm

    Private _clicks As Integer
    Private ReadOnly _points As New List(Of Point)
    Private _penColor As Color = Color.Black

    ' Fonts made for a style are kept and reused, and disposed with the form, instead of
    ' creating (and leaking) a new Font on every toggle.
    Private ReadOnly _styledFonts As New Dictionary(Of String, Font)

    Public Sub New()
        InitializeComponent()

        BuildMenu()
        BuildToolBar()
        BuildDrawingMenu()

        pages.TabPages.Add(BuildInputPage())
        pages.TabPages.Add(BuildListsPage())
        pages.TabPages.Add(BuildGridPage())
        pages.TabPages.Add(BuildTextPage())
        pages.TabPages.Add(BuildCalendarPage())
        pages.TabPages.Add(BuildAdvancedPage())

        tips.SetToolTip(countButton, "Counts your clicks")
        tips.SetToolTip(drawingPanel, "Drag with the left button; right-click for the menu")
    End Sub

    ' --- Menu bar and toolbar -----------------------------------------------------

    Private Sub BuildMenu()
        Dim fileMenu As New ToolStripMenuItem("&File")
        fileMenu.DropDownItems.Add(New ToolStripMenuItem("&Open...", Nothing, AddressOf OpenFile) With {.ShortcutKeys = Keys.Control Or Keys.O})
        fileMenu.DropDownItems.Add(New ToolStripMenuItem("&Save drawing...", Nothing, AddressOf SaveDrawing) With {.ShortcutKeys = Keys.Control Or Keys.S})
        fileMenu.DropDownItems.Add(New ToolStripSeparator())
        fileMenu.DropDownItems.Add(New ToolStripMenuItem("Print pre&view...", Nothing, AddressOf ShowPrintPreview))
        fileMenu.DropDownItems.Add(New ToolStripSeparator())
        fileMenu.DropDownItems.Add(New ToolStripMenuItem("E&xit", Nothing, Sub(s, e) Close()))

        ' The pages are added after the menu is built, so the View menu lists them each time it opens.
        Dim viewMenu As New ToolStripMenuItem("&View")
        AddHandler viewMenu.DropDownOpening, Sub(s, e) FillViewMenu(viewMenu)
        viewMenu.DropDownItems.Add("(pages)")

        Dim formatMenu As New ToolStripMenuItem("F&ormat")
        formatMenu.DropDownItems.Add(New ToolStripMenuItem("&Font...", Nothing, AddressOf ChooseFont))
        formatMenu.DropDownItems.Add(New ToolStripMenuItem("Pen &colour...", Nothing, AddressOf ChoosePenColor))

        Dim helpMenu As New ToolStripMenuItem("&Help")
        helpMenu.DropDownItems.Add(New ToolStripMenuItem("&About...", Nothing, AddressOf ShowAbout) With {.ShortcutKeys = Keys.F1})

        mainMenu.Items.AddRange(New ToolStripItem() {fileMenu, viewMenu, formatMenu, helpMenu})
    End Sub

    Private Sub FillViewMenu(viewMenu As ToolStripMenuItem)
        viewMenu.DropDownItems.Clear()
        For index = 0 To pages.TabPages.Count - 1
            Dim pageIndex = index
            Dim item As New ToolStripMenuItem(pages.TabPages(index).Text, Nothing, Sub(s, e) pages.SelectedIndex = pageIndex)
            item.Checked = pages.SelectedIndex = index
            viewMenu.DropDownItems.Add(item)
        Next
    End Sub

    Private Sub BuildToolBar()
        mainToolBar.Items.Add(New ToolStripButton("Open", Nothing, AddressOf OpenFile) With {.ToolTipText = "Open a text file into the Text page"})
        mainToolBar.Items.Add(New ToolStripButton("Save", Nothing, AddressOf SaveDrawing) With {.ToolTipText = "Save the drawing as PNG"})
        mainToolBar.Items.Add(New ToolStripSeparator())

        Dim boldButton As New ToolStripButton("Bold") With {.CheckOnClick = True, .ToolTipText = "Bold title"}
        AddHandler boldButton.CheckedChanged, Sub(s, e)
                                                  Dim style = If(boldButton.Checked, FontStyle.Bold, FontStyle.Regular)
                                                  titleLabel.Font = StyledFont(titleLabel.Font, style)
                                              End Sub
        mainToolBar.Items.Add(boldButton)

        Dim colours As New ToolStripDropDownButton("Pen")
        For Each colour In {Color.Black, Color.Red, Color.Green, Color.Blue}
            Dim chosen = colour
            colours.DropDownItems.Add(New ToolStripMenuItem(colour.Name, Nothing, Sub(s, e) _penColor = chosen))
        Next
        mainToolBar.Items.Add(colours)

        mainToolBar.Items.Add(New ToolStripSeparator())
        mainToolBar.Items.Add(New ToolStripLabel("Basalt.Forms"))
    End Sub

    Private Sub BuildDrawingMenu()
        Dim menu As New ContextMenuStrip(components)
        menu.Items.Add(New ToolStripMenuItem("&Clear", Nothing, Sub(s, e)
                                                                    _points.Clear()
                                                                    drawingPanel.Invalidate()
                                                                End Sub))
        menu.Items.Add(New ToolStripMenuItem("Pen &colour...", Nothing, AddressOf ChoosePenColor))
        drawingPanel.ContextMenuStrip = menu
    End Sub

    ''' <summary>The given font in the given style, created once and reused.</summary>
    Private Function StyledFont(baseFont As Font, style As FontStyle) As Font
        Dim key = $"{baseFont.FontFamily.Name}|{baseFont.Size}|{style}"
        Dim styled As Font = Nothing
        If Not _styledFonts.TryGetValue(key, styled) Then
            styled = New Font(baseFont, style)
            _styledFonts.Add(key, styled)
        End If

        Return styled
    End Function

    Private Sub MainForm_FormClosed(sender As Object, e As FormClosedEventArgs) Handles Me.FormClosed
        For Each styled In _styledFonts.Values
            styled.Dispose()
        Next
        _styledFonts.Clear()
    End Sub

    ' --- Commands ------------------------------------------------------------------

    Private Sub OpenFile(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog With {.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

            LoadTextFile(dialog.FileName)
            pages.SelectedTab = textPage
            statusLabel.Text = $"Opened {IO.Path.GetFileName(dialog.FileName)}"
        End Using
    End Sub

    Private Sub SaveDrawing(sender As Object, e As EventArgs)
        Using dialog As New SaveFileDialog With {.Filter = "PNG image (*.png)|*.png", .FileName = "drawing.png"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

            Using bitmap As New Bitmap(drawingPanel.Width, drawingPanel.Height)
                drawingPanel.DrawToBitmap(bitmap, New Rectangle(0, 0, bitmap.Width, bitmap.Height))
                bitmap.Save(dialog.FileName, Imaging.ImageFormat.Png)
            End Using

            statusLabel.Text = $"Saved {IO.Path.GetFileName(dialog.FileName)}"
        End Using
    End Sub

    Private Sub ChooseFont(sender As Object, e As EventArgs)
        Using dialog As New FontDialog With {.Font = titleLabel.Font}
            If dialog.ShowDialog(Me) = DialogResult.OK Then titleLabel.Font = dialog.Font
        End Using
    End Sub

    Private Sub ChoosePenColor(sender As Object, e As EventArgs)
        Using dialog As New ColorDialog With {.Color = _penColor, .FullOpen = True}
            If dialog.ShowDialog(Me) = DialogResult.OK Then _penColor = dialog.Color
        End Using
    End Sub

    Private Sub ShowPrintPreview(sender As Object, e As EventArgs)
        Using document As New PrintDocument()
            AddHandler document.PrintPage, AddressOf PrintDrawing

            Using preview As New PrintPreviewDialog With {.Document = document}
                preview.ShowDialog(Me)
            End Using
        End Using
    End Sub

    Private Sub PrintDrawing(sender As Object, e As PrintPageEventArgs)
        Dim g = e.Graphics
        Using heading As New Font("Microsoft Sans Serif", 18, FontStyle.Bold)
            g.DrawString("Basalt.Forms demo", heading, Brushes.Black, e.MarginBounds.Left, e.MarginBounds.Top)
        End Using
        PaintDrawing(g, New Point(e.MarginBounds.Left, e.MarginBounds.Top + 50))
        e.HasMorePages = False
    End Sub

    Private Sub ShowAbout(sender As Object, e As EventArgs)
        MessageBox.Show(Me, "Windows Forms in VB on macOS and Linux, drawn by Basalt.Forms.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ' --- Basics page -------------------------------------------------------------------

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

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If _clicks > 0 AndAlso MessageBox.Show("Quit the demo?", "Basalt.Forms", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.No Then
            e.Cancel = True
        End If
    End Sub

    Private Sub drawingPanel_MouseDown(sender As Object, e As MouseEventArgs) Handles drawingPanel.MouseDown
        If e.Button <> MouseButtons.Left Then Return

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
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        PaintDrawing(e.Graphics, Point.Empty)
        e.Graphics.DrawString("Draw here with the mouse; right-click for options", Me.Font, Brushes.Gray, 10, 100)
    End Sub

    Private Sub PaintDrawing(g As Graphics, origin As Point)
        Dim state = g.Save()
        g.TranslateTransform(origin.X, origin.Y)

        Using brush As New LinearGradientBrush(New Rectangle(10, 10, 160, 80), Color.SteelBlue, Color.LightSkyBlue, LinearGradientMode.ForwardDiagonal)
            g.FillEllipse(brush, 10, 10, 160, 80)
        End Using

        Using pen As New Pen(Color.DarkRed, 3)
            g.DrawRectangle(pen, 190, 20, 120, 60)
        End Using

        If _points.Count > 1 Then
            Using pen As New Pen(_penColor, 2)
                g.DrawLines(pen, _points.ToArray())
            End Using
        End If

        g.Restore(state)
    End Sub

    Private Sub clockTimer_Tick(sender As Object, e As EventArgs) Handles clockTimer.Tick
        clockLabel.Text = DateTime.Now.ToString("HH:mm:ss") & If(Control.IsKeyLocked(Keys.CapsLock), "  CAPS", "")
    End Sub

End Class
