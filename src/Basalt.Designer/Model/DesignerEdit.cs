using System.Xml.Linq;

namespace Basalt.Designer.Model;

/// <summary>A reversible edit applied to the XAML document.</summary>
public interface IDesignerEdit
{
    string Description { get; }
    void Apply();
    void Undo();
}

/// <summary>Sets, changes or removes an attribute of an element.</summary>
public sealed class SetAttributeEdit : IDesignerEdit
{
    private readonly XElement _element;
    private readonly XName _attribute;
    private readonly string? _newValue;
    private readonly string? _oldValue;

    public SetAttributeEdit(XElement element, XName attribute, string? newValue)
    {
        _element = element;
        _attribute = attribute;
        _newValue = newValue;
        _oldValue = element.Attribute(attribute)?.Value;
    }

    public string Description => $"Set {_attribute.LocalName}";

    public void Apply() => Write(_newValue);
    public void Undo() => Write(_oldValue);

    /// <summary>A null or empty value removes the attribute instead of blanking it.</summary>
    private void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
            _element.Attribute(_attribute)?.Remove();
        else
            _element.SetAttributeValue(_attribute, value);
    }
}

/// <summary>Inserts a new control into a container.</summary>
public sealed class InsertElementEdit : IDesignerEdit
{
    private readonly XElement _parent;
    private readonly XElement _template;
    private readonly int _index;

    public InsertElementEdit(XElement parent, XElement element, int index = -1)
    {
        _parent = parent;
        _template = element;
        _index = index;
    }

    /// <summary>
    /// The element actually present in the tree.
    ///
    /// LINQ-to-XML clones an XElement that already belongs to a document instead
    /// of re-parenting it, so the inserted instance may differ from the one
    /// passed to the constructor. Undoing without accounting for that would remove
    /// a detached node and leave the copy in the document.
    /// </summary>
    public XElement? Inserted { get; private set; }

    public string Description => $"Insert {_template.Name.LocalName}";

    public void Apply()
    {
        var siblings = XamlDocument.ControlChildren(_parent).ToList();

        if (_index < 0 || _index >= siblings.Count)
        {
            XamlFormatting.AddIndented(_parent, _template);
            Inserted = (XElement)_parent.Elements().Last();
        }
        else
        {
            siblings[_index].AddBeforeSelf(_template);
            Inserted = (XElement)siblings[_index].PreviousNode!;

            XamlFormatting.IndentBefore(Inserted, siblings[_index]);
        }
    }

    public void Undo()
    {
        // The whitespace put in with it goes too, or every insert and undo
        // leaves a blank line behind and the file slowly grows them.
        XamlFormatting.RemoveWithWhitespace(Inserted);
    }
}

/// <summary>Removes a control, remembering where it was.</summary>
public sealed class RemoveElementEdit : IDesignerEdit
{
    private readonly XElement _element;
    private XElement? _parent;
    private XNode? _previousSibling;

    public RemoveElementEdit(XElement element) => _element = element;

    public string Description => $"Delete {_element.Name.LocalName}";

    public void Apply()
    {
        _parent = _element.Parent;

        // The element the undo will put it back after, skipping the
        // whitespace that is about to go with it.
        _previousSibling = _element.PreviousNode is XText space && IsLayout(space)
            ? space.PreviousNode
            : _element.PreviousNode;

        XamlFormatting.RemoveWithWhitespace(_element);

        // A container left with nothing but blank lines in it reads as
        // broken. Emptied outright, so it closes on its own tag again.
        if (_parent is not null) XamlFormatting.TidyIfEmpty(_parent);
    }

    public void Undo()
    {
        if (_previousSibling is not null)
            _previousSibling.AddAfterSelf(_element);
        else
            _parent?.AddFirst(_element);

        if (_parent is not null) XamlFormatting.Reindent(_parent);
    }

    private static bool IsLayout(XText text) =>
        text.Value.Length > 0 && text.Value.All(char.IsWhiteSpace);
}

/// <summary>
/// Moves a control into another container, or to another place among its
/// siblings.
/// </summary>
/// <remarks>
/// Without this a control could only ever be repositioned inside the panel it
/// was first dropped in: rearranging a form meant deleting a control and
/// adding it again somewhere else, losing everything set on it.
///
/// The element is detached before being added, which is what keeps the same
/// instance in the tree — LINQ-to-XML copies an element that still belongs to
/// a document rather than moving it, and the caller's references, the
/// selection among them, would then point at a node no longer in the file.
/// </remarks>
public sealed class MoveElementEdit : IDesignerEdit
{
    private readonly XElement _element;
    private readonly XElement _newParent;
    private readonly int _index;

