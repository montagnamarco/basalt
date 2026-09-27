using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Basalt.Razor.Vb.Generator;

/// <summary>
/// Whether generated views and components are written with Option Strict On.
/// </summary>
/// <remarks>
/// The project's own setting, as the compiler has it, unless the project
/// says otherwise for its templates with VbRazorOptionStrict (On or Off):
/// a project moving to Option Strict On one file at a time can hold its .vb
/// files to it before its views.
///
/// Read through reflection because the generator is built against
/// Microsoft.CodeAnalysis.Common, which has no VisualBasicCompilationOptions;
/// referencing the Visual Basic compiler package would pin every consumer to
/// its version.
/// </remarks>
internal static class ProjectOptionStrict
{
    public static bool IsOn(Compilation compilation, AnalyzerConfigOptionsProvider options)
    {
        if (options.GlobalOptions.TryGetValue("build_property.VbRazorOptionStrict", out var chosen) &&
            chosen is { Length: > 0 })
            return string.Equals(chosen.Trim(), "On", StringComparison.OrdinalIgnoreCase);

        var strict = compilation.Options.GetType().GetProperty("OptionStrict")?.GetValue(compilation.Options);

        return string.Equals(strict?.ToString(), "On", StringComparison.Ordinal);
    }
}
