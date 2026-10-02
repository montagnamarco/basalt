' The "Input" page: the controls a user types into, picks from or slides.
Partial Class MainForm

    Private _errors As ErrorProvider

    Private Function BuildInputPage() As TabPage
        Dim page As New TabPage("Input")

        ' Owned by the form's components, so it is disposed with the form.
        _errors = New ErrorProvider(components)

        ' Left column: text entry, with validation shown by an ErrorProvider.
        Dim nameBox = AddLabelled(page, "Name (required):", New TextBox(), 12)
        AddHandler nameBox.Validating, Sub(s, e)
                                           _errors.SetError(nameBox, If(nameBox.Text.Trim().Length = 0, "Please enter a name.", ""))
                                       End Sub

        Dim phoneBox = AddLabelled(page, "Phone:", New MaskedTextBox("(000) 000-0000"), 52)
        tips.SetToolTip(phoneBox, "Digits only: the mask places the separators")

        Dim countryBox = AddLabelled(page, "Country:", New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}, 92)
        countryBox.Items.AddRange(New Object() {"Italy", "France", "Germany", "Spain", "United Kingdom", "United States"})
        countryBox.SelectedIndex = 0

        Dim ageBox = AddLabelled(page, "Age:", New NumericUpDown With {.Minimum = 0, .Maximum = 120, .Value = 30}, 132)

        Dim sizeBox = AddLabelled(page, "T-shirt size:", New DomainUpDown(), 172)
        sizeBox.Items.AddRange(New Object() {"XL", "L", "M", "S", "XS"})
        sizeBox.SelectedIndex = 2

        Dim birthday = AddLabelled(page, "Birthday:", New DateTimePicker With {.Format = DateTimePickerFormat.Short}, 212)

        ' Right column: choices.
        Dim contact As New GroupBox With {.Text = "Contact me by", .Location = New Point(330, 12), .Size = New Size(230, 100)}
        Dim byMail As New RadioButton With {.Text = "E-mail", .Location = New Point(12, 22), .AutoSize = True, .Checked = True}
        Dim byPhone As New RadioButton With {.Text = "Phone", .Location = New Point(12, 46), .AutoSize = True}
        Dim never As New RadioButton With {.Text = "Never", .Location = New Point(12, 70), .AutoSize = True}
        contact.Controls.AddRange(New Control() {byMail, byPhone, never})
        page.Controls.Add(contact)

        Dim newsletter As New CheckBox With {.Text = "Send me the newsletter", .Location = New Point(330, 122), .AutoSize = True}
        Dim terms As New CheckBox With {.Text = "I accept the terms", .Location = New Point(330, 146), .AutoSize = True, .ThreeState = True}
        page.Controls.AddRange(New Control() {newsletter, terms})

        ' Bottom: a slider driving a progress bar.
        Dim volumeLabel As New Label With {.Text = "Volume: 40", .Location = New Point(12, 262), .AutoSize = True}
        Dim volume As New TrackBar With {.Location = New Point(100, 255), .Size = New Size(220, 45), .Maximum = 100, .Value = 40, .TickFrequency = 10}
        Dim level As New ProgressBar With {.Location = New Point(330, 262), .Size = New Size(230, 20), .Value = 40}
        AddHandler volume.ValueChanged, Sub(s, e)
                                            level.Value = volume.Value
                                            volumeLabel.Text = $"Volume: {volume.Value}"
                                        End Sub
        page.Controls.AddRange(New Control() {volumeLabel, volume, level})

        Dim link As New LinkLabel With {.Text = "Read more about Basalt", .Location = New Point(12, 310), .AutoSize = True}
        AddHandler link.LinkClicked, Sub(s, e)
                                         link.LinkVisited = True
                                         statusLabel.Text = "The link was clicked"
                                     End Sub
        page.Controls.Add(link)

        Dim summary As New Button With {.Text = "&Summary", .Location = New Point(330, 305), .Size = New Size(100, 25)}
        AddHandler summary.Click, Sub(s, e)
                                      Dim contactBy = If(byMail.Checked, "e-mail", If(byPhone.Checked, "phone", "never"))
                                      MessageBox.Show(Me,
                                          $"{nameBox.Text}, {ageBox.Value} years, {countryBox.Text}" & Environment.NewLine &
                                          $"Phone {phoneBox.Text}, size {sizeBox.Text}, born {birthday.Value:d}" & Environment.NewLine &
                                          $"Contact by {contactBy}; newsletter {newsletter.Checked}; terms {terms.CheckState}",
                                          "Summary")
                                  End Sub
        page.Controls.Add(summary)

        Return page
    End Function

    ''' <summary>Places a caption and its control on one row, and returns the control.</summary>
    Private Shared Function AddLabelled(Of T As Control)(page As TabPage, caption As String, control As T, top As Integer) As T
        page.Controls.Add(New Label With {.Text = caption, .Location = New Point(12, top + 3), .AutoSize = True})
        control.Location = New Point(110, top)
        control.Width = 200
        page.Controls.Add(control)

        Return control
    End Function

End Class
