using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <param name="Kind">
/// The icon to show beside the entry; null for a pseudo-entry such as
/// "(Declarations)" that names a position rather than a symbol.
/// </param>
/// <param name="HasHandler">
/// Whether this event already has a handler — shown in bold, the way Visual
/// Studio marks a control's wired-up events in the Procedure list.
/// </param>
internal sealed record VisualBasicNavigationMember(string Name, int Offset,
    string? EventName = null, Type? DelegateType = null, string? Parameters = null,
    CompletionKind? Kind = null, bool HasHandler = false)
{
    public override string ToString() => Name;
}

/// <param name="IsGeneral">
/// Whether this is the type's own "(General)" entry — the one the caret
/// following logic falls back to when it is inside the type but not inside
/// any control or component field's own entry.
/// </param>
internal sealed record VisualBasicNavigationObject(string ClassName, string Name, int Offset,
    IReadOnlyList<VisualBasicNavigationMember> Members, bool IsGeneral = false)
{
    public override string ToString() => Name;
}

/// <summary>Builds navigation from VB syntax and trusted framework metadata, never application instances.</summary>
internal static class VisualBasicEditorNavigation
{
    public static IReadOnlyList<VisualBasicNavigationObject> Read(string source, string? companion)
    {
        var root = VisualBasicSyntaxTree.ParseText(source).GetRoot();
        var companionRoot = companion is null ? null : VisualBasicSyntaxTree.ParseText(companion).GetRoot();
        var allTypes = root.DescendantNodes().OfType<TypeBlockSyntax>()
            .Concat(companionRoot?.DescendantNodes().OfType<TypeBlockSyntax>() ?? []).ToArray();
        var result = new List<VisualBasicNavigationObject>();
        foreach (var type in root.DescendantNodes().OfType<TypeBlockSyntax>())
        {
            var className = type.BlockStatement.Identifier.ValueText;

            // Declaration order, as VB6 and Visual Studio's own Procedure list
            // show it — not alphabetical, which would scramble the order the
            // author wrote the class in.
            var declared = type.Members.Select(Member).OfType<VisualBasicNavigationMember>().ToArray();

            // "(Declarations)" leads the list, as it does in VB6: selecting it
            // puts the caret at the top of the general section, ready for a
            // new field or the first statement, rather than inside a member.
            var general = new List<VisualBasicNavigationMember>(declared.Length + 1)
            {
                new(Localizer.Get(StringKeys.EditorVbDeclarations), type.BlockStatement.FullSpan.End),
            };
            general.AddRange(declared);

            result.Add(new(className, Localizer.Get(StringKeys.EditorVbGeneral),
                type.BlockStatement.Identifier.SpanStart, general, IsGeneral: true));

            if (type is not ClassBlockSyntax)
            {
                continue;
            }
            var parts = allTypes.Where(candidate => candidate.BlockStatement.Identifier.ValueText
                .Equals(className, StringComparison.OrdinalIgnoreCase)).ToArray();
            var inherited = parts.SelectMany(part => part.Inherits)
                .SelectMany(statement => statement.Types).FirstOrDefault();
            // Read once per class: each WithEvents field below would otherwise
            // parse the whole file again on every refresh.
            var handled = HandledEvents(source, className);
            var ownEvents = WithHandlerFlags(EventsFor(inherited, allTypes), handled, "Me");
            if (ownEvents.Count > 0)
            {
                result.Add(new(className, Localizer.Get(StringKeys.EditorVbFormEvents, className),
                    type.BlockStatement.Identifier.SpanStart, ownEvents));
            }
            foreach (var field in parts.SelectMany(part => part.Members.OfType<FieldDeclarationSyntax>())
                .Where(field => field.Modifiers.Any(token => token.IsKind(SyntaxKind.WithEventsKeyword))))
            {
                foreach (var declaration in field.Declarators)
                {
                    var fieldType = (declaration.AsClause as SimpleAsClauseSyntax)?.Type;
                    var rawEvents = EventsFor(fieldType, allTypes);
                    foreach (var name in declaration.Names)
                    {
                        var events = WithHandlerFlags(rawEvents, handled, name.Identifier.ValueText);
                        result.Add(new(className, name.Identifier.ValueText, type.BlockStatement.Identifier.SpanStart, events));
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Marks each event already bound to a handler in the source, so the
    /// Procedure list can show it in bold the way Visual Studio does.
    /// </summary>
    private static IReadOnlyList<VisualBasicNavigationMember> WithHandlerFlags(
        IReadOnlyList<VisualBasicNavigationMember> events, ISet<(string Control, string Event)> handled, string controlName)
    {
        if (events.Count == 0 || handled.Count == 0)
        {
            return events;
        }
        return [.. events.Select(member => member.EventName is { } eventName && handled.Contains((controlName, eventName))
            ? member with { HasHandler = true }
            : member)];
    }

    /// <summary>
    /// Every (control, event) pair a Handles clause in the class already binds,
    /// matched without regard to case as Visual Basic matches names. Empty when
    /// the class cannot be found in the text the handlers live in.
    /// </summary>
    private static ISet<(string Control, string Event)> HandledEvents(string source, string className)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var handled = new HashSet<(string Control, string Event)>(new PairComparer(comparer));
        var root = VisualBasicSyntaxTree.ParseText(source).GetRoot();
        foreach (var type in root.DescendantNodes().OfType<ClassBlockSyntax>()
                     .Where(type => comparer.Equals(type.ClassStatement.Identifier.ValueText, className)))
        {
            foreach (var method in type.Members.OfType<MethodBlockSyntax>())
            {
                if (method.SubOrFunctionStatement.HandlesClause is not { } clause) continue;
                foreach (var item in clause.Events)
                    handled.Add((item.EventContainer.ToString(), item.EventMember.Identifier.ValueText));
            }
        }
        return handled;
    }

    private sealed class PairComparer(StringComparer comparer) : IEqualityComparer<(string Control, string Event)>
    {
        public bool Equals((string Control, string Event) x, (string Control, string Event) y) =>
            comparer.Equals(x.Control, y.Control) && comparer.Equals(x.Event, y.Event);

        public int GetHashCode((string Control, string Event) pair) =>
            HashCode.Combine(comparer.GetHashCode(pair.Control), comparer.GetHashCode(pair.Event));
    }

    private static VisualBasicNavigationMember? Member(StatementSyntax statement) => statement switch
    {
        MethodBlockSyntax method => new(method.SubOrFunctionStatement.Identifier.ValueText,
            BodyOffset(method), Kind: CompletionKind.Method),
        PropertyBlockSyntax property => new(property.PropertyStatement.Identifier.ValueText,
            property.PropertyStatement.Identifier.SpanStart, Kind: CompletionKind.Property),
        PropertyStatementSyntax property => new(property.Identifier.ValueText, property.Identifier.SpanStart,
            Kind: CompletionKind.Property),
        EventStatementSyntax eventStatement => new(eventStatement.Identifier.ValueText, eventStatement.Identifier.SpanStart,
            Kind: CompletionKind.Event),
        _ => null,
    };

    private static int BodyOffset(MethodBlockSyntax method) => method.Statements.FirstOrDefault()?.SpanStart
        ?? method.EndSubOrFunctionStatement.FullSpan.Start;

    private static IReadOnlyList<VisualBasicNavigationMember> EventsFor(TypeSyntax? syntax, TypeBlockSyntax[] allTypes)
    {
        if (syntax is null)
        {
            return [];
        }
        var name = syntax.ToString().Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
        var firstIdentifier = syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .FirstOrDefault()?.Identifier.ValueText;
        var hasAlias = !syntax.DescendantNodesAndSelf().OfType<GlobalNameSyntax>().Any()
            && syntax.SyntaxTree.GetRoot().DescendantNodes().OfType<SimpleImportsClauseSyntax>()
            .Any(import => import.Alias?.Identifier.ValueText.Equals(firstIdentifier, StringComparison.OrdinalIgnoreCase) == true);
        if (hasAlias)
        {
            // Aliases need project semantic resolution. Guessing a framework type
            // here can generate a handler with the wrong delegate signature.
            return [];
        }
        var local = !name.Contains('.') ? allTypes.FirstOrDefault(type => type.BlockStatement.Identifier.ValueText
            .Equals(name, StringComparison.OrdinalIgnoreCase)) : null;
        if (local is not null)
        {
            return LocalEvents(local);
        }
        if (!name.Contains('.') && IsImportedFromAnotherFrameworkNamespace(name, syntax.SyntaxTree.GetRoot()))
        {
            // Imports System.Timers makes Timer mean System.Timers.Timer; assuming the
            // Windows Forms type would offer Tick and generate a handler that does not compile.
            return [];
        }
        var qualified = name.Contains('.') ? name : "System.Windows.Forms." + name;
        var runtime = typeof(Forms.Control).Assembly.GetType(qualified, false, true)
            ?? typeof(Forms.MouseEventArgs).Assembly.GetType(qualified, false, true);
        if (name.Equals("SerialPort", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System.IO.Ports.SerialPort", StringComparison.OrdinalIgnoreCase))
        {
            // Only inspect the trusted framework type. Constructing it or opening a
            // port would execute application behaviour during editor navigation.
            runtime = typeof(System.IO.Ports.SerialPort);
        }
        if (runtime is not null)
        {
            return TypeDescriptor.GetEvents(runtime).Cast<EventDescriptor>()
                .Where(descriptor => descriptor.IsBrowsable && descriptor.EventType.GetMethod("Invoke") is not null)
                .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
                .Select(descriptor => new VisualBasicNavigationMember(descriptor.Name, 0,
                    descriptor.Name, descriptor.EventType, Kind: CompletionKind.Event)).ToArray();
        }
        return [];
    }

    // The framework assemblies that define component types a form commonly declares
    // WithEvents under the same simple name as a Forms type (Timer above all). Listing
    // them makes the check independent of which assemblies the IDE happens to have loaded.
    private static readonly System.Reflection.Assembly[] s_frameworkComponentAssemblies =
    [
        typeof(object).Assembly,
        typeof(System.Timers.Timer).Assembly,
        typeof(System.ComponentModel.Component).Assembly,
        typeof(System.IO.Ports.SerialPort).Assembly,
    ];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> s_frameworkTypeExists =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool IsImportedFromAnotherFrameworkNamespace(string typeName, SyntaxNode root)
    {
        var importedNamespaces = root.DescendantNodes().OfType<SimpleImportsClauseSyntax>()
            .Where(import => import.Alias is null)
            .Select(import => import.Name.ToString())
            .Where(name => !name.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase));
        foreach (var importedNamespace in importedNamespaces)
        {
            var candidate = importedNamespace + "." + typeName;
            // Navigation is rebuilt after every debounced edit; a case-insensitive type
            // lookup scans each assembly, so remember the answer per qualified name.
            var exists = s_frameworkTypeExists.GetOrAdd(candidate, name => s_frameworkComponentAssemblies
                .Any(assembly => assembly.GetType(name, false, true) is not null));
            if (exists)
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<VisualBasicNavigationMember> LocalEvents(TypeBlockSyntax type) =>
        type.Members.OfType<EventStatementSyntax>()
            .Where(statement => statement.ParameterList is not null)
            .Select(statement => new VisualBasicNavigationMember(statement.Identifier.ValueText, 0,
                statement.Identifier.ValueText, Parameters: statement.ParameterList!.ToString(), Kind: CompletionKind.Event))
            .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static (string Source, int Offset) SelectEvent(string source,
        VisualBasicNavigationObject target, VisualBasicNavigationMember member)
    {
        var control = target.Name.StartsWith('(') ? "Me" : target.Name;
        var binding = Basalt.Workspace.WinFormsEventHandlers.Read(source, target.ClassName, control);
        if (binding.TryGetValue(member.EventName!, out var existing))
        {
            return (source, Basalt.Workspace.WinFormsEventHandlers.HandlerOffset(source, target.ClassName, existing));
        }
        var root = VisualBasicSyntaxTree.ParseText(source).GetRoot();
        var type = root.DescendantNodes().OfType<ClassBlockSyntax>().Single(candidate =>
            candidate.ClassStatement.Identifier.ValueText.Equals(target.ClassName, StringComparison.OrdinalIgnoreCase));
        var used = type.Members.OfType<MethodBlockSyntax>().Select(method =>
            method.SubOrFunctionStatement.Identifier.ValueText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = (control == "Me" ? target.ClassName : control) + "_" + member.EventName;
        var name = stem;
        for (var number = 1; used.Contains(name); number++)
        {
            name = stem + number;
        }
        string updated;
        if (member.DelegateType is { } delegateType)
        {
            updated = Basalt.Workspace.WinFormsEventHandlers.Set(source, target.ClassName,
                control, member.EventName!, name, delegateType);
        }
        else
        {
            var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var handler = $"{newline}    Private Sub {name}{member.Parameters} Handles {control}.{member.EventName}{newline}        {newline}    End Sub{newline}";
            updated = SourceText.From(source).WithChanges(new TextChange(
                new TextSpan(type.EndClassStatement.FullSpan.Start, 0), handler)).ToString();
        }
        return (updated, Basalt.Workspace.WinFormsEventHandlers.HandlerOffset(updated, target.ClassName, name));
    }
}
