using System.Globalization;
using Basalt.Workspace;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Shell.Controls;

/// <summary>Keeps invalid input until the user corrects or removes its row.</summary>
internal sealed class WinFormsColumnDraft
{
    public WinFormsColumnDraft(WinFormsColumnDefinition column)
    {
        Name = column.Name;
        TypeName = column.TypeName;
        HeaderText = column.HeaderText;
        Width = column.Width.ToString(CultureInfo.InvariantCulture);
        OriginalName = column.OriginalName;
    }

    public string Name { get; set; }
    public string? TypeName { get; set; }
    public string HeaderText { get; set; }
    public string Width { get; set; }
    public string? OriginalName { get; }

    public bool TryCreateDefinition(out WinFormsColumnDefinition? definition)
    {
        definition = null;
        if (!SyntaxFacts.IsValidIdentifier(Name)
            || TypeName is null
            || !WinFormsCollectionDocument.ColumnTypes.Contains(TypeName, StringComparer.Ordinal)
            || !int.TryParse(Width, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || width is < 2 or > 65536
            || !WinFormsEditorText.IsSingleLine(HeaderText))
        {
            return false;
        }

        definition = new WinFormsColumnDefinition(Name, TypeName, HeaderText, width, OriginalName);
        return true;
    }
}
