using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed record WinFormsColumnDefinition(string Name, string TypeName, string HeaderText, int Width = 100, string? OriginalName = null);
public sealed record WinFormsItemsState(IReadOnlyList<string> Items, bool IsSupported, string? UnsupportedReason = null);
public sealed record WinFormsColumnsState(IReadOnlyList<WinFormsColumnDefinition> Columns, bool IsSupported, string? UnsupportedReason = null);

/// <summary>Edits constant collection initialization without evaluating application expressions.</summary>
public sealed class WinFormsCollectionDocument
{
    public static IReadOnlyList<string> ColumnTypes { get; } = [
        "DataGridViewTextBoxColumn", "DataGridViewCheckBoxColumn", "DataGridViewComboBoxColumn",
        "DataGridViewButtonColumn", "DataGridViewImageColumn", "DataGridViewLinkColumn"];
    private static readonly HashSet<string> s_columnBaseProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "HeaderText", "Width", "ReadOnly", "Visible", "Frozen", "DisplayIndex", "DataPropertyName",
        "AutoSizeMode", "FillWeight", "MinimumWidth", "Resizable", "SortMode", "ToolTipText", "DividerWidth", "DefaultCellStyle",
    };
    private readonly SourceText _text;
    private readonly ClassBlockSyntax _class;
    private readonly MethodBlockSyntax _initialize;
    private readonly Dictionary<string, WinFormsItemsState> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WinFormsColumnsState> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, ExpressionSyntax>> _columnProperties = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _newline;

    public WinFormsCollectionDocument(string source) : this(source,
        (CompilationUnitSyntax)VisualBasicSyntaxTree.ParseText(source).GetRoot()) { }

    internal WinFormsCollectionDocument(string source, CompilationUnitSyntax syntax)
    {
        _text = SourceText.From(source);
        _newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        _initialize = syntax.DescendantNodes().OfType<MethodBlockSyntax>().Single(method =>
            method.SubOrFunctionStatement.Identifier.ValueText.Equals("InitializeComponent", StringComparison.OrdinalIgnoreCase));
        _class = _initialize.FirstAncestorOrSelf<ClassBlockSyntax>()!;
        foreach (var field in _class.Members.OfType<FieldDeclarationSyntax>())
        foreach (var name in field.Declarators.SelectMany(declarator => declarator.Names))
        {
            var key = name.Identifier.ValueText;
            _items[key] = ReadItemsCore(key);
            _columns[key] = ReadColumnsCore(key);
            var properties = new Dictionary<string, ExpressionSyntax>(StringComparer.OrdinalIgnoreCase);
            foreach (var assignment in _initialize.Statements.OfType<AssignmentStatementSyntax>())
                if (assignment.Left is MemberAccessExpressionSyntax member && Receiver(member.Expression)?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
                    properties[member.Name.Identifier.ValueText] = assignment.Right;
            _columnProperties[key] = properties;
        }
    }

    public WinFormsItemsState ReadItems(string controlName) => _items.GetValueOrDefault(controlName) ?? new([], false, "Unknown control");
    public WinFormsColumnsState ReadColumns(string controlName) => _columns.GetValueOrDefault(controlName) ?? new([], false, "Unknown control");
    public IReadOnlyDictionary<string, ExpressionSyntax> ReadColumnProperties(string columnName) =>
        _columnProperties.GetValueOrDefault(columnName) ?? new Dictionary<string, ExpressionSyntax>();

    public string SetItems(string controlName, IReadOnlyList<string> values)
    {
        RequireSupported(ReadItems(controlName).IsSupported);
        if (values.Any(value => value.Contains('\r') || value.Contains('\n'))) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionItemTextSingleLine));
        var expression = "New Object() {" + string.Join(", ", values.Select(Quote)) + "}";
        return ReplaceCollection(controlName, "Items", values.Count == 0 ? null : expression, []);
    }

    public string SetColumns(string controlName, IReadOnlyList<WinFormsColumnDefinition> values)
    {
        var original = ReadColumns(controlName);
        RequireSupported(original.IsSupported);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!SyntaxFacts.IsValidIdentifier(value.Name) || !unique.Add(value.Name)) throw new ArgumentException(Localizer.Get(StringKeys.WinFormsCollectionColumnNamesMustBeUniqueIdentifiers));
            if (value.HeaderText.Contains('\r') || value.HeaderText.Contains('\n')) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionColumnHeaderSingleLine));
            if (value.OriginalName is not null && !original.Columns.Any(column => column.Name.Equals(value.OriginalName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsCollectionOriginalColumnNotFound));
            if (!ColumnTypes.Contains(value.TypeName)) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionColumnTypeUnsupported));
            if (value.Width < 2 || value.Width > 65536) throw new ArgumentOutOfRangeException(nameof(values));
            if (!original.Columns.Any(column => column.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase))
                && _class.Members.OfType<FieldDeclarationSyntax>().SelectMany(field => field.Declarators).SelectMany(declarator => declarator.Names)
                    .Any(name => name.Identifier.ValueText.Equals(value.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(Localizer.Get(StringKeys.WinFormsCollectionColumnNameAlreadyUsed));
        }
        var changes = new List<TextChange>();
        var statements = new List<string>();
        foreach (var old in original.Columns.Where(old => !values.Any(value => (value.OriginalName ?? value.Name).Equals(old.Name, StringComparison.OrdinalIgnoreCase))))
        {
            var field = FindField(old.Name);
            if (field.Declarators.SelectMany(declarator => declarator.Names).Count() != 1)
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionSplitSharedFieldRemove));
            if (_class.Members.Where(member => member != field && member != _initialize).Any(member => References(member, old.Name)))
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionColumnReferencedOutsideInitialize));
            var initialization = _initialize.Statements.Where(statement => References(statement, old.Name)
                && !IsCollectionStatement(statement, controlName, "Columns")).ToArray();
            if (initialization.Any(statement => statement is not AssignmentStatementSyntax assignment
                || !(Receiver(assignment.Left)?.Equals(old.Name, StringComparison.OrdinalIgnoreCase) == true
                    || assignment.Left is MemberAccessExpressionSyntax property && Receiver(property.Expression)?.Equals(old.Name, StringComparison.OrdinalIgnoreCase) == true)))
                throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionColumnAdditionalReferences));
            changes.Add(new(field.Span, ""));
            foreach (var statement in initialization)
                changes.Add(new(statement.Span, ""));
        }
        foreach (var value in values)
        {
            var old = original.Columns.FirstOrDefault(column => column.Name.Equals(value.OriginalName ?? value.Name, StringComparison.OrdinalIgnoreCase));
            if (old is not null && !old.Name.Equals(value.Name, StringComparison.Ordinal))
            {
                var field = FindField(old.Name);
                if (_class.Members.Where(member => member != field && member != _initialize).Any(member => References(member, old.Name)))
                    throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionColumnReferencedOutsideInitialize));
                foreach (var identifier in field.Declarators.SelectMany(declarator => declarator.Names))
                    if (identifier.Identifier.ValueText.Equals(old.Name, StringComparison.OrdinalIgnoreCase)) changes.Add(new(identifier.Identifier.Span, value.Name));
                foreach (var statement in _initialize.Statements.Where(statement => !IsCollectionStatement(statement, controlName, "Columns")))
                foreach (var identifier in statement.DescendantNodes().OfType<IdentifierNameSyntax>())
                    if (identifier.Identifier.ValueText.Equals(old.Name, StringComparison.OrdinalIgnoreCase)) changes.Add(new(identifier.Span, value.Name));
            }
            var fullType = "Global.System.Windows.Forms." + value.TypeName;
            if (old is null)
            {
                changes.Add(new(new TextSpan(_class.EndClassStatement.SpanStart, 0),
                    $"    Friend WithEvents {value.Name} As {fullType}{_newline}"));
                statements.Add($"Me.{value.Name} = New {fullType}()");
            }
            else if (old.TypeName != value.TypeName)
            {
                var extraInitialization = _initialize.Statements.Where(statement => References(statement, old.Name)
                    && !IsCollectionStatement(statement, controlName, "Columns"))
                    .Any(statement => statement is not AssignmentStatementSyntax assignment
                        || (Receiver(assignment.Left) is null && (assignment.Left is not MemberAccessExpressionSyntax member
                            || !s_columnBaseProperties.Contains(member.Name.Identifier.ValueText))));
                if (extraInitialization) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionColumnAdditionalInitialization));
                var field = FindField(old.Name);
                if (field.Declarators.Count != 1 || field.Declarators[0].Names.Count != 1
                    || field.Declarators[0].AsClause is not SimpleAsClauseSyntax asClause)
                    throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionSplitSharedFieldChangeType));
                changes.Add(new(asClause.Type.Span, fullType));
                var creation = FindCreation(old.Name)!;
                changes.Add(new(creation.Type.Span, fullType));
            }
            SetColumnProperty(old?.Name ?? value.Name, "Name", Quote(value.Name), changes, statements, value.Name);
            SetColumnProperty(old?.Name ?? value.Name, "HeaderText", Quote(value.HeaderText), changes, statements, value.Name);
            SetColumnProperty(old?.Name ?? value.Name, "Width", value.Width.ToString(System.Globalization.CultureInfo.InvariantCulture), changes, statements, value.Name);
        }
        return ReplaceCollection(controlName, "Columns", values.Count == 0 ? null :
            "New System.Windows.Forms.DataGridViewColumn() {" + string.Join(", ", values.Select(value => "Me." + value.Name)) + "}", changes, statements);
    }

    private void SetColumnProperty(string name, string property, string expression, List<TextChange> changes, List<string> added, string outputName)
    {
        var assignments = _initialize.Statements.OfType<AssignmentStatementSyntax>().Where(statement =>
            statement.Left is MemberAccessExpressionSyntax member && Receiver(member.Expression) is { } receiver
            && receiver.Equals(name, StringComparison.OrdinalIgnoreCase) && member.Name.Identifier.ValueText.Equals(property, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (assignments.Length == 0) added.Add($"Me.{outputName}.{property} = {expression}");
        else foreach (var assignment in assignments) changes.Add(new(assignment.Right.Span, expression));
    }

    private string ReplaceCollection(string control, string collection, string? array, IReadOnlyList<TextChange> otherChanges, IReadOnlyList<string>? before = null)
    {
        var matches = _initialize.Statements.Where(statement => IsCollectionStatement(statement, control, collection)).ToArray();
        var changes = otherChanges.ToList();
        var position = matches.FirstOrDefault()?.SpanStart ?? WinFormsInitializeComponentSyntax.InsertionPoint(_initialize);
        var lines = (before ?? []).ToList();
        if (array is not null) lines.Add($"Me.{control}.{collection}.AddRange({array})");
        // Insert before existing trivia rather than replacing it: comments remain exactly where written.
        var replacement = lines.Count == 0 ? "" : string.Join(_newline + "        ", lines);
        foreach (var statement in matches)
            changes.Add(new(statement.Span, statement == matches[0] ? replacement : ""));
        if (matches.Length == 0 && lines.Count != 0)
            changes.Add(new(new TextSpan(position, 0), "        " + replacement + _newline));
        return _text.WithChanges(changes).ToString();
    }

    private WinFormsItemsState ReadItemsCore(string name)
    {
        var items = new List<string>();
        foreach (var statement in _initialize.Statements.Where(statement => ReferencesCollection(statement, name, "Items")))
        {
            if (!TryValues(statement, name, "Items", out var expressions)) return new(items, false, statement.ToString());
            foreach (var expression in expressions)
            {
                if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression)) return new(items, false, expression.ToString());
                items.Add(literal.Token.ValueText);
            }
        }
        return new(items, true);
    }

    private WinFormsColumnsState ReadColumnsCore(string name)
    {
        var columns = new List<WinFormsColumnDefinition>();
        foreach (var statement in _initialize.Statements.Where(statement => ReferencesCollection(statement, name, "Columns")))
        {
            if (!TryValues(statement, name, "Columns", out var expressions)) return new(columns, false, statement.ToString());
            foreach (var expression in expressions)
            {
                var columnName = Receiver(expression);
                var creation = columnName is null ? null : FindCreation(columnName);
                var fullType = creation?.Type.ToString();
                if (fullType?.StartsWith("Global.", StringComparison.OrdinalIgnoreCase) == true) fullType = fullType[7..];
                var type = ColumnTypes.FirstOrDefault(candidate => ("System.Windows.Forms." + candidate).Equals(fullType, StringComparison.OrdinalIgnoreCase));
                if (columnName is null || type is null) return new(columns, false, expression.ToString());
                var header = ReadLiteralProperty(columnName, "HeaderText", columnName);
                var width = ReadLiteralProperty(columnName, "Width", "100");
                if (!HasConstantProperty(columnName, "HeaderText", SyntaxKind.StringLiteralExpression)
                    || !HasConstantProperty(columnName, "Name", SyntaxKind.StringLiteralExpression)
                    || !HasConstantProperty(columnName, "Width", SyntaxKind.NumericLiteralExpression))
                    return new(columns, false, columnName);
                if (!int.TryParse(width, out var size)) return new(columns, false, width);
                columns.Add(new(columnName, type, header, size, columnName));
            }
        }
        return new(columns, true);
    }

    private string ReadLiteralProperty(string name, string property, string fallback)
    {
        var value = _initialize.Statements.OfType<AssignmentStatementSyntax>().LastOrDefault(statement =>
            statement.Left is MemberAccessExpressionSyntax member && Receiver(member.Expression)?.Equals(name, StringComparison.OrdinalIgnoreCase) == true
            && member.Name.Identifier.ValueText.Equals(property, StringComparison.OrdinalIgnoreCase))?.Right;
        return value is LiteralExpressionSyntax literal ? literal.Token.ValueText : value?.ToString() ?? fallback;
    }

    private bool HasConstantProperty(string name, string property, SyntaxKind kind) =>
        _initialize.Statements.OfType<AssignmentStatementSyntax>().Where(statement =>
            statement.Left is MemberAccessExpressionSyntax member && Receiver(member.Expression)?.Equals(name, StringComparison.OrdinalIgnoreCase) == true
            && member.Name.Identifier.ValueText.Equals(property, StringComparison.OrdinalIgnoreCase))
        .All(statement => statement.Right.IsKind(kind));

    private ObjectCreationExpressionSyntax? FindCreation(string name) => _initialize.Statements.OfType<AssignmentStatementSyntax>()
        .FirstOrDefault(statement => Receiver(statement.Left)?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)?.Right as ObjectCreationExpressionSyntax;
    private FieldDeclarationSyntax FindField(string name) => _class.Members.OfType<FieldDeclarationSyntax>().Single(field =>
        field.Declarators.SelectMany(declarator => declarator.Names).Any(identifier => identifier.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase)));
    private static bool References(SyntaxNode syntax, string name) => syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
        .Any(identifier => identifier.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static bool ReferencesCollection(SyntaxNode syntax, string name, string collection) => syntax.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
        .Any(member => Receiver(member.Expression)?.Equals(name, StringComparison.OrdinalIgnoreCase) == true && member.Name.Identifier.ValueText.Equals(collection, StringComparison.OrdinalIgnoreCase));
    private static bool IsCollectionStatement(StatementSyntax statement, string name, string collection) => TryValues(statement, name, collection, out _);
    private static bool TryValues(StatementSyntax statement, string name, string collection, out IReadOnlyList<ExpressionSyntax> values)
    {
        values = [];
        if (statement.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.CommentTrivia) && statement.Span.Contains(trivia.Span))) return false;
        if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }
            || call.Expression is not MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax receiver } method
            || Receiver(receiver.Expression)?.Equals(name, StringComparison.OrdinalIgnoreCase) != true
            || !receiver.Name.Identifier.ValueText.Equals(collection, StringComparison.OrdinalIgnoreCase)) return false;
        var args = call.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().Select(argument => argument.Expression).ToArray() ?? [];
        if (method.Name.Identifier.ValueText.Equals("Add", StringComparison.OrdinalIgnoreCase) && args.Length == 1) { values = args; return true; }
        if (!method.Name.Identifier.ValueText.Equals("AddRange", StringComparison.OrdinalIgnoreCase) || args is not [ArrayCreationExpressionSyntax array]
            || array.Initializer is null) return false;
        values = array.Initializer.Initializers.OfType<ExpressionSyntax>().ToArray();
        return values.Count == array.Initializer.Initializers.Count;
    }
    private static string? Receiver(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax { Expression: MeExpressionSyntax } member => member.Name.Identifier.ValueText,
        _ => null,
    };
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static void RequireSupported(bool supported) { if (!supported) throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsCollectionUnsupported)); }
}
