using System.Text;
using System.Xml.Linq;
using Basalt.Core.Model;
using Basalt.Designer.Model;

namespace Basalt.Designer;

/// <summary>Where a handler ended up, so the editor can go there.</summary>
public sealed record HandlerLocation(string FilePath, string MethodName, string Code, bool WasCreated);

/// <summary>
/// Wiring a control on the form to a method in the code-behind.
/// </summary>
/// <remarks>
/// This is the part that made Visual Basic what it was: double-click a button
/// and you are in the code that runs when it is pressed. Without it the
/// designer can draw a form and not make it do anything, which is a drawing
/// program rather than a development environment.
///
/// The XAML holds the wiring — <c>Click="OkButton_Click"</c> — and the
/// code-behind holds the method. Both have to be written, and the pair has to
/// stay consistent: an attribute naming a method that does not exist is a
/// window that will not load.
/// </remarks>
public static class EventHandlers
{
    /// <summary>
    /// The event a control gets when it is double-clicked.
    /// </summary>
    /// <remarks>
    /// One per kind, and the one a person means: a button means Click, a text
    /// box means TextChanged. Anything not listed falls back to what every
    /// control has, so the gesture always does something.
    /// </remarks>
    public static string DefaultEventFor(string elementName) => elementName switch
    {
        "Button" or "RepeatButton" or "ToggleButton" or "MenuItem" => "Click",
        "TextBox" or "AutoCompleteBox" => "TextChanged",
        "CheckBox" or "RadioButton" or "ToggleSwitch" => "IsCheckedChanged",
        "ComboBox" or "ListBox" or "ListView" or "TabControl" => "SelectionChanged",
        "Slider" or "ProgressBar" or "NumericUpDown" => "ValueChanged",
        "TreeView" => "SelectionChanged",
        "Calendar" or "DatePicker" => "SelectedDateChanged",
        "Window" => "Opened",
        _ => "PointerPressed",
    };

    /// <summary>
    /// The events a control offers, most useful first.
    /// </summary>
    /// <remarks>
    /// A chosen list rather than everything reflection can find. A Button
    /// has some ninety events once the inherited ones are counted, and a
    /// list that long is one nobody reads: these are the ones a form
    /// actually handles, and the rest can still be written by hand.
    ///
    /// The control's own come first, then the ones every control has, so
    /// Click is at the top for a Button rather than sorted under P.
    /// </remarks>
    public static IReadOnlyList<string> EventsFor(string elementName)
    {
        var own = elementName switch
        {
            "Button" or "RepeatButton" or "ToggleButton" or "MenuItem" =>
                new[] { "Click" },
            "TextBox" or "AutoCompleteBox" =>
                ["TextChanged"],
            "CheckBox" or "RadioButton" or "ToggleSwitch" =>
                ["IsCheckedChanged", "Click"],
            "ComboBox" or "ListBox" or "ListView" or "TreeView" or "TabControl" =>
                ["SelectionChanged"],
            "Slider" or "NumericUpDown" =>
                ["ValueChanged"],
            "Calendar" or "DatePicker" =>
                ["SelectedDateChanged"],
            "Window" =>
                ["Opened", "Closing", "Closed"],
            _ => [],
        };

        return [.. own, .. Common.Where(e => !own.Contains(e))];
    }

    /// <summary>
    /// The events every control has.
    /// </summary>
    /// <remarks>
    /// Pointer and keyboard before focus and layout: a form handles a click
    /// or a key far more often than it handles being laid out.
    /// </remarks>
    private static readonly string[] Common =
    [
        "PointerPressed",
        "PointerReleased",
        "PointerMoved",
        "PointerEntered",
        "PointerExited",
        "DoubleTapped",
        "KeyDown",
        "KeyUp",
        "GotFocus",
        "LostFocus",
        "Loaded",
        "SizeChanged",
    ];

    /// <summary>
    /// The name a handler gets: the control, then the event.
    /// </summary>
    /// <remarks>
    /// The convention every Visual Basic developer already reads without
    /// thinking, and the reason a control has to be named before it can have
    /// a handler at all.
    /// </remarks>
    public static string NameFor(string controlName, string eventName) =>
        $"{controlName}_{eventName}";

