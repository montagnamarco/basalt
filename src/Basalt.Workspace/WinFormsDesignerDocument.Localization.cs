using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>
/// A property value that leaves the neutral .resx when a form becomes Localizable = False and
/// returns to InitializeComponent as "Me.Control.Property = Expression".
/// </summary>
public sealed record WinFormsLocalizedAssignment(string ControlName, string PropertyName, string Expression);

/// <summary>
/// The Visual Studio layout of a localizable form. With Localizable = True, InitializeComponent
/// declares a ComponentResourceManager, calls <c>resources.ApplyResources(control, "name")</c> for
/// each control and for the form itself (<c>"$this"</c>), and the moved values live in the .resx.
/// </summary>
public sealed partial class WinFormsDesignerDocument
{
    /// <summary>The resource key prefix Visual Studio gives the form itself: "$this.Text".</summary>
    public const string FormResourcePrefix = "$this";

    /// <summary>
    /// True when the form is resolved through <c>ApplyResources(Me, "$this")</c>, the statement
    /// Visual Studio writes for a form whose Localizable property is True.
    /// </summary>
    public bool IsLocalizable => HasApplyResources("Me", FormResourcePrefix);

    /// <summary>
    /// The trusted ComponentResourceManager local InitializeComponent reads resources through, or null
    /// when the method declares none. The Shell needs it to write a GetObject call for a value it cannot
    /// express as a literal.
    /// </summary>
    public string? ResourceManagerName
    {
        get
        {
            foreach (var declaration in _initialize.Statements.OfType<LocalDeclarationStatementSyntax>())
            {
                var managers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddTrustedManagers(declaration, managers);
                if (managers.Count > 0) return managers.First();
            }
            return null;
        }
    }

    /// <summary>
    /// Writes the Localizable = True layout. The caller has already stored each property in
    /// <paramref name="movedProperties"/> in the neutral .resx, so its assignment statements leave
    /// the code. Every control gets its ApplyResources call in the position Visual Studio uses.
    /// </summary>
    public string EnableLocalization(IReadOnlyCollection<(string ControlName, string PropertyName)> movedProperties) =>
        LocalizeTargets(movedProperties, ResourceTargets());

    /// <summary>
    /// Gives controls that were just added or pasted into a Localizable form the same layout: their
    /// <paramref name="movedProperties"/> leave the code and each control gets its ApplyResources call.
    /// The form itself is not a target, so its own call and its statements are left as they are.
    /// </summary>
    public string LocalizeAddedControls(IReadOnlyCollection<(string ControlName, string PropertyName)> movedProperties,
        IReadOnlyCollection<string> controlNames)
    {
        ArgumentNullException.ThrowIfNull(controlNames);
        return LocalizeTargets(movedProperties, controlNames.Select(name => (name, name)));
    }

    /// <summary>
    /// The one move both the Localizable switch and an added control use: the assignments of
    /// <paramref name="movedProperties"/> leave the code, and every target without an ApplyResources call gets one
    /// in the position Visual Studio uses.
    /// </summary>
    private string LocalizeTargets(IReadOnlyCollection<(string ControlName, string PropertyName)> movedProperties,
        IEnumerable<(string ControlName, string Prefix)> targets)
    {
        ArgumentNullException.ThrowIfNull(movedProperties);
        var (declared, managerName) = EnsureComponentResourceManager();
        var source = declared.RemovePropertyAssignments(movedProperties);
        foreach (var (controlName, prefix) in targets)
        {
            var document = new WinFormsDesignerDocument(source);
            if (document.HasApplyResources(controlName, prefix)) continue;
            source = document.InsertApplyResources(controlName, prefix, managerName);
        }
        return source;
    }

    /// <summary>
    /// Writes the Localizable = False layout. Each ApplyResources call is replaced by the
    /// assignments in <paramref name="assignments"/> for its control, or removed when it has none.
    /// The ComponentResourceManager declaration goes too, once nothing else reads it.
    /// </summary>
    public string DisableLocalization(IReadOnlyCollection<WinFormsLocalizedAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var changes = new List<TextChange>();
        foreach (var (statement, controlName, _) in ApplyResourcesStatements())
        {
            var lines = assignments
                .Where(assignment => assignment.ControlName.Equals(controlName, StringComparison.OrdinalIgnoreCase))
                .Select(assignment => $"{ReceiverOf(controlName)}.{assignment.PropertyName} = {assignment.Expression}")
                .ToArray();
            changes.Add(lines.Length == 0
                ? new TextChange(LineSpan(statement), "")
                // Keep the statement's indentation for every restored line, and leave its leading
                // comment lines in place.
                : new TextChange(statement.Span, string.Join(NewLine + Indentation(statement.SpanStart), lines)));
        }

        var withoutCalls = new WinFormsDesignerDocument(_text.WithChanges(changes).ToString());
        return withoutCalls.RemoveUnusedResourceManager();
    }

    private IEnumerable<(string ControlName, string Prefix)> ResourceTargets()
    {
        yield return ("Me", FormResourcePrefix);
        foreach (var control in Controls)
            yield return (control.Name, control.Name);
    }

    private bool HasApplyResources(string controlName, string prefix) => ResourceOperations.Any(operation =>
        operation.PropertyName is null
        && operation.ControlName.Equals(controlName, StringComparison.OrdinalIgnoreCase)
        && prefix.Equals(operation.ResourceKey, StringComparison.Ordinal));

