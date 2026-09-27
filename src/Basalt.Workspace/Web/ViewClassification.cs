using Basalt.Razor.Vb;

namespace Basalt.Workspace.Web;

/// <summary>
/// What each name in a template's Visual Basic is, by Roslyn, placed back in
/// the template.
/// </summary>
/// <remarks>
/// The template is generated to Visual Basic, the generated code classified,
/// and each name carried back through the verified mappings: by line where
/// the writer copied the line, by the covering mapping where the text from
/// its start agrees. A name is kept only where the template holds the same
/// text; one that cannot be placed exactly is left uncoloured, never
/// coloured next door.
/// </remarks>
public static class ViewClassification
{
    /// <summary>A name in the template: where it starts, how long, and Roslyn's kind for it.</summary>
    public sealed record Name(int Start, int Length, string Kind);

    public static async Task<IReadOnlyList<Name>> ClassifyAsync(
        string path,
        string template,
        ViewHost host,
        Func<string, CancellationToken, Task<IReadOnlyList<(int Start, int Length, string Kind)>>> classifyGenerated,
        CancellationToken ct = default)
    {
        var generated = await TemplateGeneration.ForAsync(path, host, template, null, ct).ConfigureAwait(false);
        var spans = await classifyGenerated(generated.Code, ct).ConfigureAwait(false);

        var names = new List<Name>();

        foreach (var (start, length, kind) in spans)
        {
            ct.ThrowIfCancellationRequested();

            var at = TemplateGeneration.StatementLineOriginal(path, template, generated, start)
                     ?? TemplateGeneration.VerifiedSpanOriginal(template, generated, start);

            if (at is not { } place || place + length > template.Length) continue;

            if (string.CompareOrdinal(template, place, generated.Code, start, length) != 0) continue;

            names.Add(new Name(place, length, kind));
        }

        return [.. names.DistinctBy(name => name.Start).OrderBy(name => name.Start)];
    }
}
