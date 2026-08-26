# A Visual Basic 6 project

Written the way Visual Basic 6 wrote them — CRLF line endings, a `.vbp`
listing the files, a `.frm` holding both a form's controls and its code —
so the converter is measured against the real format rather than against
something convenient.

## What is in it

`Anagrafica.vbp` names one form, one module and one OCX.

`Form1.frm` carries the two halves a `.frm` always has: nested `Begin`/`End`
declarations describing the controls, then the form's own Visual Basic after
the `VB_` attributes.

It is deliberately awkward in four ways, each of which broke something:

- a **`Frame`** with a text box and a label inside it, because losing the
  nesting puts a group's contents loose on the form;
- a **`ListView` from `MSCOMCTL.OCX`**, which does not exist outside Windows
  and stands in for every third-party control nobody will ever have;
- **`StartUpPosition = 3  'Windows Default`**, because Visual Basic writes a
  comment after some values and keeping it puts the comment into the property;
- **`Trim$`** and **`MsgBox "text"`**, the two spellings VB.NET rejects.

## What it is for

`FormFile` reads it, `FormToAxaml` turns the form into Avalonia markup, and
`ProjectConversion` writes a `.vbproj` beside the `.vbp`. The tests in
`Vb6FormTests` use a copy of this shape rather than the files, so a test
failure names what broke instead of pointing at a path.

Nothing here is built by the solution: these are inputs, not a project.
