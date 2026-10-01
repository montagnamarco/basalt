# Reference behaviour from Windows

Runs the scenarios of `Scenarios.cs` against Microsoft's own
`System.Windows.Forms` and writes, next to the executable, a `goldens` folder
with `behaviour.json`: one entry per scenario, each the list of lines it
produced (event names in the order they fired, then the final state it read;
a scenario that throws instead records a single `"throws <ExceptionType>"`
line).

Build the Windows executable from any system:

```
dotnet publish tests/Basalt.Forms.Goldens -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/forms-goldens
```

Run `artifacts/forms-goldens/Basalt.Forms.Goldens.exe` on Windows (a virtual
machine with the repository's folder shared works: the output lands back
here), then copy `artifacts/forms-goldens/goldens/behaviour.json` into
`tests/Basalt.Forms.Tests/Goldens/behaviour.json`.
