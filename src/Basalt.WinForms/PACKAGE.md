# Basalt.WinForms

Windows Forms for .NET on **Windows, macOS and Linux**. The package carries
assemblies with the names and the public API of Microsoft's own
(`System.Windows.Forms`, `System.Windows.Forms.Primitives`,
`Microsoft.VisualBasic.Forms`, `System.Windows.Extensions`, `Accessibility`).
Every control is drawn with Avalonia and SkiaSharp, so the same application
looks and behaves the same on each system, and code written for WinForms
builds and runs without changes.

## Use it

Add the package to a `net10.0` Visual Basic or C# project, with no
`-windows` suffix on the target framework:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Basalt.WinForms" Version="0.1.0" />
  </ItemGroup>
</Project>
```

```vb
Imports System.Windows.Forms

Public Class MainForm
    Inherits Form

    Public Sub New()
        Text = "Hello"
        Dim button As New Button With {.Text = "Click me", .Left = 20, .Top = 20}
        AddHandler button.Click, Sub() MessageBox.Show("Hello from Basalt.WinForms")
        Controls.Add(button)
    End Sub
End Class
```

Then start it as usual (`Application.Run(New MainForm())`). Designer files
(`*.Designer.vb`, `*.Designer.cs`) and `.resx` resources from Visual Studio
work as they are.

`Basalt.Drawing` (`System.Drawing.Common` on SkiaSharp) is a dependency and
is also published on its own, for programs that only draw.

## What is and is not supported

- Supported: the controls, layout, data binding, dialogs, clipboard,
  drag and drop, resources and the Visual Basic `My` and `MsgBox` layer, on
  Windows, macOS and Linux. Controls are drawn by the package, not by the
  operating system, so they follow one look on every platform.
- Not supported: what is the Win32 API itself. Native window handles and
  `WndProc`/`DefWndProc` message handling, `CreateParams`, P/Invoke that
  needs an `HWND`, ActiveX and COM controls (`AxHost`), and the Internet
  Explorer based `WebBrowser` engine. Code that depends on them has to be
  ported; code that stays inside the public WinForms API does not.
- The assemblies reproduce Microsoft's public API and add none; the
  repository's tests check the surface against it.
- No XML documentation ships with the package yet; IntelliSense falls back
  to Microsoft's documentation of the same API.

Source and issues: https://github.com/montagnamarco/basalt