    private XElement? _oldParent;
    private XNode? _wasAfter;

    public MoveElementEdit(XElement element, XElement newParent, int index = -1)
    {
        _element = element;
        _newParent = newParent;
        _index = index;
    }

    public string Description => $"Move {_element.Name.LocalName}";

    public void Apply()
    {
        _oldParent = _element.Parent;

        // Past the whitespace that is about to go with it, or the undo would
        // put the element back after a node no longer in the tree.
        _wasAfter = _element.PreviousNode is XText space
                    && space.Value.All(char.IsWhiteSpace)
            ? space.PreviousNode
            : _element.PreviousNode;

        XamlFormatting.RemoveWithWhitespace(_element);

        var siblings = XamlDocument.ControlChildren(_newParent).ToList();

        if (_index < 0 || _index >= siblings.Count)
        {
            XamlFormatting.AddIndented(_newParent, _element);
        }
        else
        {
            siblings[_index].AddBeforeSelf(_element);
            XamlFormatting.IndentBefore(_element, siblings[_index]);
        }

        // The panel it came from may now be empty, or left with a gap where
        // it used to sit.
        if (_oldParent is not null && !ReferenceEquals(_oldParent, _newParent))
            XamlFormatting.TidyIfEmpty(_oldParent);
    }

    public void Undo()
    {
        XamlFormatting.RemoveWithWhitespace(_element);

        if (_wasAfter is not null) _wasAfter.AddAfterSelf(_element);
        else _oldParent?.AddFirst(_element);

        // Both ends: the one it went back to, and the one it came out of.
        if (_oldParent is not null) XamlFormatting.Reindent(_oldParent);

        XamlFormatting.TidyIfEmpty(_newParent);
    }
}

/// <summary>History of the designer's edits.</summary>
public sealed class EditHistory
{
    private readonly Stack<IDesignerEdit> _undo = new();
    private readonly Stack<IDesignerEdit> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;

    public event EventHandler? Changed;

    /// <summary>
    /// Several edits undone and redone as one.
    /// </summary>
    /// <remarks>
    /// Dragging a control writes two attributes, and resizing writes as many
    /// as four. Without this each one is its own step, so undoing a single
    /// drag takes several presses and leaves the control somewhere it never
    /// was — which reads as a broken undo rather than as a fine-grained one.
    /// </remarks>
    private sealed class CompositeEdit : IDesignerEdit
    {
        private readonly IReadOnlyList<IDesignerEdit> _edits;

        public CompositeEdit(string description, IReadOnlyList<IDesignerEdit> edits)
        {
            Description = description;
            _edits = edits;
        }

        public string Description { get; }

        public void Apply()
        {
            foreach (var edit in _edits) edit.Apply();
        }

        public void Undo()
        {
            // Backwards: a later edit may depend on an earlier one, and
            // undoing them in order would unpick the wrong one first.
            for (var i = _edits.Count - 1; i >= 0; i--) _edits[i].Undo();
        }
    }

    /// <summary>
    /// Folds the last edit into the one before it, as a single step.
    /// </summary>
    /// <remarks>
    /// For an action whose second half can only be worked out once the first
    /// has run — pasting, where a clone has no parent until it is in the tree
    /// and so cannot be positioned before then.
    /// </remarks>
    public void FoldLastTwo(string description)
    {
        if (_undo.Count < 2) return;

        var second = _undo.Pop();
        var first = _undo.Pop();

        _undo.Push(new CompositeEdit(description, [first, second]));
    }

    /// <summary>
    /// Runs several edits as a single undoable step.
    /// </summary>
    public void ExecuteAsOne(string description, IReadOnlyList<IDesignerEdit> edits)
    {
        if (edits.Count == 0) return;

        // One edit needs no wrapper, and wrapping it would put the group's
        // description in the undo menu instead of its own.
        Execute(edits.Count == 1 ? edits[0] : new CompositeEdit(description, edits));
    }

    public void Execute(IDesignerEdit edit)
    {
        edit.Apply();
        _undo.Push(edit);
        // A new edit invalidates the redo branch.
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var edit = _undo.Pop();
        edit.Undo();
        _redo.Push(edit);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var edit = _redo.Pop();
        edit.Apply();
        _undo.Push(edit);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
