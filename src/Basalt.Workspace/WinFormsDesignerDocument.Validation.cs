using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed partial class WinFormsDesignerDocument
{
    public WinFormsCollectionDocument Collections { get; }

    /// <summary>
    /// Refuses a file with a syntax error. The message names the line and column of its first error, which the designer
    /// shows in its status line.
    /// </summary>
    private static void ThrowOnSyntaxErrors(CompilationUnitSyntax syntax)
    {
        if (!syntax.ContainsDiagnostics) return;
        var start = syntax.GetDiagnostics().FirstOrDefault()?.Location.GetLineSpan().StartLinePosition ?? default;
        throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerSyntaxErrors, start.Line + 1, start.Character + 1));
    }

    private void ValidateControlHierarchy()
    {
        var controls = Controls.ToDictionary(control => control.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var control in Controls)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = control;
            while (true)
            {
                if (!visited.Add(current.Name)) throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerHierarchyCycle));
                if (current.ParentName.Equals("Me", StringComparison.OrdinalIgnoreCase)
                    || !controls.TryGetValue(current.ParentName, out current!)) break;
            }
        }
    }
}
