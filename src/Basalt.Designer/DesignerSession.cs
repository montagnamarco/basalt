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
    /// Moves the selection to where it was dragged, in its container's terms.
    /// </summary>
    /// <remarks>
    /// A Grid or a DockPanel is told which cell or edge the control now
    /// belongs to; anywhere else this is the old pixel move, which is still
    /// the right answer inside a Canvas.
    ///
    /// The offsets are where the pointer ended up inside the container, which
    /// only the surface can measure.
    /// </remarks>
    public void DragSelectionTo(
        double offsetX, double offsetY,
        double containerWidth, double containerHeight,
        double dx, double dy)
    {
        if (Selection is not { Parent: { } container }) return;

        var kind = DesignerLayout.KindOf(container);

        if (kind is DesignerLayout.LayoutKind.Coordinates or DesignerLayout.LayoutKind.Single)
        {
            MoveSelection(dx, dy);
            return;
        }

        var edits = DesignerLayout.PlaceDrop(
            Selection, container,
            new DesignerLayout.Point(offsetX, offsetY),
            new DesignerLayout.Size(containerWidth, containerHeight));

        // A StackPanel decides the order itself, so a drag inside one has
        // nothing to write: saying so beats writing a Margin that fights it.
        if (edits.Count > 0) ApplyAsOne("Move", edits);
    }

    /// <summary>
    /// Moves the selection into another container.
    /// </summary>
    /// <remarks>
    /// The positioning attributes of the container it leaves are stripped:
    /// a Canvas.Left carried into a Grid is an attribute the layout ignores,
    /// and a Grid.Row carried into a StackPanel is the same. Leaving them
    /// behind made a control that had been moved twice carry the leftovers
    /// of every panel it had ever been in.
    ///
    /// The whole thing is one undo step, because to the user it is one move.
    /// </remarks>
    public void Reparent(
        XElement element, XElement newParent,
        double offsetX, double offsetY,
        double containerWidth, double containerHeight)
    {
        if (ReferenceEquals(element.Parent, newParent)) return;

        // Into itself or into its own child would detach the subtree the
        // element is being put inside, which loses it.
        if (element == newParent || newParent.Ancestors().Contains(element)) return;

        if (!CanAccept(newParent)) return;

        var edits = new List<IDesignerEdit>
        {
            new MoveElementEdit(element, newParent),
        };

        edits.AddRange(DesignerLayout.ClearPositioning(element));

        // Where it lands is decided by the container it is going into, so the
        // attributes are worked out from that one rather than from the panel
        // it is leaving. The edits are built now and applied in order, so the
        // whole move is a single step in the history.
        edits.AddRange(DesignerLayout.PlaceDrop(
            element, newParent,
            new DesignerLayout.Point(offsetX, offsetY),
            new DesignerLayout.Size(containerWidth, containerHeight)));

        ApplyAsOne("Move", edits);
    }

    /// <summary>
    /// Wires the selected control to a method in the code-behind.
    /// </summary>
    /// <remarks>
    /// The gesture Visual Basic was built around: double-click a button and
    /// you are in the code that runs when it is pressed. Three things have to
    /// happen together — the control gets a name if it has none, the XAML
    /// gets the attribute, and the code-behind gets the method — and all
    /// three are one action to the user, so one step in the history.
    ///
    /// Returns where the handler is, so the caller can open the file there.
    /// Null when there is nothing selected or no code-behind to write to.
    /// </remarks>
    public HandlerLocation? AttachHandler(string? eventName = null)
    {
        if (Selection is not { } element) return null;
        if (Document.FilePath is not { } xamlPath) return null;

        var chosen = eventName ?? EventHandlers.DefaultEventFor(element.Name.LocalName);

        var edits = new List<IDesignerEdit>();

        // A handler refers to its control by name, so an unnamed one cannot
        // have a handler at all: it is given one rather than refused.
        var name = XamlDocument.GetName(element);

        if (name is not { Length: > 0 })
        {
            name = EventHandlers.SuggestName(element, Document.Root);

            edits.Add(new SetAttributeEdit(element, XamlDocument.XamlNs + "Name", name));
        }

        var method = EventHandlers.NameFor(name, chosen);

        // Already wired to something else: that is the user's choice, and
        // overwriting it would lose whatever they pointed it at.
        var existing = element.Attribute(chosen)?.Value;

        if (existing is { Length: > 0 }) method = existing;
        else edits.Add(new SetAttributeEdit(element, chosen, method));

        if (edits.Count > 0) ApplyAsOne("Handler", edits);

        var codePath = CodeBehindGenerator.CodeBehindPath(xamlPath, Language);

        var code = File.Exists(codePath)
            ? File.ReadAllText(codePath)
            : CodeBehindGenerator.Generate(Document, Language);

        var had = EventHandlers.Contains(code, method, Language);

        if (!had) code = EventHandlers.AddHandler(code, method, chosen, Language);

        return new HandlerLocation(codePath, method, code, !had);
    }

    /// <summary>
    /// Draws the selection over, or under, the controls beside it.
    /// </summary>
    /// <remarks>
    /// Among siblings a control is drawn in the order it is written, so this
    /// is a move within the parent rather than a ZIndex: writing one would
    /// leave the file saying two different things about the same order.
    /// </remarks>
    public void BringToFront(bool toFront = true)
    {
        if (Selection is not { Parent: { } parent } selection) return;

        var siblings = XamlDocument.ControlChildren(parent).ToList();

        if (siblings.Count < 2) return;

        // Already there, so nothing to record: an undo step that changes
        // nothing is one the user has to press twice.
        var at = siblings.IndexOf(selection);

        if (toFront && at == siblings.Count - 1) return;
        if (!toFront && at == 0) return;

        Apply(new MoveElementEdit(selection, parent, toFront ? -1 : 0));
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
    /// <summary>
    /// Places a just-dropped control the way its container lays out.
    /// </summary>
    /// <remarks>
    /// Separate from the insert because only the surface knows where the
    /// pointer was and how big the container is on screen; the session knows
    /// what the XAML should say about it.
    /// </remarks>
    public void PlaceDrop(
        XElement inserted,
        double offsetX, double offsetY,
        double containerWidth, double containerHeight)
    {
        if (inserted.Parent is not { } container) return;

        var edits = DesignerLayout.PlaceDrop(
            inserted, container,
            new DesignerLayout.Point(offsetX, offsetY),
            new DesignerLayout.Size(containerWidth, containerHeight));

        foreach (var edit in edits) Apply(edit);
    }

    /// <summary>
    /// The grid whose rows and columns the editor should show.
    /// </summary>
    /// <remarks>
    /// The selection when it is a Grid, otherwise the Grid it sits in: the
    /// rows are wanted just as often while looking at a button inside them
    /// as while looking at the grid itself.
    /// </remarks>
    public XElement? GridInScope =>
        Selection is { } selection
            ? selection.Name.LocalName == "Grid"
                ? selection
                : selection.Parent?.Name.LocalName == "Grid" ? selection.Parent : null
            : null;

    /// <summary>The rows or columns of the grid in scope, as written.</summary>
    public IReadOnlyList<string> TracksOf(DesignerLayout.Track track) =>
        GridInScope is { } grid ? DesignerLayout.TracksOf(grid, track) : [];

    /// <summary>Rewrites the rows or columns of the grid in scope.</summary>
    public void SetTracks(DesignerLayout.Track track, IReadOnlyList<string> sizes)
    {
        if (GridInScope is not { } grid) return;

        ApplyAsOne(
            track == DesignerLayout.Track.Row ? "Rows" : "Columns",
            DesignerLayout.SetTracks(grid, track, sizes));
    }

    /// <summary>Adds a row or column to the grid in scope.</summary>
    public void AddTrack(DesignerLayout.Track track, string size = "*")
    {
        if (GridInScope is not { } grid) return;

        // A grid with nothing declared already has one implicit track, so the
        // first addition has to write both of them or the new one would be
        // the only one and everything already inside would move into it.
        var existing = DesignerLayout.TracksOf(grid, track);
        var sizes = existing.Count == 0 ? ["*", size] : (List<string>)[.. existing, size];

        SetTracks(track, sizes);
    }

    /// <summary>
    /// Removes a row or column from the grid in scope.
    /// </summary>
    /// <remarks>
    /// The controls that were in it come back to the one before, and those
    /// below move up: leaving them pointing past the end puts them all in the
    /// last track, which looks like the designer scrambled the form.
    /// </remarks>
    public void RemoveTrack(DesignerLayout.Track track, int index)
    {
        if (GridInScope is not { } grid) return;

        var existing = DesignerLayout.TracksOf(grid, track);

        if (index < 0 || index >= existing.Count) return;

        var remaining = existing.ToList();
        remaining.RemoveAt(index);

        var edits = new List<IDesignerEdit>(DesignerLayout.SetTracks(grid, track, remaining));
        edits.AddRange(DesignerLayout.AfterRemoving(grid, track, index));

        ApplyAsOne(track == DesignerLayout.Track.Row ? "Remove row" : "Remove column", edits);
    }

    /// <summary>How the container of the selection places what is inside it.</summary>
    public DesignerLayout.LayoutKind SelectionLayout =>
        DesignerLayout.KindOf(Selection?.Parent);

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
    /// <summary>Whether a control can hold another inside it.</summary>
    /// <remarks>
    /// Asked by the tree, which has to decide whether a row dropped on
    /// another goes *into* it or *beside* it.
    /// </remarks>
    public static bool CanHoldChildren(XElement element) => CanAccept(element);

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
