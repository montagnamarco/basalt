using System.Xml.Linq;
using Basalt.Core.Model;
using Basalt.Designer.Model;
using Basalt.Designer.Toolbox;

namespace Basalt.Designer;

/// <summary>
/// State of a design session: document, selection and history.
///
/// It does not depend on the shell's controls, so the designer's logic stays
/// testable without starting the user interface.
/// </summary>
public sealed class DesignerSession
{
    private readonly XamlPreviewRenderer _renderer = new();

    public DesignerSession(XamlDocument document, SourceLanguage language)
    {
        Document = document;
        Language = language;
    }

    public XamlDocument Document { get; }
    public SourceLanguage Language { get; }
    public EditHistory History { get; } = new();

    private readonly List<XElement> _selection = [];

    /// <summary>
    /// The element the property panel and the single-element commands act on.
    /// </summary>
    /// <remarks>
    /// The last one selected, which is the one Visual Studio aligns the others
    /// to. Null when nothing is selected.
    /// </remarks>
    public XElement? Selection => _selection.Count == 0 ? null : _selection[^1];

    /// <summary>Everything currently selected, in the order it was picked.</summary>
    public IReadOnlyList<XElement> SelectedElements => _selection;

    public event EventHandler? SelectionChanged;
    public event EventHandler? DocumentModified;

