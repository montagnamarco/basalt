# Reference images from Windows

Draws the scenarios of `Scenarios.cs` with Microsoft's GDI+ and writes, next to
the executable, a `goldens` folder with one PNG per scenario and
`measurements.json` (font metrics and `MeasureString` results).

Build the Windows executable from any system:

```
dotnet publish tests/Basalt.Drawing.Goldens -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/goldens
```

Run `artifacts/goldens/Basalt.Drawing.Goldens.exe` on Windows (a virtual machine
with the repository's folder shared works: the output lands back here), then
copy `artifacts/goldens/goldens/*` into `tests/Basalt.Drawing.Tests/Goldens/`.
