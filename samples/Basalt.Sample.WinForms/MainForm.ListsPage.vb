' The "Lists" page: list boxes, a tree and a list view, side by side in split panes.
Partial Class MainForm

    Private Function BuildListsPage() As TabPage
        Dim page As New TabPage("Lists")

        Dim outer As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterDistance = 180}
        Dim inner As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Horizontal, .SplitterDistance = 150}
        outer.Panel2.Controls.Add(inner)
        page.Controls.Add(outer)

        ' Left: a tree of folders.
        Dim tree As New TreeView With {.Dock = DockStyle.Fill, .CheckBoxes = False}
        Dim root = tree.Nodes.Add("Computer")
        Dim documents = root.Nodes.Add("Documents")
        documents.Nodes.Add("Letters")
        documents.Nodes.Add("Invoices")
        Dim pictures = root.Nodes.Add("Pictures")
        pictures.Nodes.Add("2025")
        pictures.Nodes.Add("2026")
        root.Nodes.Add("Music")
        root.Expand()
        AddHandler tree.AfterSelect, Sub(s, e) statusLabel.Text = $"Tree: {e.Node.FullPath}"
        outer.Panel1.Controls.Add(tree)

        ' Top right: a list box and a checked list box.
        Dim fruits As New ListBox With {.Dock = DockStyle.Left, .Width = 160, .SelectionMode = SelectionMode.MultiExtended}
        fruits.Items.AddRange(New Object() {"Apple", "Banana", "Cherry", "Grape", "Lemon", "Mango", "Orange", "Pear", "Plum"})
        AddHandler fruits.SelectedIndexChanged, Sub(s, e) statusLabel.Text = $"{fruits.SelectedItems.Count} fruit(s) selected"

        Dim tasks As New CheckedListBox With {.Dock = DockStyle.Fill, .CheckOnClick = True}
        tasks.Items.Add("Write the report", True)
        tasks.Items.Add("Call the bank")
        tasks.Items.Add("Book the train")
        tasks.Items.Add("Water the plants", True)
        AddHandler tasks.ItemCheck, Sub(s, e) statusLabel.Text = $"Task {e.Index + 1} becomes {e.NewValue}"

        inner.Panel1.Controls.Add(tasks)
        inner.Panel1.Controls.Add(fruits)

        ' Bottom right: a details list view with sortable columns.
        Dim files As New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True, .GridLines = True}
        files.Columns.Add("Name", 140)
        files.Columns.Add("Size", 70, HorizontalAlignment.Right)
        files.Columns.Add("Modified", 110)
        AddFile(files, "report.docx", "24 KB", "2026-09-30")
        AddFile(files, "budget.xlsx", "12 KB", "2026-09-28")
        AddFile(files, "photo.jpg", "2.1 MB", "2026-08-15")
        AddFile(files, "notes.txt", "1 KB", "2026-10-01")
        AddHandler files.ColumnClick, Sub(s, e)
                                          ' A second click on the same column reverses the order.
                                          Dim previous = TryCast(files.ListViewItemSorter, ColumnSorter)
                                          Dim descending = previous IsNot Nothing AndAlso previous.Column = e.Column AndAlso Not previous.Descending
                                          files.ListViewItemSorter = New ColumnSorter(e.Column, descending)
                                          statusLabel.Text = $"Sorted by {files.Columns(e.Column).Text}{If(descending, " (descending)", "")}"
                                      End Sub
        inner.Panel2.Controls.Add(files)

        Return page
    End Function

    Private Shared Sub AddFile(files As ListView, name As String, size As String, modified As String)
        Dim item As New ListViewItem(name)
        item.SubItems.Add(size)
        item.SubItems.Add(modified)
        files.Items.Add(item)
    End Sub

    ''' <summary>Orders list view items by the text of one column.</summary>
    Private NotInheritable Class ColumnSorter
        Implements IComparer

        Public ReadOnly Property Column As Integer
        Public ReadOnly Property Descending As Boolean

        Public Sub New(column As Integer, descending As Boolean)
            Me.Column = column
            Me.Descending = descending
        End Sub

        Public Function Compare(x As Object, y As Object) As Integer Implements IComparer.Compare
            Dim left = DirectCast(x, ListViewItem).SubItems(Column).Text
            Dim right = DirectCast(y, ListViewItem).SubItems(Column).Text
            Dim order = String.Compare(left, right, StringComparison.CurrentCulture)
            Return If(Descending, -order, order)
        End Function
    End Class

End Class
