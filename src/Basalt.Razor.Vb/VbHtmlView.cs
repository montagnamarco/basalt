// Written out rather than relied on implicitly: this file is compiled on its
// own by the end-to-end tests, in a project without implicit usings.
using System;
using System.Collections.Generic;
using System.Text;

namespace Basalt.Razor.Vb.Runtime;

/// <summary>
/// Base class for generated .vbhtml views.
///
/// A view builds its output by appending to a buffer, which is what the
/// generated Execute method does. Encoding happens here rather than in the
/// generated code so that a template author cannot forget it: Write always
/// encodes, and WriteRaw is the explicit way to opt out.
/// </summary>
public abstract class VbHtmlView
{
    private readonly StringBuilder _output = new();

    protected VbHtmlView() => Html = new HtmlHelper { Owner = this };

    private readonly Dictionary<string, Action> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The layout this view sits inside, by path.
    ///
    /// Set from the template with "Layout = ...". Nothing means the view is
    /// the whole page, which is what a layout itself is.
    /// </summary>
    public string? Layout { get; set; }

    /// <summary>
    /// What the inner view produced, for a layout to place with RenderBody.
    ///
    /// Set by the renderer before the layout runs; a view that is not a
    /// layout never reads it.
    /// </summary>
    public string BodyContent { get; set; } = "";

    /// <summary>Writes the page. Implemented by the generated class.</summary>
    public abstract void Execute();

    /// <summary>
    /// Runs the view and returns the markup it produced.
    ///
    /// The layout is not applied here: this class has no way to find another
    /// view by path. A host that supports layouts runs the inner view, then
    /// the layout, and hands the first result to the second through
    /// BodyContent — which is what RenderWith does.
    /// </summary>
    public string Render()
    {
        _output.Clear();
        _sections.Clear();
        Execute();
        return _output.ToString();
    }

    /// <summary>
    /// Runs this view inside a layout.
    ///
    /// The inner view runs first, because a layout has to know what it is
    /// wrapping before it can place it — and because the inner view is what
    /// declares the sections the layout asks for.
    /// </summary>
    public string RenderWith(VbHtmlView layout)
    {
        var body = Render();

        layout.BodyContent = body;

        // Run the layout first, then hand it the sections: Render clears them,
        // so copying beforehand would wipe exactly what was copied. Found by
        // a test that placed a section and got "not defined" back.
        layout._output.Clear();
        layout._sections.Clear();

        foreach (var section in _sections)
            layout._sections[section.Key] = section.Value;

        layout._sectionOwner = this;

        // The same values, not a copy: a layout reading ViewData("Title")
        // wants what the view set, and a view that set nothing leaves the
        // layout reading an empty dictionary rather than failing.
        layout.ViewData = ViewData;

        layout.Execute();

        return layout._output.ToString();
    }

    /// <summary>Places what the inner view produced.</summary>
    protected void RenderBody() => _output.Append(BodyContent);

    /// <summary>
    /// How a partial view is found by name.
    ///
    /// This class cannot look one up: it knows nothing about folders or which
    /// views a project holds. The host provides the lookup, and without one a
    /// partial says so rather than rendering an empty gap.
    /// </summary>
    public Func<string, VbHtmlView?>? PartialLookup { get; set; }

    /// <summary>
    /// Renders another view in place, as @Html.Partial does in Razor.
    ///
    /// The partial is handed the same ViewData, since that is how a row
    /// template is told what to draw.
    /// </summary>
    protected void RenderPartial(string name, object? model = null)
    {
        if (PartialLookup?.Invoke(name) is not { } partial)
        {
            throw new InvalidOperationException(
                $"The partial view '{name}' was not found.");
        }

        partial.ViewData = ViewData;
        partial.PartialLookup = PartialLookup;

        if (model is not null) SetModel(partial, model);

        _output.Append(partial.Render());
    }

    /// <summary>What a partial renders to, for the Html helper.</summary>
    internal string RenderPartialToString(string name, object? model)
    {
        if (PartialLookup?.Invoke(name) is not { } partial)
        {
            throw new InvalidOperationException(
                $"The partial view '{name}' was not found.");
        }

        partial.ViewData = ViewData;
        partial.PartialLookup = PartialLookup;

        if (model is not null) SetModel(partial, model);

        return partial.Render();
    }

    /// <summary>
    /// Gives a partial its model.
    ///
    /// By reflection because Model is declared on the generated class, not
    /// here: the base cannot know the type a particular view expects.
    /// </summary>
    private static void SetModel(VbHtmlView view, object model)
    {
        var property = view.GetType().GetProperty("Model");

        if (property is null || !property.CanWrite) return;

        if (property.PropertyType.IsInstanceOfType(model)
            || property.PropertyType == typeof(object))
        {
            property.SetValue(view, model);
        }
    }

