namespace Basalt.Shell.Controls;

/// <summary>Validation helpers shared by the collection and column editors' text fields.</summary>
internal static class WinFormsEditorText
{
    public static bool IsSingleLine(string value) => value.IndexOfAny(['\r', '\n']) < 0;
}
