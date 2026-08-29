using Basalt.Core.Model;

namespace Basalt.Designer;

public sealed record NewFormResult(string XamlPath, string CodeBehindPath);

/// <summary>
/// Creates a new window or user control: the .axaml file and its matching
/// code-behind, in the language of the project that hosts them.
/// </summary>
public static class FormTemplates
{
    public static string WindowXaml(string className, string title) =>
        $"""
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="{className}"
                Width="800" Height="500"
                Title="{title}">
          <Grid>
          </Grid>
        </Window>
        """;

    public static string UserControlXaml(string className) =>
        $"""
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="{className}">
          <Grid>
          </Grid>
        </UserControl>
        """;

    /// <summary>Writes the XAML and code-behind of a new window to disk.</summary>
    public static async Task<NewFormResult> CreateWindowAsync(
        string directory,
        string className,
        SourceLanguage language,
        string? title = null,
        CancellationToken ct = default)
    {
        var typeName = className.Contains('.') ? className[(className.LastIndexOf('.') + 1)..] : className;
        var xamlPath = Path.Combine(directory, $"{typeName}.axaml");

        if (File.Exists(xamlPath))
            throw new IOException($"There is already a file named {typeName}.axaml in this folder.");

        var xaml = WindowXaml(className, title ?? typeName);
        await File.WriteAllTextAsync(xamlPath, xaml, ct).ConfigureAwait(false);

        var document = Model.XamlDocument.Parse(xaml, xamlPath);
        var codeBehindPath = CodeBehindGenerator.CodeBehindPath(xamlPath, language);
        await File.WriteAllTextAsync(
            codeBehindPath, CodeBehindGenerator.Generate(document, language), ct).ConfigureAwait(false);

        return new NewFormResult(xamlPath, codeBehindPath);
    }
}