    /// <summary>
    /// Remembers a named piece of markup for the layout to place.
    ///
    /// The body runs when the layout asks for it, not here: a section at the
    /// top of a view often belongs at the bottom of the page.
    /// </summary>
    protected void DefineSection(string name, Action body) => _sections[name] = body;

    /// <summary>Whether the inner view defined a section.</summary>
    protected bool IsSectionDefined(string name) => _sections.ContainsKey(name);

    /// <summary>
    /// Places a section the inner view defined.
    ///
    /// A required section that is missing is an error the author should see;
    /// an optional one that is missing writes nothing.
    /// </summary>
    protected void RenderSection(string name, bool required = true)
    {
        if (_sections.TryGetValue(name, out var body))
        {
            // The body was written in the inner view and closes over it, so
            // it appends to that view's buffer, not this one. It is run with
            // the inner buffer emptied and the result moved across — which is
            // what makes a section appear where the layout places it rather
            // than where it was declared.
            var owner = _sectionOwner ?? this;

            var before = owner._output.Length;

            body();

            var written = owner._output.ToString(before, owner._output.Length - before);

            owner._output.Length = before;

            _output.Append(written);
            return;
        }

        if (required)
            throw new InvalidOperationException($"The section '{name}' was not defined.");
    }

    /// <summary>
    /// The view whose buffer a section body writes into.
    ///
    /// A section's body belongs to the view that declared it, so that is
    /// where its output lands before being moved to where the layout put it.
    /// </summary>
    private VbHtmlView? _sectionOwner;

    /// <summary>
    /// The helpers a template reaches through "@Html".
    ///
    /// It exists as an object because the signature help offered Html.Encode
    /// and Html.Raw while neither was callable: Html.Raw was a trick in the
    /// parser and Html was not a member at all. Advertising something that
    /// does not exist is worse than not having it.
    /// </summary>
    protected HtmlHelper Html { get; }

    /// <summary>
    /// Loose values passed to the view, reached as ViewData("Title").
    ///
    /// A dictionary rather than typed properties, which is what Razor does
    /// and what a layout needs: it has to read values set by a view it knows
    /// nothing about.
    /// </summary>
    public ViewDataDictionary ViewData { get; internal set; } = new();

    /// <summary>
    /// The same values, reached as ViewBag.Title.
    ///
    /// Late bound, which Option Strict Off permits and which is what makes
    /// the shorthand read the way it does in Razor.
    /// </summary>
    public dynamic ViewBag => ViewData;

    /// <summary>Appends literal markup from the template, already trusted.</summary>
    protected void WriteLiteral(string text) => _output.Append(text);

    /// <summary>
    /// Appends a value, HTML-encoded.
    ///
    /// Encoding by default is what keeps a template from turning user data into
    /// markup; a template that genuinely holds markup uses WriteRaw instead.
    /// </summary>
    protected void Write(object? value)
    {
        if (value is null) return;

        // Anything that says it is already markup writes itself. RawContent
        // is the common case; the interface is what lets a template compose
        // safe HTML of its own without going through Raw, which switches
        // encoding off for the whole value.
        if (value is IHtmlContent content)
        {
            content.WriteTo(_output);
            return;
        }

        Encode(_output, value.ToString());
    }

    /// <summary>
    /// Appends an attribute, or nothing at all.
    ///
    /// Nothing and False remove the attribute: <c>disabled="@isDisabled"</c>
    /// rendering as <c>disabled="False"</c> would disable the control, since
    /// a browser reads the attribute's presence and not its value. True
    /// renders the attribute with its own name as the value, which is how
    /// <c>disabled="disabled"</c> is written.
    ///
    /// The generated code calls this only where the whole value is one
    /// expression and the name is not data-*, where False is real data.
    /// </summary>
    protected void WriteAttribute(string name, object? value)
    {
        if (value is null or false) return;

        _output.Append(' ').Append(name).Append("=\"");

        if (value is true)
            _output.Append(name);
        else if (value is IHtmlContent content)
            content.WriteTo(_output);
        else
            Encode(_output, value.ToString());

        _output.Append('"');
    }

    /// <summary>Appends a value without encoding it.</summary>
    protected void WriteRaw(object? value)
    {
        if (value is null) return;
        _output.Append(value);
    }

    /// <summary>Marks a value as markup that must not be encoded.</summary>
    public static RawContent Raw(object? value) => new(value?.ToString() ?? "");

    /// <summary>
    /// Escapes the five characters that would otherwise be read as markup.
    ///
    /// Quotes are escaped as well as angle brackets, because a value can land
    /// inside an attribute where a bare quote would end it early.
    ///
    /// Internal rather than private so the Html helper encodes exactly the
    /// same way the page does: two encoders that could disagree would be a
    /// security question, not a tidiness one.
    /// </summary>
    internal static void Encode(StringBuilder builder, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var c in text!)
        {
            switch (c)
            {
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '&': builder.Append("&amp;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                default: builder.Append(c); break;
            }
        }
    }
}

