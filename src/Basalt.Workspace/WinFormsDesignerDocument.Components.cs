using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed partial class WinFormsDesignerDocument
{
    private IReadOnlyList<WinFormsDesignerComponent>? _components;
    private IReadOnlySet<string>? _declaredNames;

    /// <summary>All declared field identifiers, including unsupported objects left untouched by the designer.</summary>
    public IReadOnlySet<string> DeclaredNames => _declaredNames ??= _class.Members.OfType<FieldDeclarationSyntax>()
        .SelectMany(declaration => declaration.Declarators).SelectMany(declarator => declarator.Names)
        .Select(identifier => identifier.Identifier.ValueText).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Designer fields that live in the component tray rather than the form's control tree.</summary>
    public IReadOnlyList<WinFormsDesignerComponent> Components => _components ??= ReadComponents();

    public string AddComponent(string name, string typeName)
    {
        ValidateIdentifier(name);
        if (!IsTrayTypeName(typeName))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerComponentTypeUnsupported), nameof(typeName));
        if (_class.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declarators)
            .SelectMany(declarator => declarator.Names)
            .Any(field => field.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerFieldNameAlreadyExists), nameof(name));

        var parsedType = SyntaxFactory.ParseTypeName(typeName);
        if (parsedType.ContainsDiagnostics) throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerComponentTypeInvalid));
        var constructor = IsSerialPortTypeName(typeName) && HasInitializedComponentContainer()
            ? $"New {typeName}(Me.components)"
            : $"New {typeName}()";
        var source = InsertStatements($"Me.{name} = {constructor}");
        var updated = new WinFormsDesignerDocument(source);
        var position = updated._class.EndClassStatement.FullSpan.Start;
        return updated._text.WithChanges(new TextChange(new TextSpan(position, 0),
            $"    Friend WithEvents {name} As {typeName}{NewLine}")).ToString();
    }

    public string SetComponentProperty(string componentName, string propertyName, string? expression)
    {
        ValidateIdentifier(propertyName);
        FindComponent(componentName);
        var matches = _initialize.Statements.OfType<AssignmentStatementSyntax>()
            .Where(statement => statement.Left is MemberAccessExpressionSyntax member
                && (ControlName(member.Expression) ?? "Me").Equals(componentName, StringComparison.OrdinalIgnoreCase)
                && member.Name.Identifier.ValueText.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (expression is null)
            return _text.WithChanges(matches.Select(statement => new TextChange(statement.FullSpan, ""))).ToString();
        if (SyntaxFactory.ParseExpression(expression).ContainsDiagnostics)
            throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerInvalidExpression));
        if (matches.Length != 0)
            return _text.WithChanges(matches.Select(statement => new TextChange(statement.Right.Span, expression))).ToString();
        return InsertStatements($"Me.{componentName}.{propertyName} = {expression}");
    }

    public string RemoveComponent(string name)
    {
        FindComponent(name);
        var changes = new List<TextChange>();
        foreach (var statement in _initialize.Statements)
        {
            if (statement.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
                .Any(member => member.Expression is MeExpressionSyntax
                    && member.Name.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase)))
                changes.Add(new TextChange(statement.FullSpan, ""));
        }

        foreach (var field in _class.Members.OfType<FieldDeclarationSyntax>())
        {
            var names = field.Declarators.SelectMany(declarator => declarator.Names).ToArray();
            if (!names.Any(identifier => identifier.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (names.Length != 1)
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerSplitSharedFieldDeleteComponent));
            changes.Add(new TextChange(field.FullSpan, ""));
        }

        foreach (var declaration in _class.DescendantNodes().OfType<MethodStatementSyntax>())
        {
            if (declaration.HandlesClause is not { } handles) continue;
            var remaining = handles.Events.Where(item =>
                !item.EventContainer.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (remaining.Length == handles.Events.Count) continue;
            changes.Add(new TextChange(handles.Span, remaining.Length == 0 ? ""
                : handles.WithEvents(SyntaxFactory.SeparatedList(remaining)).ToString()));
        }

        return _text.WithChanges(changes).ToString();
    }

    private WinFormsDesignerComponent FindComponent(string name) =>
        Components.FirstOrDefault(component => component.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerComponentNotFound), nameof(name));

    private IReadOnlyList<WinFormsDesignerComponent> ReadComponents()
    {
        var fields = DeclaredNames;
        var components = new Dictionary<string, WinFormsDesignerComponent>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in _initialize.Statements.OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Right is not ObjectCreationExpressionSyntax creation
                || !IsTrayTypeName(creation.Type.ToString())
                || ControlName(assignment.Left) is not { } name || !fields.Contains(name))
                continue;
            components[name] = new WinFormsDesignerComponent(name, creation.Type.ToString());
        }

        foreach (var assignment in _initialize.Statements.OfType<AssignmentStatementSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax member
                || ControlName(member.Expression) is not { } name
                || !components.TryGetValue(name, out var component)) continue;
            component.Properties[member.Name.Identifier.ValueText] = assignment.Right.ToString();
            component.PropertySyntax[member.Name.Identifier.ValueText] = assignment.Right;
        }

        return components.Values.ToArray();
    }

    internal static bool IsTrayTypeName(string typeName)
    {
        var name = typeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
        return IsSerialPortTypeName(name)
            || name.Equals("System.Windows.Forms.Timer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System.Windows.Forms.ImageList", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System.Windows.Forms.ToolTip", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System.Windows.Forms.ContextMenuStrip", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Timer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ImageList", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ToolTip", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ContextMenuStrip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSerialPortTypeName(string typeName)
    {
        var name = typeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
        return name.Equals("System.IO.Ports.SerialPort", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SerialPort", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasInitializedComponentContainer()
    {
        var containerField = _class.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declarators)
            .Any(declarator => declarator.Names.Any(name =>
                    name.Identifier.ValueText.Equals("components", StringComparison.OrdinalIgnoreCase))
                && declarator.AsClause is SimpleAsClauseSyntax clause
                && IsContainerInterfaceTypeName(clause.Type.ToString()));
        return containerField && _initialize.Statements.OfType<AssignmentStatementSyntax>()
            .Any(assignment => ControlName(assignment.Left)?.Equals("components", StringComparison.OrdinalIgnoreCase) == true
                && assignment.Right is ObjectCreationExpressionSyntax creation
                && IsDesignerContainerTypeName(creation.Type.ToString()));
    }

    private static bool IsContainerInterfaceTypeName(string typeName)
    {
        var name = typeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
        return name.Equals("System.ComponentModel.IContainer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("IContainer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDesignerContainerTypeName(string typeName)
    {
        var name = typeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
        return name.Equals("System.ComponentModel.Container", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Container", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record WinFormsDesignerComponent(string Name, string TypeName)
{
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ExpressionSyntax> PropertySyntax { get; } = new(StringComparer.OrdinalIgnoreCase);
}
