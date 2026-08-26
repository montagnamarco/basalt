using System.Globalization;
using System.Xml.Linq;

namespace Basalt.Vb6;

/// <summary>
/// Turns a Visual Basic 6 form into Avalonia markup.
/// </summary>
/// <remarks>
/// Onto a Canvas, because that is what a .frm is: every control carries a Left
/// and a Top and stays where it was put. A layout panel would rearrange the
/// form, which is the one thing someone opening a twenty-year-old project does
/// not want.
///
/// A control from an OCX becomes a placeholder rather than being dropped. The
/// .frm names it and describes it even when the OCX is nowhere on the machine,
/// so the form can be opened, read and worked on with the missing piece shown
/// for what it is — which is more use than a refusal.
/// </remarks>
public static class FormToAxaml
{
    /// <summary>Twips to device-independent pixels.</summary>
    /// <remarks>
    /// 1440 twips to the inch, 96 pixels to the inch. Every position and size
    /// in a .frm is in twips, and a form written at 4680 wide is 312 across.
    /// </remarks>
    public const double TwipsPerPixel = 15.0;

    /// <summary>The Avalonia control each intrinsic VB6 control becomes.</summary>
    private static readonly Dictionary<string, string> Equivalents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["VB.TextBox"] = "TextBox",
            ["VB.CommandButton"] = "Button",
            ["VB.Label"] = "TextBlock",
            ["VB.CheckBox"] = "CheckBox",
            ["VB.OptionButton"] = "RadioButton",
            ["VB.ComboBox"] = "ComboBox",
            ["VB.ListBox"] = "ListBox",
            ["VB.Frame"] = "HeaderedContentControl",
            ["VB.PictureBox"] = "Border",
            ["VB.Image"] = "Image",
            ["VB.HScrollBar"] = "ScrollBar",
            ["VB.VScrollBar"] = "ScrollBar",
            ["VB.Line"] = "Rectangle",
            ["VB.Shape"] = "Rectangle",
        };

    /// <summary>Writes the form as Avalonia markup.</summary>
    public static string Convert(FormFile form, string className = "Form1")
    {
        var ns = XNamespace.Get("https://github.com/avaloniaui");
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var canvas = new XElement(ns + "Canvas");

        foreach (var child in form.Root.Children)
            canvas.Add(Element(child, ns, x));

        var window = new XElement(ns + "Window",
            new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName),
            new XAttribute(x + "Class", className),
            new XAttribute("Title", form.Root.Text("Caption") ?? className),
            new XAttribute("Width", Pixels(form.Root.Number("ClientWidth", 4800))),
            new XAttribute("Height", Pixels(form.Root.Number("ClientHeight", 3600))),
            canvas);

        window.SetAttributeValue("xmlns", ns.NamespaceName);

        return window.ToString();
    }

    private static XElement Element(FormControl control, XNamespace ns, XNamespace x)
    {
        var element = control.IsIntrinsic
                   && Equivalents.TryGetValue(control.Type, out var name)
            ? new XElement(ns + name)
            : Placeholder(control, ns);

        element.SetAttributeValue(x + "Name", control.Name);
        element.SetAttributeValue("Canvas.Left", Pixels(control.Number("Left")));
        element.SetAttributeValue("Canvas.Top", Pixels(control.Number("Top")));

        if (control.Properties.ContainsKey("Width"))
            element.SetAttributeValue("Width", Pixels(control.Number("Width")));

        if (control.Properties.ContainsKey("Height"))
            element.SetAttributeValue("Height", Pixels(control.Number("Height")));

        // Caption is the text on a button or a label; Text is what is in a
        // box. Avalonia spells both differently depending on the control, so
        // the mapping is by target rather than by source.
        var caption = control.Text("Caption") ?? control.Text("Text");

        if (caption is not null)
        {
            var property = element.Name.LocalName switch
            {
                "Button" or "CheckBox" or "RadioButton" => "Content",
                "HeaderedContentControl" => "Header",
                "TextBox" => "Text",
                "TextBlock" => "Text",
                _ => null
            };

            if (property is not null) element.SetAttributeValue(property, caption);
        }

        if (control.Text("Enabled") is "0" or "False")
            element.SetAttributeValue("IsEnabled", "False");

        if (control.Text("Visible") is "0" or "False")
            element.SetAttributeValue("IsVisible", "False");

        // A Frame holds other controls, and so does the placeholder standing in
        // for a container from an OCX: what was inside must stay inside, or a
        // tab page's contents end up loose on the form.
        if (control.Children.Count > 0)
        {
            var inner = new XElement(ns + "Canvas");

            foreach (var child in control.Children)
                inner.Add(Element(child, ns, x));

            element.Add(inner);
        }

        return element;
    }

    /// <summary>
    /// What stands in for a control this machine does not have.
    /// </summary>
    /// <remarks>
    /// Named and outlined rather than blank: the point is that the form can be
    /// opened and read with the gap visible, so the author knows what has to be
    /// replaced instead of finding out when it does not appear.
    /// </remarks>
    private static XElement Placeholder(FormControl control, XNamespace ns) =>
        new(ns + "Border",
            new XAttribute("BorderThickness", "1"),
            new XAttribute("BorderBrush", "#C2491A"),
            new XAttribute("Background", "#1A000000"),
            new XAttribute("Tag", control.Type),
            new XElement(ns + "TextBlock",
                new XAttribute("Text", control.Type),
                new XAttribute("Margin", "4"),
                new XAttribute("Foreground", "#C2491A"),
                new XAttribute("FontSize", "10")));

    private static string Pixels(double twips) =>
        Math.Round(twips / TwipsPerPixel).ToString(CultureInfo.InvariantCulture);
}