    /// <summary>
    /// A name for a control that has none.
    /// </summary>
    /// <remarks>
    /// A handler refers to its control by name, so an unnamed control cannot
    /// have one. Numbered from the ones already there — Button1, Button2 —
    /// which is both what Visual Basic did and what people expect to see.
    /// </remarks>
    public static string SuggestName(XElement element, XElement root)
    {
        var kind = element.Name.LocalName;

        var taken = root
            .DescendantsAndSelf()
            .Select(XamlDocument.GetName)
            .Where(name => name is { Length: > 0 })
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 1; ; i++)
        {
            var candidate = kind + i.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (taken.Add(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Adds a handler to the code-behind, or finds the one already there.
    /// </summary>
    /// <remarks>
    /// Never twice: double-clicking a button that already has a handler is
    /// how a person goes back to code they wrote, and generating a second
    /// copy would break the build in a way they did not ask for.
    ///
    /// The method is put before the last <c>End Class</c>, which is where a
    /// person would put it and keeps the file compiling.
    /// </remarks>
    public static string AddHandler(
        string code, string methodName, string eventName, SourceLanguage language)
    {
        if (Contains(code, methodName, language)) return code;

        return language switch
        {
            SourceLanguage.VisualBasic => AddVisualBasic(code, methodName, eventName),
            _ => AddCSharp(code, methodName, eventName),
        };
    }

    /// <summary>Whether the code already declares this handler.</summary>
    public static bool Contains(string code, string methodName, SourceLanguage language) =>
        language == SourceLanguage.VisualBasic
            ? code.Contains($"Sub {methodName}(", StringComparison.Ordinal)
            : code.Contains($"void {methodName}(", StringComparison.Ordinal);

    /// <summary>Which line the handler is on, so the editor can go to it.</summary>
    public static int LineOf(string code, string methodName, SourceLanguage language)
    {
        var marker = language == SourceLanguage.VisualBasic
            ? $"Sub {methodName}("
            : $"void {methodName}(";

        var lines = code.Split('\n');

        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Contains(marker, StringComparison.Ordinal))
                return i + 1;

        return 1;
    }

    private static string AddVisualBasic(string code, string methodName, string eventName)
    {
        var body = new StringBuilder();

        body.AppendLine();
        body.AppendLine($"    Private Sub {methodName}(sender As Object, e As {ArgsFor(eventName)})");
        body.AppendLine();
        body.AppendLine("    End Sub");

        return InsertBefore(code, "End Class", body.ToString());
    }

    private static string AddCSharp(string code, string methodName, string eventName)
    {
        var body = new StringBuilder();

        body.AppendLine();
        body.AppendLine($"    private void {methodName}(object? sender, {ArgsFor(eventName)} e)");
        body.AppendLine("    {");
        body.AppendLine("    }");

        return InsertBefore(code, "}", body.ToString());
    }

    /// <summary>
    /// The argument type an event hands its handler.
    /// </summary>
    /// <remarks>
    /// Wrong here means a handler that will not compile, and the user has to
    /// work out why. Only the ones the toolbox can produce are listed; the
    /// rest take the base type, which always compiles even where a more
    /// precise one exists.
    /// </remarks>
    private static string ArgsFor(string eventName) => eventName switch
    {
        "Click" => "Avalonia.Interactivity.RoutedEventArgs",
        "TextChanged" or "SelectionChanged" or "IsCheckedChanged" or "SelectedDateChanged"
            => "Avalonia.Interactivity.RoutedEventArgs",
        "ValueChanged" => "Avalonia.AvaloniaPropertyChangedEventArgs",
        "PointerPressed" => "Avalonia.Input.PointerPressedEventArgs",
        "Opened" => "EventArgs",
        _ => "Avalonia.Interactivity.RoutedEventArgs",
    };

    /// <summary>
    /// Puts the method before the last closing line of the class.
    /// </summary>
    /// <remarks>
    /// The last one rather than the first: a file with a namespace ends with
    /// two, and putting the method before the first would land it inside
    /// whatever nested type happened to close there.
    /// </remarks>
    private static string InsertBefore(string code, string marker, string body)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n').ToList();

        var at = lines.FindLastIndex(line =>
            line.TrimStart().StartsWith(marker, StringComparison.Ordinal));

        if (at < 0) return code.TrimEnd() + "\n" + body;

        lines.InsertRange(at, body.TrimEnd('\n').Split('\n'));

        return string.Join("\n", lines);
    }
}