/// <summary>
/// A value that is already markup.
///
/// Write() asks this of anything it is given, so a type can carry HTML
/// safely without the template calling Raw — which turns encoding off for the
/// whole value and cannot be composed. A helper that builds a fragment out of
/// encoded parts implements this and stays safe.
/// </summary>
public interface IHtmlContent
{
    /// <summary>Appends this content, already encoded as it should be.</summary>
    void WriteTo(StringBuilder builder);
}

/// <summary>Markup that is written out as-is.</summary>
public sealed class RawContent : IHtmlContent
{
    public RawContent(string value) => Value = value;

    public string Value { get; }

    public void WriteTo(StringBuilder builder) => builder.Append(Value);

    public override string ToString() => Value;
}

/// <summary>
/// Markup built from parts, each encoded or raw as it is added.
///
/// The reason the interface exists: composing a fragment out of user data
/// used to mean building a string and calling Raw on the result, which turns
/// encoding off for everything in it — including the part that came from the
/// user.
/// </summary>
public sealed class HtmlContentBuilder : IHtmlContent
{
    private readonly List<Action<StringBuilder>> _parts = new();

    /// <summary>Adds text, to be encoded when written.</summary>
    public HtmlContentBuilder Append(string? text)
    {
        _parts.Add(builder => VbHtmlView.Encode(builder, text));
        return this;
    }

    /// <summary>Adds markup, written out as it stands.</summary>
    public HtmlContentBuilder AppendHtml(string? markup)
    {
        _parts.Add(builder => builder.Append(markup));
        return this;
    }

    /// <summary>Adds other content, which writes itself.</summary>
    public HtmlContentBuilder AppendHtml(IHtmlContent? content)
    {
        if (content is not null) _parts.Add(content.WriteTo);
        return this;
    }

    public void WriteTo(StringBuilder builder)
    {
        foreach (var part in _parts) part(builder);
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        WriteTo(builder);
        return builder.ToString();
    }
}

/// <summary>
/// What a template reaches through <c>@Html</c>.
///
/// Deliberately small: the members here are the ones a server-rendered view
/// actually uses, and each does what its ASP.NET counterpart does so that a
/// template written against Razor reads the same way.
/// </summary>
public sealed class HtmlHelper
{
    /// <summary>
    /// Marks a value as markup that must not be encoded.
    ///
    /// The same thing the parser used to do by name; now it is a real call,
    /// so it can be passed around and its result stored.
    /// </summary>
    public RawContent Raw(object? value) => new(value?.ToString() ?? "");

    /// <summary>
    /// The view this helper belongs to, so Partial can render into it.
    /// </summary>
    internal VbHtmlView? Owner { get; set; }

    /// <summary>
    /// Renders another view in place: <c>@Html.Partial("_Row")</c>.
    /// </summary>
    public RawContent Partial(string name, object? model = null)
    {
        if (Owner is null) return new RawContent("");

        return new RawContent(Owner.RenderPartialToString(name, model));
    }

    /// <summary>
    /// A fragment built from parts, each encoded or raw as it is added.
    ///
    /// <c>@Html.Content().Append(user.Name).AppendHtml("&lt;br&gt;")</c> stays
    /// safe where building the same string and calling Raw on it would not:
    /// Raw switches encoding off for everything, the user's name included.
    /// </summary>
    public HtmlContentBuilder Content() => new();

    /// <summary>Encodes a value as the page would encode it.</summary>
    public string Encode(object? value)
    {
        if (value is null) return "";

        var written = new StringBuilder();

        VbHtmlView.Encode(written, value.ToString());

        return written.ToString();
    }
}

/// <summary>
/// The loose values a view is handed.
///
/// Reading a key that was never set gives Nothing rather than throwing: a
/// layout asks for ViewData("Title") without knowing whether the view set
/// one, and an exception there would be a page that fails to render over a
/// missing heading.
/// </summary>
public sealed class ViewDataDictionary : System.Dynamic.DynamicObject
{
    private readonly Dictionary<string, object?> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    /// <summary>Whether a key was set at all, which is not the same as being null.</summary>
    public bool Contains(string key) => _values.ContainsKey(key);

    public int Count => _values.Count;

    /// <summary>Reading ViewBag.Something.</summary>
    public override bool TryGetMember(
        System.Dynamic.GetMemberBinder binder, out object? result)
    {
        result = this[binder.Name];
        return true;
    }

    /// <summary>Writing ViewBag.Something = value.</summary>
    public override bool TrySetMember(
        System.Dynamic.SetMemberBinder binder, object? value)
    {
        this[binder.Name] = value;
        return true;
    }
}
