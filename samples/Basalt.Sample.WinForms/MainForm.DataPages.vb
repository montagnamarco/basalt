Imports System.ComponentModel

' The "Grid", "Text" and "Calendar" pages.
Partial Class MainForm

    Private textPage As TabPage
    Private editor As RichTextBox

    ''' <summary>One record of the people shown on the Grid page.</summary>
    Public Class Person
        Public Property Name As String = ""
        Public Property City As String = ""
        Public Property Age As Integer
    End Class

    Private Function BuildGridPage() As TabPage
        Dim page As New TabPage("Grid")

        ' Top: a grid filled row by row, with a check box column painted by the grid itself.
        Dim grid As New DataGridView With {.Dock = DockStyle.Top, .Height = 180, .SelectionMode = DataGridViewSelectionMode.FullRowSelect}
        grid.Columns.Add("product", "Product")
        grid.Columns.Add("quantity", "Quantity")
        grid.Columns.Add("price", "Price")
        grid.Rows.Add("Notebook", "3", "2.50")
        grid.Rows.Add("Pencil", "12", "0.40")
        grid.Rows.Add("Eraser", "5", "0.80")
        grid.Rows.Add("Ruler", "2", "1.20")
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.AliceBlue
        AddHandler grid.CellValueChanged, Sub(s, e) statusLabel.Text = $"Cell ({e.ColumnIndex}, {e.RowIndex}) changed"
        page.Controls.Add(grid)

        ' Bottom: a BindingSource over a list of people, with a navigator and bound text boxes.
        Dim people As New BindingList(Of Person) From {
            New Person With {.Name = "Ada Lovelace", .City = "London", .Age = 36},
            New Person With {.Name = "Grace Hopper", .City = "New York", .Age = 85},
            New Person With {.Name = "Katherine Johnson", .City = "Hampton", .Age = 101}
        }
        Dim source As New BindingSource With {.DataSource = people}

        Dim details As New Panel With {.Dock = DockStyle.Fill}
        Dim navigator As New BindingNavigator(True) With {.BindingSource = source, .Dock = DockStyle.Top}
        details.Controls.Add(navigator)

        Dim nameBox = AddBoundBox(details, "Name:", source, NameOf(Person.Name), 40)
        AddBoundBox(details, "City:", source, NameOf(Person.City), 70)
        AddBoundBox(details, "Age:", source, NameOf(Person.Age), 100)
        AddHandler source.PositionChanged, Sub(s, e) statusLabel.Text = $"Person {source.Position + 1} of {source.Count}"

        page.Controls.Add(details)
        details.BringToFront()

        Return page
    End Function

    Private Shared Function AddBoundBox(container As Control, caption As String, source As BindingSource, member As String, top As Integer) As TextBox
        container.Controls.Add(New Label With {.Text = caption, .Location = New Point(12, top + 3), .AutoSize = True})
        Dim box As New TextBox With {.Location = New Point(80, top), .Width = 220}
        box.DataBindings.Add("Text", source, member)
        container.Controls.Add(box)

        Return box
    End Function

    Private Function BuildTextPage() As TabPage
        textPage = New TabPage("Text")

        editor = New RichTextBox With {.Dock = DockStyle.Fill}
        editor.Text = "Rich text on Basalt." & Environment.NewLine & "Select some words and use the buttons above."

        Dim formatting As New ToolStrip With {.Dock = DockStyle.Top}
        formatting.Items.Add(New ToolStripButton("Bold", Nothing, Sub(s, e) ToggleStyle(FontStyle.Bold)))
        formatting.Items.Add(New ToolStripButton("Italic", Nothing, Sub(s, e) ToggleStyle(FontStyle.Italic)))
        formatting.Items.Add(New ToolStripButton("Underline", Nothing, Sub(s, e) ToggleStyle(FontStyle.Underline)))
        formatting.Items.Add(New ToolStripSeparator())
        formatting.Items.Add(New ToolStripButton("Red", Nothing, Sub(s, e) editor.SelectionColor = Color.Red))
        formatting.Items.Add(New ToolStripButton("Black", Nothing, Sub(s, e) editor.SelectionColor = Color.Black))
        formatting.Items.Add(New ToolStripSeparator())
        formatting.Items.Add(New ToolStripButton("Centre", Nothing, Sub(s, e) editor.SelectionAlignment = HorizontalAlignment.Center))
        formatting.Items.Add(New ToolStripButton("Left", Nothing, Sub(s, e) editor.SelectionAlignment = HorizontalAlignment.Left))

        textPage.Controls.Add(editor)
        textPage.Controls.Add(formatting)

        Return textPage
    End Function

    Private Sub ToggleStyle(style As FontStyle)
        Dim current = If(editor.SelectionFont, editor.Font)
        editor.SelectionFont = StyledFont(current, current.Style Xor style)
        editor.Focus()
    End Sub

    Private Sub LoadTextFile(path As String)
        If path.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase) Then
            editor.LoadFile(path, RichTextBoxStreamType.RichText)
        Else
            editor.Text = IO.File.ReadAllText(path)
        End If
    End Sub

    Private Function BuildCalendarPage() As TabPage
        Dim page As New TabPage("Calendar")

        Dim calendar As New MonthCalendar With {.Location = New Point(12, 12), .MaxSelectionCount = 7}
        calendar.AddBoldedDate(DateTime.Today.AddDays(3))
        calendar.AddBoldedDate(DateTime.Today.AddDays(10))
        calendar.UpdateBoldedDates()

        Dim chosen As New Label With {.Location = New Point(12, 200), .AutoSize = True, .Text = "Pick a day or drag a week"}
        AddHandler calendar.DateChanged, Sub(s, e)
                                             chosen.Text = If(e.Start = e.End,
                                                 $"Selected {e.Start:D}",
                                                 $"Selected {e.Start:d} to {e.End:d}")
                                         End Sub

        page.Controls.AddRange(New Control() {calendar, chosen})

        Return page
    End Function

End Class
