using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Shell.Controls;

/// <summary>
/// The controls of the form being designed, as a tree.
/// </summary>
/// <remarks>
/// The surface can only reach what can be clicked, and a container is covered
/// by the things inside it: a Grid filled by a Button had no way of being
/// selected at all. The tree shows the shape of the form and reaches every
/// part of it, which is also how you see at a glance that a control ended up
/// in the wrong panel.
/// </remarks>
public sealed class ElementTreePanel : UserControl
{
    private readonly TreeView _tree;

    private bool _echoing;

    /// <summary>Raised when a control is picked, so the surface can follow.</summary>
    public event EventHandler<XElement>? ElementSelected;

    public ElementTreePanel()
    {
        _tree = new TreeView
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
        };

        // A drag begins once the pointer has travelled: pressing alone is how
        // a row is selected, and starting a drag on the press would make
        // clicking a control feel like it moved.
        _tree.PointerMoved += (_, e) =>
        {
            if (_pressed is not { } node || _dragging is not null) return;

            if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
            {
                _pressed = null;
                return;
            }

            var moved = e.GetPosition(this) - _pressedAt;

            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;

            if (_pressEvent is not { } press) return;

            _dragging = node;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DataFormat.Text, node.Label));

            _ = DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        };

        _tree.PointerReleased += (_, _) =>
        {
            _pressed = null;
            _pressEvent = null;
            _dragging = null;
        };

        _tree.SelectionChanged += (_, _) =>
        {
            // Not while the panel is following the surface: showing what was
            // selected elsewhere would otherwise be reported back as a fresh
            // choice and the two would answer each other in a loop.
            if (_echoing) return;

            if (_tree.SelectedItem is Node node) ElementSelected?.Invoke(this, node.Element);
        };

        Content = _tree;
    }

    /// <summary>One control in the tree.</summary>
    /// <remarks>
    /// A class rather than the XElement itself: the tree needs a label and
    /// its children, and building them once beats asking on every redraw.
    /// </remarks>
    internal sealed class Node
    {
        public Node(XElement element)
        {
            Element = element;

            Children =
            [
                .. XamlDocument.ControlChildren(element).Select(child => new Node(child))
            ];
        }

        public XElement Element { get; }

        public IReadOnlyList<Node> Children { get; }

        /// <summary>
        /// The type, and the name where the control has one.
        /// </summary>
        /// <remarks>
        /// Name first when there is one: in a form of nine buttons the type
        /// tells you nothing about which is which, and the name is the thing
        /// the code behind refers to.
        /// </remarks>
        public string Label
        {
            get
            {
                var type = Element.Name.LocalName;

                return XamlDocument.GetName(Element) is { Length: > 0 } name
                    ? $"{name}  ({type})"
                    : type;
            }
        }
    }

    /// <summary>The labels as shown, outermost first. For tests.</summary>
    internal IReadOnlyList<string> Labels
    {
        get
        {
            var labels = new List<string>();

            void Walk(IEnumerable<Node> nodes)
            {
                foreach (var node in nodes)
                {
                    labels.Add(node.Label);
                    Walk(node.Children);
                }
            }

            Walk(_roots);

            return labels;
        }
    }

    private IReadOnlyList<Node> _roots = [];

    /// <summary>Shows the controls of the given session.</summary>
    public void Show(DesignerSession? session)
    {
        if (session is null)
        {
            _roots = [];
            _tree.ItemsSource = null;
            return;
        }

        _roots =
        [
            .. XamlDocument
                .ControlChildren(session.Document.Root)
                .Select(child => new Node(child))
        ];

        _tree.ItemsSource = _roots;

        _tree.ItemTemplate = new FuncTreeDataTemplate<Node>(
            (node, _) => Row(node),
            node => node?.Children ?? []);

        // Everything open: a form has a handful of controls, and a tree that
        // has to be unfolded before it shows anything is one more step
        // between wanting a control and reaching it.
        ExpandAll();

        Follow(session.Selection);
    }

    /// <summary>
    /// One row: the control's own icon, then what it is called.
    /// </summary>
    /// <remarks>
    /// The same icons the toolbox uses, so a control is recognised by the
    /// same shape wherever it appears. A tree of identical rows has to be
    /// read word by word; with the shapes the eye finds the button.
    /// </remarks>
    private Control Row(Node? node)
    {
        var row = new Border
        {
            Margin = new Thickness(0, Spacing.Hairline),
            Background = Brushes.Transparent,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Spacing.Tight,
                Children =
                {
                    new ToolboxIconView
                    {
                        ElementName = node?.Element.Name.LocalName,
                        IconSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = node?.Label ?? "",
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        if (node is not null) AllowDragging(row, node);

        return row;
    }

    /// <summary>
    /// Lets a row be dragged onto another, to move the control.
    /// </summary>
    /// <remarks>
    /// The tree is where the shape of the form is visible, so it is the
    /// natural place to change it: dragging on the surface can only reach
    /// what is not covered, and a control cannot be dropped into a panel
    /// that has no room to show.
    /// </remarks>
    private void AllowDragging(Control row, Node node)
    {
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;

            _pressed = node;
            _pressedAt = e.GetPosition(this);
            _pressEvent = e;
        };

        DragDrop.SetAllowDrop(row, true);

        row.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var allowed = CanDropOn(node);

            e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;

            // Shown on the row under the pointer, so the drop is not a guess.
            // A container is filled, since the control goes inside it; a plain
            // control gets a line, since the control goes beside it. Nothing
            // at all where the drop would be refused.
            ShowDropOn(allowed ? node : null, row);

            e.Handled = true;
        });

        row.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ShowDropOn(null, null));

        row.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_dragging is { } moving && CanDropOn(node))
                ElementMoved?.Invoke(this, (moving.Element, node.Element));

            ShowDropOn(null, null);

            _dragging = null;
            e.Handled = true;
        });
    }

    /// <summary>
    /// Marks the row a drop would land on.
    /// </summary>
    /// <remarks>
    /// Two different marks for two different outcomes: a container is filled
    /// because the control goes *inside* it, and a plain control gets a line
    /// underneath because the control goes *beside* it. Drawing the same
    /// thing for both would leave the more important of the two questions
    /// unanswered.
    /// </remarks>
    private void ShowDropOn(Node? node, Control? row)
    {
        if (_marked is { } previous)
        {
            previous.Background = Brushes.Transparent;
            previous.BorderThickness = default;
        }

        _marked = null;

        if (node is null || row is not Border marker) return;

        var into = DesignerSession.CanHoldChildren(node.Element);

        marker.Background = into
            ? new SolidColorBrush(Color.FromArgb(50, 30, 144, 255))
            : Brushes.Transparent;

        marker.BorderBrush = Brushes.DodgerBlue;
        marker.BorderThickness = into
            ? new Thickness(1)
            : new Thickness(0, 0, 0, 2);

        _marked = marker;
    }

    /// <summary>The row currently marked as the destination.</summary>
    private Border? _marked;

    /// <summary>What the drop feedback is showing. For tests.</summary>
    internal (string? Label, bool Inside) MarkedForTests =>
        _marked is null
            ? (null, false)
            : ((_marked.Child as Panel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text,
               _marked.BorderThickness.Top > 0);

    /// <summary>Shows the destination, as dragging over a row does. For tests.</summary>
    internal void ShowDropOnForTests(XElement element)
    {
        if (FindNode(_roots, element) is not { } node) return;

        ShowDropOn(node, Row(node));
    }

    /// <summary>Whether what is being dragged can go onto this row.</summary>
    /// <remarks>
    /// Onto itself or into its own subtree would detach the very branch the
    /// control is being put inside, which loses it.
    /// </remarks>
    private bool CanDropOn(Node target) =>
        _dragging is { } moving
        && !ReferenceEquals(moving.Element, target.Element)
        && !target.Element.Ancestors().Contains(moving.Element);

    /// <summary>How far the pointer travels before a press becomes a drag.</summary>
    private const double DragThreshold = 4;

    private Node? _pressed;
    private Node? _dragging;
    private Point _pressedAt;

    /// <summary>
    /// The press the drag grows out of.
    /// </summary>
    /// <remarks>
    /// Avalonia starts a drag from the press, not from the move that decided
    /// it was one: the pointer has to have been captured by the same gesture.
    /// </remarks>
    private PointerPressedEventArgs? _pressEvent;

    /// <summary>Raised when a control is dragged onto another. (Moved, Onto)</summary>
    public event EventHandler<(XElement Moved, XElement Onto)>? ElementMoved;

    /// <summary>Shows what the surface has selected, without reporting it back.</summary>
    public void Follow(XElement? element)
    {
        if (element is null)
        {
            _echoing = true;
            _tree.SelectedItem = null;
            _echoing = false;
            return;
        }

        if (FindNode(_roots, element) is not { } node) return;

        _echoing = true;
        _tree.SelectedItem = node;
        _echoing = false;
    }

    private static Node? FindNode(IEnumerable<Node> nodes, XElement element)
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node.Element, element)) return node;

            if (FindNode(node.Children, element) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// Opens every branch.
    /// </summary>
    /// <remarks>
    /// A form has a handful of controls, and a tree that must be unfolded
    /// before it shows anything is one more step between wanting a control
    /// and reaching it.
    /// </remarks>
    private void ExpandAll()
    {
        foreach (var root in _roots)
        {
            // Null before the tree has built its containers, which is the
            // case on the very first show; the next one catches it.
            if (_tree.TreeContainerFromItem(root) is TreeViewItem item)
                _tree.ExpandSubTree(item);
        }
    }

    /// <summary>Picks a control, as clicking its row does. For tests.</summary>
    internal void SelectForTests(XElement element)
    {
        if (FindNode(_roots, element) is not { } node) return;

        _tree.SelectedItem = node;

        ElementSelected?.Invoke(this, node.Element);
    }

    /// <summary>What the tree has selected. For tests.</summary>
    internal XElement? SelectedElement => (_tree.SelectedItem as Node)?.Element;
}
