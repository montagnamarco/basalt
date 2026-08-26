VERSION 5.00
Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
Begin VB.Form Form1 
   Caption         =   "Anagrafica"
   ClientHeight    =   3195
   ClientWidth     =   4680
   StartUpPosition =   3  'Windows Default
   Begin VB.Frame fraDati 
      Caption         =   "Dati"
      Height          =   1500
      Left            =   120
      Top             =   120
      Width           =   4400
      Begin VB.TextBox txtNome 
         Height          =   285
         Left            =   1320
         Top             =   480
         Width           =   2295
      End
      Begin VB.Label lblNome 
         Caption         =   "Nome:"
         Height          =   255
         Left            =   240
         Top             =   525
         Width           =   975
      End
   End
   Begin VB.CommandButton cmdOk 
      Caption         =   "OK"
      Height          =   375
      Left            =   1320
      Top             =   1800
      Width           =   1215
   End
   Begin MSComctlLib.ListView lvwElenco 
      Height          =   900
      Left            =   120
      Top             =   2280
      Width           =   4400
   End
End
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_PredeclaredId = True
Option Explicit

Private Sub cmdOk_Click()
    If Trim$(txtNome.Text) = "" Then
        MsgBox "Inserire il nome", vbExclamation
        Exit Sub
    End If
    MsgBox "Ciao " & txtNome.Text
End Sub

Private Sub Form_Load()
    txtNome.Text = ""
End Sub