    public void Select(XElement? element)
    {
        if (_selection.Count == 1 && ReferenceEquals(_selection[0], element)) return;
        if (_selection.Count == 0 && element is null) return;

        _selection.Clear();

        if (element is not null) _selection.Add(element);

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds an element to the selection, or takes it out if it is already in.
    /// </summary>
    /// <remarks>
    /// What Shift-clicking does. Toggling rather than only adding: undoing a
    /// mis-click by clicking again is what everyone expects, and starting over
    /// for one extra control is a real annoyance in a busy layout.
    /// </remarks>
    public void ToggleSelection(XElement element)
    {
        if (!_selection.Remove(element)) _selection.Add(element);

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects several elements at once.</summary>
    public void SelectMany(IEnumerable<XElement> elements)
    {
        _selection.Clear();
        _selection.AddRange(elements);

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets a property on the selected element.</summary>
    public void SetProperty(string name, string? value)
    {
        if (Selection is null) return;

        // Attached properties such as "Grid.Row" remain a single attribute:
        // in XAML they are written with the dot in the name.
        Apply(new SetAttributeEdit(Selection, name, value));
    }

    /// <summary>
    /// Moves the selection by the given amount, as one undoable step.
    /// </summary>
    public void MoveSelection(double dx, double dy)
    {
        if (Selection is null) return;
        if (dx == 0 && dy == 0) return;

        ApplyAsOne("Move", DesignerGeometry.Move(Selection, dx, dy));
    }

    /// <summary>
    /// Resizes the selection, as one undoable step.
    /// </summary>
    /// <param name="dx">How far the origin moves, when a left handle was dragged.</param>
    /// <param name="dy">How far the origin moves, when a top handle was dragged.</param>
    public void ResizeSelection(double width, double height, double dx = 0, double dy = 0)
    {
        if (Selection is null) return;

        ApplyAsOne("Resize", DesignerGeometry.Resize(Selection, width, height, dx, dy));
    }

    /// <summary>
    /// Lines the selected controls up with the last one picked.
    /// </summary>
    /// <param name="measured">
    /// Where each selected control is on screen, in the same order as
    /// <see cref="SelectedElements"/>. Taken from the caller because only the
    /// surface knows what the layout actually did with them: a control with no
    /// Margin still sits somewhere.
    /// </param>
    public void AlignSelection(
        IReadOnlyList<AlignmentArithmetic.Box> measured, AlignmentCommand command)
    {
        if (_selection.Count < 2) return;
        if (measured.Count != _selection.Count) return;

        var adjustments = AlignmentArithmetic.Align(measured, command);
        var edits = new List<Model.IDesignerEdit>();

        for (var i = 0; i < _selection.Count; i++)
        {
            var element = _selection[i];
            var adjustment = adjustments[i];

            if (AlignmentArithmetic.IsResize(command))
            {
                edits.AddRange(DesignerGeometry.Resize(
                    element, adjustment.Width, adjustment.Height));

                continue;
            }

            if (adjustment.Dx == 0 && adjustment.Dy == 0) continue;

            edits.AddRange(DesignerGeometry.Move(element, adjustment.Dx, adjustment.Dy));
        }

        ApplyAsOne(command.ToString(), edits);
    }

    public void SetName(string? name)
    {
        if (Selection is null) return;
        Apply(new SetAttributeEdit(Selection, XamlDocument.XamlNs + "Name", name));
    }

    /// <summary>Inserts a toolbox control into the given container.</summary>
    public XElement InsertFromToolbox(ToolboxItem item, XElement? container = null, int index = -1)
    {
        var parent = container ?? Selection ?? Document.Root;

        // Up the tree until something can take it: a control that hosts
        // nothing, and a Border that already holds something, both send the
        // new element to their parent. Dropping it in anyway produced a file
        // Avalonia refuses to load — "multiple assignments to the content" —
        // and a blank preview with no clue why.
        while (!CanAccept(parent) && parent.Parent is { } above) parent = above;

        // Nowhere above took it, which happens on a Window that already holds
        // its one child: the root's own content is where it belongs, since
        // that is what fills the window.
        if (!CanAccept(parent))
        {
            parent = XamlDocument.ControlChildren(Document.Root).FirstOrDefault()
                     ?? Document.Root;

            while (!CanAccept(parent) &&
                   XamlDocument.ControlChildren(parent).FirstOrDefault() is { } inside)
                parent = inside;
        }

        var edit = new InsertElementEdit(parent, ParseFragment(item.DefaultXaml), index);
        Apply(edit);

        // Select the node actually inserted, which LINQ-to-XML may have cloned
        // from the starting fragment.
        var inserted = edit.Inserted!;
        Select(inserted);
        return inserted;
    }

    /// <summary>
    /// What was cut or copied, waiting to be pasted.
    /// </summary>
    /// <remarks>
    /// The designer's own rather than the system clipboard: pasting XAML into
    /// a text editor is a different feature, and reading arbitrary clipboard
    /// text back as controls means deciding what to do with the half of it
    /// that is not XAML.
    /// </remarks>
    private readonly List<XElement> _clipboard = [];

    /// <summary>Whether there is anything to paste.</summary>
    public bool CanPaste => _clipboard.Count > 0;

    /// <summary>Copies the selection, leaving it in place.</summary>
    public void CopySelection()
    {
        if (_selection.Count == 0) return;

        // Cloned on the way in, so editing the originals afterwards does not
        // change what gets pasted.
        _clipboard.Clear();
        _clipboard.AddRange(_selection.Select(e => new XElement(e)));
    }

    /// <summary>Copies the selection and removes it.</summary>
    public void CutSelection()
    {
        if (_selection.Count == 0) return;

        CopySelection();

        var removable = _selection
            .Where(e => !ReferenceEquals(e, Document.Root))
            .ToList();

        ApplyAsOne("Cut", [.. removable.Select(e => new RemoveElementEdit(e))]);

        _selection.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Pastes what was cut or copied, offset so it does not hide the original.
    /// </summary>
    public void PasteClipboard()
    {
        if (_clipboard.Count == 0) return;

        var parent = Selection?.Parent ?? Document.Root;

        var edits = new List<InsertElementEdit>();

        foreach (var template in _clipboard)
            edits.Add(new InsertElementEdit(parent, new XElement(template)));

        ApplyAsOne("Paste", [.. edits]);

        // Offset after inserting, not before: a detached clone has no parent,
        // so the geometry reads it as living in no container and writes a
        // Margin the Canvas ignores. The copy landed exactly on the original
        // and looked as though nothing had happened.
        var offsets = new List<IDesignerEdit>();

        foreach (var edit in edits)
            if (edit.Inserted is { } inserted)
                offsets.AddRange(
                    DesignerGeometry.Move(inserted, PasteOffset, PasteOffset));

        ApplyAsOne("Offset", offsets);

        // One step, not two: the user did one thing.
        if (offsets.Count > 0) History.FoldLastTwo("Paste");

        // The pasted controls are what the user goes on to move, so they are
        // what ends up selected.
        _selection.Clear();

        foreach (var edit in edits)
            if (edit.Inserted is { } inserted) _selection.Add(inserted);

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>How far a pasted copy sits from its original.</summary>
    private const double PasteOffset = 10;

    public void RemoveSelected()
    {
        if (Selection is null || ReferenceEquals(Selection, Document.Root)) return;

        var toRemove = Selection;
        Select(toRemove.Parent);
        Apply(new RemoveElementEdit(toRemove));
    }

    public void Undo()
    {
        History.Undo();
        DocumentModified?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        History.Redo();
        DocumentModified?.Invoke(this, EventArgs.Empty);
    }

    public PreviewResult Render() => _renderer.Render(Document);

    public string GenerateCodeBehind() => CodeBehindGenerator.Generate(Document, Language);

    private void Apply(IDesignerEdit edit)
    {
        History.Execute(edit);
        DocumentModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies several edits as a single undoable step.
    /// </summary>
    /// <remarks>
    /// A drag writes two attributes and a resize as many as four. Undone one
    /// at a time they leave the control somewhere it never was, which reads as
    /// a broken undo rather than a precise one.
    /// </remarks>
    private void ApplyAsOne(string description, IReadOnlyList<IDesignerEdit> edits)
    {
        if (edits.Count == 0) return;

        History.ExecuteAsOne(description, edits);
        DocumentModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Containers that hold as many children as you like.
    /// </summary>
    /// <remarks>
    /// The element name is used because the document is XML, not a tree of
    /// already instantiated controls.
    /// </remarks>
    private static readonly HashSet<string> ManyChildren =
    [
        "Grid", "StackPanel", "DockPanel", "WrapPanel", "Canvas", "Panel",
        "RelativePanel", "UniformGrid", "TabControl", "Menu", "MenuItem",
    ];

    /// <summary>
    /// Containers that hold exactly one child.
    /// </summary>
    /// <remarks>
    /// Border, Window and the rest set their Content, and a second child
    /// makes Avalonia refuse the whole file: "Unable to find setter that
    /// allows multiple assignments to the content". The preview then goes
    /// blank, which reads as the designer breaking rather than as one control
    /// too many.
    /// </remarks>
    /// <remarks>
    /// Not Button, though it does have a Content: dropping a control inside a
    /// button is almost never what was meant, and treating it as a container
    /// swallowed controls aimed at the panel behind it.
    /// </remarks>
    private static readonly HashSet<string> OneChild =
    [
        "Border", "ScrollViewer", "Window", "UserControl", "TabItem",
        "Expander", "GroupBox", "SplitView", "Viewbox",
    ];

    /// <summary>Whether an element can take another child.</summary>
    private static bool CanAccept(XElement element)
    {
        var name = element.Name.LocalName;

        if (ManyChildren.Contains(name)) return true;

        // One-child containers accept the first and refuse the second: an
        // empty Border is a fine place to drop a control, a full one is not.
        return OneChild.Contains(name) &&
               !XamlDocument.ControlChildren(element).Any();
    }

    /// <summary>Whether an element could ever host children.</summary>
    private static bool CanContainChildren(XElement element) =>
        ManyChildren.Contains(element.Name.LocalName) ||
        OneChild.Contains(element.Name.LocalName);

    /// <summary>
    /// Parses a toolbox fragment in the Avalonia namespace, so that the inserted
    /// element belongs to the same namespace as the document and does not carry
    /// a redundant xmlns declaration with it.
    /// </summary>
    private static XElement ParseFragment(string xaml)
    {
        var wrapper = $"""<Root xmlns="{XamlDocument.AvaloniaNs}" xmlns:x="{XamlDocument.XamlNs}">{xaml}</Root>""";
        return XElement.Parse(wrapper).Elements().First();
    }
}
