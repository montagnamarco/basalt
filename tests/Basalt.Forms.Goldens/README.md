# Reference behaviour and painting from Windows

Runs the scenarios of `Scenarios.cs` and `VisualScenarios*.cs` against
Microsoft's own `System.Windows.Forms` and writes, next to the executable, a
`goldens` folder with:

- `behaviour.json`: one entry per `Scenarios.cs` scenario, each the list of
  lines it produced (event names in the order they fired, then the final
  state it read; a scenario that throws instead records a single
  `"throws <ExceptionType>"` line).
- `visual/<name>.png`: one bitmap per `VisualScenarios.cs` scenario, painted
  under the classic (Windows 2000) theme — `EnableVisualStyles` is never
  called, and `Application.VisualStyleState` is forced to `NoneEnabled` before
  any control is created, so the comparison is against the same flat look
  Basalt.Forms itself paints.
- `visual/manifest.json`: for every visual scenario, its name, its painted
  width and height, and the text rectangles (in control coordinates) where
  rasterised glyphs land — GDI and Skia draw text differently, so the test
  only reports mismatches there instead of asserting on them.

Build the Windows executable from any system:

```
dotnet publish tests/Basalt.Forms.Goldens -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/forms-goldens
```

Run `artifacts/forms-goldens/Basalt.Forms.Goldens.exe` on Windows (a virtual
machine with the repository's folder shared works: the output lands back
here), then copy:

> Run it with the Windows display scale at **100% (96 DPI)**: at any other
> scale WinForms scales fonts and metrics and every bitmap changes size. In
> Parallels, set the virtual machine's display to "Scaled" (not "Retina"/
> "Best for Retina") and check Settings > System > Display > Scale = 100%.


- `artifacts/forms-goldens/goldens/behaviour.json` into
  `tests/Basalt.Forms.Tests/Goldens/behaviour.json`.
- `artifacts/forms-goldens/goldens/visual/` (every PNG plus `manifest.json`)
  into `tests/Basalt.Forms.Tests/Goldens/Visual/`.

Both `BehaviourGoldenTests` and `VisualGoldenTests` skip, with a message
pointing back here, until their respective recording exists.
