using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Basalt.Designer.Model;

namespace Basalt.Designer;

public sealed record PreviewResult(Control? Root, string? Error)
{
    public bool Succeeded => Root is not null;
}

/// <summary>
/// Instantiates the user's XAML using the real Avalonia engine, in-process.
///
/// This is why the IDE is itself an Avalonia application: the preview is not an
/// approximation redrawn on a canvas, but the authentic visual tree, with the
/// same styles, layout and themes the application will have at runtime.
/// </summary>
public sealed class XamlPreviewRenderer
{
    /// <summary>
    /// Assembly of the user's project, needed to resolve the custom controls
    /// and converters referenced by the XAML. If it is null, only Avalonia's
    /// built-in controls can be rendered.
    /// </summary>
    public Assembly? LocalAssembly { get; set; }

    public PreviewResult Render(XamlDocument document) => Render(document.ToXaml());

    /// <summary>
    /// Removes x:Class from the document to be rendered.
    ///
    /// The XAML loader requires the code-behind type to already exist in the
    /// loaded assembly. At design time that is almost never the case: the window
    /// is new, or was modified after the last build. Since the preview shows only
    /// the visual tree, the attribute can be dropped without any loss.
    /// </summary>
    private static string StripClassAttribute(string xaml)
    {
        try
        {
            var xml = System.Xml.Linq.XDocument.Parse(xaml, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            xml.Root?.Attribute(XamlDocument.XamlNs + "Class")?.Remove();
            return xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        }
        catch (System.Xml.XmlException)
        {
            // Malformed XAML: pass it to the loader, which will produce a more
            // precise error message than we could here.
            return xaml;
        }
    }

    public PreviewResult Render(string xaml)
    {
        try
        {
            var configuration = new RuntimeXamlLoaderConfiguration
            {
                // In design mode Avalonia applies the d:DesignWidth and
                // d:DesignHeight attributes and does not run the initialization
                // code reserved for runtime.
                DesignMode = true,
                LocalAssembly = LocalAssembly
            };

            var document = new RuntimeXamlLoaderDocument(StripClassAttribute(xaml));
            var instance = AvaloniaRuntimeXamlLoader.Load(document, configuration);

            // A window cannot be hosted inside another window: for the preview
            // its content is shown instead.
            return instance switch
            {
                Window window => new PreviewResult(UnwrapWindow(window), null),
                Control control => new PreviewResult(control, null),
                _ => new PreviewResult(null, $"The root of the document is not a control: {instance?.GetType().Name ?? "null"}.")
            };
        }
        catch (Exception ex)
        {
            // While typing, the XAML is almost always temporarily invalid: the
            // error should be displayed, not propagated.
            return new PreviewResult(null, Describe(ex));
        }
    }

    /// <summary>Extracts a window's content, preserving its design-time size.</summary>
    /// <remarks>
    /// An empty window gets an empty panel rather than nothing: returning
    /// null left the surface with no control and no error to show either, so
    /// a new file looked exactly like a designer that had broken. The panel
    /// is also somewhere to drop the first control, which an empty window
    /// otherwise has no room for.
    /// </remarks>
    private static Control UnwrapWindow(Window window)
    {
        var content = window.Content as Control ?? new Panel();

        window.Content = null;

        if (!double.IsNaN(window.Width)) content.Width = window.Width;
        if (!double.IsNaN(window.Height)) content.Height = window.Height;

        return content;
    }

    private static string Describe(Exception ex)
    {
        // The XAML loader's exceptions wrap the real cause.
        var message = ex.Message;
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            message += $"\n  → {inner.Message}";
        return message;
    }
}
