; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------------------------------------------------
VBH100 | Basalt.Razor.Vb | Error | A .vbhtml template could not be parsed correctly.
VBH101 | Basalt.Razor.Vb | Error | Parsing a .vbhtml template threw an unexpected error.
VBH102 | Basalt.Razor.Vb | Warning | A .vbhtml template appears in a non-Visual Basic project.
VBRZ010 | Basalt.Razor.Vb | Warning | A .vbrazor component appears in a non-Visual Basic project.
VBRZ011 | Basalt.Razor.Vb | Error | A .vbrazor component could not be read.
VBRZ012 | Basalt.Razor.Vb | Warning | A .vbrazor component in a project that does not reference Blazor.
VBP100 | Basalt.Pages | Error | A .vbpage page could not be generated.
VBRZ013 | Basalt.Razor.Vb | Error | A .vbrazor component has a template problem the parser reported.
VBRZ014 | Basalt.Razor.Vb | Warning | A .vbrazor component uses a directive that is not supported yet and is ignored.
VBRZ015 | Basalt.Razor.Vb | Warning | The parameters of the project's components could not be read; components are written untyped.
VBH103 | Basalt.Razor.Vb | Warning | A .vbhtml view whose path from the project folder cannot be worked out.
