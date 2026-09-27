using System.Runtime.Loader;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basalt.Tests;

/// <summary>
/// Compiles .vbrazor components with the real generator and renders one of
/// them to HTML with ASP.NET Core's own HtmlRenderer.
/// </summary>
/// <remarks>
/// What a test of the generated code cannot show: that the render tree the
/// component builds is the one Blazor accepts and turns into the markup
/// expected — fragments in their place, a context value where it is used, a
/// cascaded value arriving. The assembly is loaded into a collectible
/// context of its own, so each test's components come and go with it.
/// </remarks>
internal static class ComponentRendering
{
    public static readonly string Site = Path.Combine(Path.GetTempPath(), "Site");

    public static (string, string) Component(string name, string text) =>
        (Path.Combine(Site, "Components", name + ".vbrazor"), text);

    /// <summary>Renders the component named, from the templates and Visual Basic given.</summary>
    public static async Task<string> RenderAsync(
        string componentName, string[] code, params (string Path, string Text)[] templates)
    {
        var outcome = GeneratorRun.Run("VbComponentGenerator", Site, optionStrict: true, properties: null, code, templates);

        Assert.Null(outcome.Exception);
        Assert.Empty(outcome.CompilationErrors);

        using var image = new MemoryStream();

        var emitted = outcome.Compilation!.Emit(image);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        image.Position = 0;

        var context = new AssemblyLoadContext("rendering-" + Guid.NewGuid().ToString("N"), isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(image);
            var type = assembly.GetTypes().Single(t => t.Name == componentName && typeof(IComponent).IsAssignableFrom(t));

            await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync(type, ParameterView.Empty);

                return output.ToHtmlString();
            });
        }
        finally
        {
            context.Unload();
        }
    }
}