    private List<(StatementSyntax Statement, string ControlName, string Prefix)> ApplyResourcesStatements() =>
        ReadResourceOperationStatements()
            .Where(entry => entry.Operation.PropertyName is null && entry.Operation.ResourceKey is not null)
            .Select(entry => (entry.Statement, entry.Operation.ControlName, entry.Operation.ResourceKey!))
            .ToList();

    private string RemovePropertyAssignments(IReadOnlyCollection<(string ControlName, string PropertyName)> properties)
    {
        var changes = new List<TextChange>();
        foreach (var statement in _initialize.Statements.OfType<AssignmentStatementSyntax>())
        {
            if (statement.Left is not MemberAccessExpressionSyntax member
                || ControlName(member.Expression) is not { } owner)
                continue;
            var propertyName = member.Name.Identifier.ValueText;
            if (properties.Any(item => item.ControlName.Equals(owner, StringComparison.OrdinalIgnoreCase)
                && item.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase)))
                changes.Add(new TextChange(LineSpan(statement), ""));
        }
        return _text.WithChanges(changes).ToString();
    }

    /// <summary>
    /// Adds the ApplyResources call before the line that follows the control's creation, or after the
    /// form's AutoScale statements, which is where Visual Studio places it.
    /// </summary>
    private string InsertApplyResources(string controlName, string prefix, string managerName)
    {
        var call = $"{managerName}.ApplyResources({ReceiverOf(controlName)}, \"{prefix}\")";
        return InsertLineBefore(ApplyResourcesPosition(controlName), call);
    }

    private int ApplyResourcesPosition(string controlName)
    {
        var statements = _initialize.Statements;
        if (controlName == "Me")
        {
            // The form's call follows its AutoScale statements, ahead of Me.Name and the rest.
            var lastAutoScale = -1;
            for (var index = 0; index < statements.Count; index++)
            {
                if (statements[index] is AssignmentStatementSyntax
                    {
                        Left: MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } form,
                    }
                    && (form.Name.Identifier.ValueText.Equals("AutoScaleDimensions", StringComparison.OrdinalIgnoreCase)
                        || form.Name.Identifier.ValueText.Equals("AutoScaleMode", StringComparison.OrdinalIgnoreCase)))
                    lastAutoScale = index;
            }
            return lastAutoScale < 0
                ? WinFormsInitializeComponentSyntax.InsertionPoint(_initialize)
                : NextStatementOr(lastAutoScale + 1);
        }

        var creation = -1;
        for (var index = 0; index < statements.Count; index++)
        {
            if (statements[index] is AssignmentStatementSyntax { Right: ObjectCreationExpressionSyntax } assignment
                && ControlName(assignment.Left)?.Equals(controlName, StringComparison.OrdinalIgnoreCase) == true)
            {
                creation = index;
                break;
            }
        }
        if (creation < 0) return WinFormsInitializeComponentSyntax.InsertionPoint(_initialize);

        // Visual Studio opens each control's section with its own property assignments, so the call
        // goes in front of the first of them, below the section's comment header.
        for (var index = creation + 1; index < statements.Count; index++)
        {
            if (statements[index] is AssignmentStatementSyntax { Left: MemberAccessExpressionSyntax member }
                && ControlName(member.Expression)?.Equals(controlName, StringComparison.OrdinalIgnoreCase) == true)
                return statements[index].SpanStart;
        }
        return NextStatementOr(creation + 1);
    }

    /// <summary>
    /// The start of the statement at <paramref name="index"/>, or the ResumeLayout insertion point when
    /// there is none. Position is the first token, so an inserted line lands below its comment header.
    /// </summary>
    private int NextStatementOr(int index) =>
        index < _initialize.Statements.Count
            ? _initialize.Statements[index].SpanStart
            : WinFormsInitializeComponentSyntax.InsertionPoint(_initialize);

    /// <summary>
    /// Removes the ComponentResourceManager declaration when no statement still reads it, so a form that
    /// was never localized returns to its original text after Localizable = False.
    /// </summary>
    private string RemoveUnusedResourceManager()
    {
        var changes = new List<TextChange>();
        var readers = _initialize.Statements.Where(statement => statement is not LocalDeclarationStatementSyntax)
            .SelectMany(statement => statement.DescendantTokens())
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in _initialize.Statements.OfType<LocalDeclarationStatementSyntax>())
        {
            var managers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddTrustedManagers(declaration, managers);
            if (managers.Count == 0 || managers.Any(readers.Contains)) continue;
            changes.Add(new TextChange(LineSpan(declaration), ""));
        }
        return _text.WithChanges(changes).ToString();
    }

    private string InsertLineBefore(int position, string statement) =>
        _text.WithChanges(new TextChange(new TextSpan(_text.Lines.GetLineFromPosition(position).Start, 0),
            Indentation(position) + statement + NewLine)).ToString();

    /// <summary>
    /// The lines holding the whole statement, from its first line through the line break after its last token, so
    /// comment lines above it stay. A statement the VB writer continues with <c>&amp; _</c> spans several lines, and
    /// removing only its first would leave the continuation behind as a stray line.
    /// </summary>
    private TextSpan LineSpan(SyntaxNode statement)
    {
        var first = _text.Lines.GetLineFromPosition(statement.SpanStart);
        var last = _text.Lines.GetLineFromPosition(statement.Span.End);
        return TextSpan.FromBounds(first.Start, last.EndIncludingLineBreak);
    }

    private string Indentation(int position)
    {
        var text = _text.Lines.GetLineFromPosition(position).ToString();
        return text[..(text.Length - text.TrimStart().Length)];
    }

    private static string ReceiverOf(string controlName) =>
        controlName.Equals("Me", StringComparison.OrdinalIgnoreCase) ? "Me" : $"Me.{controlName}";
}
