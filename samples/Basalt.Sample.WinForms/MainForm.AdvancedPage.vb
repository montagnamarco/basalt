Imports System.Drawing.Drawing2D
Imports System.Threading

' The "Advanced" page: work on a background thread that reports through Invoke,
' BeginInvoke and InvokeAsync; a button shaped by its Region; MouseHover; a virtual
' list view with a hundred thousand rows; and an owner-drawn tree.
Partial Class MainForm

    Private _primesCancel As CancellationTokenSource

    Private Function BuildAdvancedPage() As TabPage
        Dim page As New TabPage("Advanced")

        ' Background work: the worker never touches a control directly.
        Dim progress As New ProgressBar With {.Location = New Point(12, 12), .Size = New Size(260, 22), .Maximum = 100}
        Dim result As New Label With {.Location = New Point(12, 40), .Size = New Size(400, 20), .Text = "Primes below 3,000,000: not counted yet"}
        Dim start As New Button With {.Location = New Point(280, 11), .Size = New Size(80, 24), .Text = "Count"}
        Dim cancel As New Button With {.Location = New Point(366, 11), .Size = New Size(80, 24), .Text = "Cancel", .Enabled = False}

        AddHandler start.Click, Async Sub(s, e)
                                    start.Enabled = False
                                    cancel.Enabled = True
                                    _primesCancel = New CancellationTokenSource()
                                    Dim token = _primesCancel.Token
                                    Try
                                        Dim count = Await Task.Run(Function() CountPrimes(3_000_000, progress, token), token)
                                        ' InvokeAsync: the result goes back to the UI thread as a task.
                                        Await result.InvokeAsync(Sub() result.Text = $"Primes below 3,000,000: {count:N0}")
                                    Catch ex As OperationCanceledException
                                        result.Text = "Counting cancelled"
                                    Finally
                                        start.Enabled = True
                                        cancel.Enabled = False
                                    End Try
                                End Sub
        AddHandler cancel.Click, Sub(s, e) _primesCancel?.Cancel()

        ' A round button: outside its region it is neither drawn nor clicked.
        Dim round As New Button With {.Location = New Point(12, 80), .Size = New Size(120, 60), .Text = "Round", .BackColor = Color.LightSteelBlue}
        Using shape As New GraphicsPath()
            shape.AddEllipse(0, 0, round.Width, round.Height)
            round.Region = New Region(shape)
        End Using
        AddHandler round.Click, Sub(s, e) statusLabel.Text = "Clicked inside the ellipse"

        ' MouseHover comes once each time the pointer rests on the label.
        Dim hover As New Label With {.Location = New Point(150, 100), .Size = New Size(260, 20), .Text = "Rest the pointer here", .BorderStyle = BorderStyle.FixedSingle}
        Dim hovers = 0
        AddHandler hover.MouseHover, Sub(s, e)
                                         hovers += 1
                                         hover.Text = $"MouseHover number {hovers}"
                                     End Sub

        page.Controls.AddRange(New Control() {progress, result, start, cancel, round, hover, BuildVirtualList(), BuildOwnerDrawnTree()})
        Return page
    End Function

    ''' <summary>Runs on a worker thread; the progress bar is reached only through BeginInvoke.</summary>
    Private Shared Function CountPrimes(limit As Integer, progress As ProgressBar, token As CancellationToken) As Integer
        Dim composite(limit) As Boolean
        Dim count = 0
        Dim lastPercent = -1

        For number = 2 To limit
            token.ThrowIfCancellationRequested()
            If composite(number) Then Continue For

            count += 1
            For multiple As Long = CLng(number) * number To limit Step number
                composite(CInt(multiple)) = True
            Next

            Dim percent = CInt(CLng(number) * 100 \ limit)
            If percent <> lastPercent Then
                lastPercent = percent
                progress.BeginInvoke(Sub() progress.Value = percent)
            End If
        Next

        ' Invoke waits until the UI thread has filled the bar.
        progress.Invoke(Sub() progress.Value = 100)
        Return count
    End Function

    ''' <summary>A hundred thousand rows that exist only when the list view asks for them.</summary>
    Private Function BuildVirtualList() As ListView
        Dim list As New ListView With {
            .Location = New Point(12, 160), .Size = New Size(300, 200),
            .View = View.Details, .FullRowSelect = True, .VirtualMode = True, .VirtualListSize = 100_000}
        list.Columns.Add("Row", 90)
        list.Columns.Add("Square", 100, HorizontalAlignment.Right)
        list.Columns.Add("Hex", 80)

        AddHandler list.RetrieveVirtualItem, Sub(s, e)
                                                 Dim row = e.ItemIndex + 1
                                                 Dim item As New ListViewItem($"Row {row:N0}")
                                                 item.SubItems.Add((CLng(row) * row).ToString("N0"))
                                                 item.SubItems.Add(row.ToString("X"))
                                                 e.Item = item
                                             End Sub
        Return list
    End Function

    ''' <summary>A tree whose node text is drawn by the application, in the colour of its tag.</summary>
    Private Function BuildOwnerDrawnTree() As TreeView
        Dim tree As New TreeView With {.Location = New Point(324, 160), .Size = New Size(200, 200), .DrawMode = TreeViewDrawMode.OwnerDrawText}
        Dim colours = tree.Nodes.Add("Colours")
        For Each colour In {Color.Firebrick, Color.ForestGreen, Color.RoyalBlue, Color.DarkOrange}
            colours.Nodes.Add(New TreeNode(colour.Name) With {.Tag = colour})
        Next
        colours.Expand()

        AddHandler tree.DrawNode, Sub(s, e)
                                      Dim colour = If(TypeOf e.Node.Tag Is Color, DirectCast(e.Node.Tag, Color), tree.ForeColor)
                                      Dim selected = (e.State And TreeNodeStates.Selected) <> 0
                                      If selected Then e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds)
                                      TextRenderer.DrawText(e.Graphics, e.Node.Text, tree.Font, e.Bounds, If(selected, SystemColors.HighlightText, colour), TextFormatFlags.VerticalCenter)
                                  End Sub
        Return tree
    End Function

End Class
