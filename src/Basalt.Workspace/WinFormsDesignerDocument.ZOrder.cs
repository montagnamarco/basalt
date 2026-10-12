using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed partial class WinFormsDesignerDocument
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ZOrder { get; }
    private readonly HashSet<string> _unsupportedZOrder = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyDictionary<string, IReadOnlyList<string>> ReadZOrder()
    {
        var orders = Controls.GroupBy(OrderKeyOf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var statement in _initialize.Statements)
        {
            foreach (var invocation in statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax collection,
                    } call || !collection.Name.Identifier.ValueText.Equals("Controls", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var parent = OrderKeyOfCollection(collection.Expression);
                if (parent is null || !orders.TryGetValue(parent, out var order))
                {
                    continue;
                }
                var arguments = invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray() ?? [];
                var method = call.Name.Identifier.ValueText;
                if (method.Equals("Add", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 1)
                {
                    AddChild(arguments[0].Expression, parent, order);
                }
                else if (method.Equals("AddRange", StringComparison.OrdinalIgnoreCase) && arguments.Length == 1
                    && arguments[0].Expression is ArrayCreationExpressionSyntax { Initializer: { } initializer })
                {
                    foreach (var expression in initializer.Initializers)
                    {
                        AddChild(expression, parent, order);
                    }
                }
                else if (method.Equals("SetChildIndex", StringComparison.OrdinalIgnoreCase))
                {
                    var child = arguments.Length == 2 ? ControlName(arguments[0].Expression) : null;
                    if (child is null || !order.Contains(child, StringComparer.OrdinalIgnoreCase)
                        || !TryReadChildIndex(arguments[1].Expression, out var index))
                    {
                        _unsupportedZOrder.Add(parent);
                        continue;
                    }
                    var existing = order.FindIndex(name => name.Equals(child, StringComparison.OrdinalIgnoreCase));
                    var canonical = order[existing];
                    order.RemoveAt(existing);
                    order.Insert(index == -1 ? order.Count : Math.Min(index, order.Count), canonical);
                }
            }
        }
        foreach (var control in Controls)
        {
            var order = orders[OrderKeyOf(control)];
            if (!order.Contains(control.Name, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(control.Name);
            }
        }
        return orders.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryReadChildIndex(ExpressionSyntax expression, out int index)
    {
        if (expression is LiteralExpressionSyntax { Token.Value: int value } && value >= 0)
        {
            index = value;
            return true;
        }
        if (expression is UnaryExpressionSyntax
            {
                OperatorToken.Text: "-",
                Operand: LiteralExpressionSyntax { Token.Value: 1 },
            })
        {
            index = -1;
            return true;
        }
        index = default;
        return false;
    }

    private void AddChild(ExpressionSyntax expression, string parent, List<string> order)
    {
        var name = ControlName(expression);
        if (name is not null && Controls.Any(control => control.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && OrderKeyOf(control).Equals(parent, StringComparison.OrdinalIgnoreCase))
            && !order.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            order.Add(name);
        }
    }

    /// <summary>
    /// The key of a control's z-order: its owner, and the owner's panel when the control sits in one, as "SplitContainer1.Panel1".
    /// Each panel orders its own children, because a SetChildIndex written on the container would not find a panel's child.
    /// </summary>
    internal static string OrderKeyOf(WinFormsDesignerControl control) =>
        control.ParentPanel is null ? control.ParentName : control.ParentName + "." + control.ParentPanel;

    /// <summary>The key of a Controls collection's owner, read from the collection's receiver: "Me", a control, or a control's panel.</summary>
    private string? OrderKeyOfCollection(ExpressionSyntax expression)
    {
        if (ControlName(expression) is { } name) return name;
        return expression is MemberAccessExpressionSyntax panel && ControlName(panel.Expression) is { } owner
            ? owner + "." + panel.Name.Identifier.ValueText : null;
    }

    public string SetZOrder(IReadOnlyList<string> names, bool bringToFront)
    {
        if (names.Count == 0)
        {
            return Source;
        }
        var controls = names.Select(FindControl).ToArray();
        var parent = OrderKeyOf(controls[0]);
        if (controls.Any(control => control.Name == "Me" || !OrderKeyOf(control).Equals(parent, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerZOrderMustSharedParent), nameof(names));
        }
        if (_unsupportedZOrder.Contains(parent))
        {
            throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerZOrderUnsupported));
        }
        var current = ZOrder[parent];
        var selected = current.Where(name => names.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
        var remaining = current.Except(selected, StringComparer.OrdinalIgnoreCase).ToArray();
        var desired = bringToFront ? selected.Concat(remaining).ToArray() : remaining.Concat(selected).ToArray();
        if (current.SequenceEqual(desired, StringComparer.OrdinalIgnoreCase))
        {
            return Source;
        }
        // A key names a panel as Owner.Panel, so the receiver is Me.Owner.Panel: the way Visual Studio writes a panel's children.
        var target = parent.Equals("Me", StringComparison.OrdinalIgnoreCase) ? "Me" : "Me." + parent;
        return InsertStatements(desired.Select((name, index) =>
            $"{target}.Controls.SetChildIndex(Me.{name}, {index})").ToArray());
    }
}
