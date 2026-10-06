using SymbolKind = Basalt.Extensibility.SymbolKind;
using Basalt.Workspace.ObjectBrowser;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// The Object Browser's model: what the compiler says about a small Visual
/// Basic project, read the way the panel reads it.
/// </summary>
public sealed class ObjectBrowserTests
{
    internal const string SourcePath = "/work/Shop.vb";

    internal const string Source = """
        Imports System

        Namespace Shop
            ''' <summary>A person who buys things.</summary>
            Public Class Customer
                Inherits Person

                Public Const MaxOrders As Integer = 10
                Private _secret As Integer

                Public Sub New(name As String)
                End Sub

                ''' <summary>Shows the <paramref name="text"/> in a box, see <see cref="Greet"/>.</summary>
                Public Sub Show(text As String)
                End Sub

                Public Function Total(Optional discount As Double = 0) As Decimal
                    Return 0D
                End Function

                Public Property Name As String

                Public Event Changed As EventHandler

                Public Overrides Function ToString() As String
                    Return Name
                End Function

                Friend Sub Hidden()
                End Sub
            End Class

            Public MustInherit Class Shape
            End Class

            Public Class Person
                Public Sub Greet()
                End Sub
            End Class

            Public Module Helpers
                Public Sub Run()
                End Sub
            End Module

            Public Structure Point
                Public X As Integer
            End Structure

            Public Interface IShape
                Function Area() As Double
            End Interface

            Public Enum Palette
                Red
                Green
            End Enum

            Public Delegate Sub Handler(value As Integer)
        End Namespace
        """;

    internal static Compilation Compile(string source = Source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll"
                or "System.Collections.dll" or "Microsoft.VisualBasic.Core.dll")
            .Select(path => MetadataReference.CreateFromFile(path));

        var tree = VisualBasicSyntaxTree.ParseText(
            source,
            new VisualBasicParseOptions(documentationMode: DocumentationMode.Parse),
            path: SourcePath);

        return VisualBasicCompilation.Create(
            "ShopLib", [tree], references,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static ObjectBrowserService ServiceFor(Compilation compilation) =>
        new(_ => Task.FromResult<IReadOnlyList<BrowserProject>>([new BrowserProject("ShopLib", compilation)]),
            pathExists: _ => true);

    private static async Task<BrowserLibrary> ProjectLibraryAsync(ObjectBrowserService service)
    {
        var libraries = await service.GetLibrariesAsync();
        return libraries.Single(library => library.Kind == BrowserLibraryKind.Project);
    }

    internal static int ShowLine { get; } =
        Source.Split('\n').ToList().FindIndex(line => line.Contains("Sub Show")) + 1;

    private static async Task<BrowserSymbol> TypeAsync(
        ObjectBrowserService service, string name)
    {
        var library = await ProjectLibraryAsync(service);
        var types = await service.GetTypesAsync(library, "Shop");
        return types.Single(type => type.Name == name);
    }

    [Fact]
    public async Task ListsAllThenTheProjectThenTheAssembliesItReferences()
    {
        var service = ServiceFor(Compile());

        var libraries = await service.GetLibrariesAsync();

        Assert.Equal(BrowserLibraryKind.All, libraries[0].Kind);
        Assert.Equal("ShopLib", libraries[1].Name);
        Assert.Equal(BrowserLibraryKind.Project, libraries[1].Kind);
        Assert.Contains(libraries, library =>
            library.Kind == BrowserLibraryKind.Assembly && library.Name == "System.Private.CoreLib");
    }

    [Fact]
    public async Task ListsTheNamespacesThatDeclareTypesAndTheirTypesWithKinds()
    {
        var service = ServiceFor(Compile());
        var library = await ProjectLibraryAsync(service);

        var namespaces = await service.GetNamespacesAsync(library);
        Assert.Equal(["Shop"], namespaces);

        var types = await service.GetTypesAsync(library, "Shop");

        var kinds = types.ToDictionary(type => type.Name, type => type.Kind);
        Assert.Equal(SymbolKind.Class, kinds["Customer"]);
        Assert.Equal(SymbolKind.Module, kinds["Helpers"]);
        Assert.Equal(SymbolKind.Structure, kinds["Point"]);
        Assert.Equal(SymbolKind.Interface, kinds["IShape"]);
        Assert.Equal(SymbolKind.Enum, kinds["Palette"]);
        Assert.Equal(SymbolKind.Delegate, kinds["Handler"]);
        Assert.Equal(types.Select(type => type.Name).Order(StringComparer.OrdinalIgnoreCase), types.Select(type => type.Name));
    }

    [Fact]
    public async Task WritesDeclarationsInVisualBasic()
    {
        var service = ServiceFor(Compile());
        var customer = await TypeAsync(service, "Customer");

        var members = await service.GetMembersAsync(customer, includeInherited: false);
        string SignatureOf(string name) => members.Single(member => member.Name == name).Signature;

        Assert.Equal("Public Class Customer", customer.Signature);
        Assert.Equal("Public Module Helpers", (await TypeAsync(service, "Helpers")).Signature);
        Assert.Equal("Public MustInherit Class Shape", (await TypeAsync(service, "Shape")).Signature);
        Assert.Equal("Public Sub Show(text As String)", SignatureOf("Show"));
        Assert.Equal("Public Function Total(Optional discount As Double = 0) As Decimal", SignatureOf("Total"));
        Assert.Equal("Public Property Name As String", SignatureOf("Name"));
        Assert.Equal("Public Event Changed As EventHandler", SignatureOf("Changed"));
        Assert.Equal("Public Const MaxOrders As Integer = 10", SignatureOf("MaxOrders"));
        Assert.Equal("Public Sub New(name As String)", SignatureOf("New"));
    }

    [Fact]
    public async Task ListsMembersWithTheirKindsAndHidesWhatACallerCannotName()
    {
        var service = ServiceFor(Compile());
        var customer = await TypeAsync(service, "Customer");

        var members = await service.GetMembersAsync(customer, includeInherited: false);
        var kinds = members.ToDictionary(member => member.Name, member => member.Kind);

        Assert.Equal(SymbolKind.Method, kinds["Show"]);
        Assert.Equal(SymbolKind.Function, kinds["Total"]);
        Assert.Equal(SymbolKind.Property, kinds["Name"]);
        Assert.Equal(SymbolKind.Event, kinds["Changed"]);
        Assert.Equal(SymbolKind.Constant, kinds["MaxOrders"]);

        // Private fields, the property's accessors and its backing field are
        // the compiler's business, not the browser's; Friend is the author's own.
        Assert.DoesNotContain(members, member => member.Name is "_secret" or "_Name" or "get_Name" or "set_Name");
        Assert.Contains(members, member => member.Name == "Hidden");
    }

    [Fact]
    public async Task InheritedMembersAppearOnlyWhenAsked()
    {
        var service = ServiceFor(Compile());
        var customer = await TypeAsync(service, "Customer");

        var own = await service.GetMembersAsync(customer, includeInherited: false);
        var all = await service.GetMembersAsync(customer, includeInherited: true);

        Assert.DoesNotContain(own, member => member.Name == "Greet");
        Assert.DoesNotContain(own, member => member.IsInherited);

        var greet = Assert.Single(all, member => member.Name == "Greet");
        Assert.True(greet.IsInherited);
        Assert.Equal("Person", greet.DeclaringType);
        Assert.Contains(all, member => member.Name == "GetType" && member.IsInherited);

        // ToString is overridden here: the base's version is not a second row.
        Assert.Single(all, member => member.Name == "ToString");
        Assert.False(all.Single(member => member.Name == "ToString").IsInherited);

        // A base class's New is no way to make a Customer.
        Assert.Single(all, member => member.Name == "New");
    }

    [Fact]
    public async Task DescribesAMemberWithItsContainerAndItsXmlSummary()
    {
        var service = ServiceFor(Compile());
        var customer = await TypeAsync(service, "Customer");
        var show = (await service.GetMembersAsync(customer, false)).Single(member => member.Name == "Show");

        var description = await service.DescribeAsync(show);

        Assert.Equal("Public Sub Show(text As String)", description.Signature);
        Assert.Equal("Customer", description.DeclaringType);
        Assert.Equal("Shop", description.Namespace);
        Assert.Equal("ShopLib", description.AssemblyName);
        Assert.Equal("Shows the text in a box, see Greet.", description.Summary);

        var typeDescription = await service.DescribeAsync(customer);
        Assert.Equal("A person who buys things.", typeDescription.Summary);
        Assert.Null(typeDescription.DeclaringType);
    }

    [Fact]
    public async Task AMemberWithoutDocumentationHasNoSummary()
    {
        var service = ServiceFor(Compile());
        var customer = await TypeAsync(service, "Customer");
        var total = (await service.GetMembersAsync(customer, false)).Single(member => member.Name == "Total");

        Assert.Null((await service.DescribeAsync(total)).Summary);
    }

    [Fact]
    public async Task SearchFindsTypesByTheirNameAndByTheirMembers()
    {
        var service = ServiceFor(Compile());
        var library = await ProjectLibraryAsync(service);

        var byTypeName = (await service.SearchAsync(library, "cust")).Types;
        Assert.Equal(["Customer"], byTypeName.Select(type => type.Name));

        // Show is a member: its type is what the list offers.
        var byMemberName = (await service.SearchAsync(library, "show")).Types;
        Assert.Equal(["Customer"], byMemberName.Select(type => type.Name));

        Assert.Empty((await service.SearchAsync(library, "nothing-called-this")).Types);
        Assert.Empty((await service.SearchAsync(library, "  ")).Types);
    }

    [Fact]
    public async Task SearchNarrowsTheMembersUnlessTheTypeItselfMatched()
    {
        var service = ServiceFor(Compile());
        var customer = await TypeAsync(service, "Customer");
        var members = await service.GetMembersAsync(customer, false);

        var narrowed = ObjectBrowserService.FilterMembers(customer, members, "show");
        Assert.Equal(["Show"], narrowed.Select(member => member.Name));

        // Searching for the type shows the whole type, not an empty list.
        Assert.Equal(members.Count, ObjectBrowserService.FilterMembers(customer, members, "custom").Count);
        Assert.Equal(members.Count, ObjectBrowserService.FilterMembers(customer, members, "").Count);
    }

    [Fact]
    public async Task ASymbolDeclaredInSourceKnowsWhereItIsWrittenAndOneFromMetadataDoesNot()
    {
        var service = ServiceFor(Compile());
        var customer = await TypeAsync(service, "Customer");
        var show = (await service.GetMembersAsync(customer, false)).Single(member => member.Name == "Show");

        var target = Assert.IsType<BrowserSourceTarget>(show.Source);
        Assert.Equal(SourcePath, target.FilePath);
        Assert.Equal(ShowLine, target.Line);
        Assert.Equal(Source.Split('\n')[ShowLine - 1].IndexOf("Show", StringComparison.Ordinal) + 1, target.Column);

        var libraries = await service.GetLibrariesAsync();
        var core = libraries.Single(library => library.Name == "System.Private.CoreLib");
        var system = (await service.GetTypesAsync(core, "System")).Single(type => type.Name == "String");

        Assert.Null(system.Source);
        Assert.Equal("Public NotInheritable Class String", system.Signature);
    }

    [Fact]
    public async Task AReferencedAssemblyIsReadLazilyByNamespace()
    {
        var service = ServiceFor(Compile());
        var libraries = await service.GetLibrariesAsync();
        var core = libraries.Single(library => library.Name == "System.Private.CoreLib");

        var namespaces = await service.GetNamespacesAsync(core);
        Assert.Contains("System", namespaces);
        Assert.Contains("System.Collections.Generic", namespaces);

        var generic = await service.GetTypesAsync(core, "System.Collections.Generic");
        Assert.Contains(generic, type => type.Name == "List(Of T)");

        var list = generic.Single(type => type.Name == "List(Of T)");
        var add = (await service.GetMembersAsync(list, false)).First(member => member.Name == "Add");
        Assert.Equal("Public Sub Add(item As T)", add.Signature);
    }
    [Fact]
    public async Task OpeningASymbolAfterAnEditLandsOnTheCurrentLineNotTheOldOne()
    {
        var current = Compile();
        var service = new ObjectBrowserService(
            _ => Task.FromResult<IReadOnlyList<BrowserProject>>([new BrowserProject("ShopLib", current)]),
            pathExists: _ => true);

        var customer = await TypeAsync(service, "Customer");
        var show = (await service.GetMembersAsync(customer, false)).Single(member => member.Name == "Show");
        Assert.Equal(ShowLine, show.Source!.Line);

        // Three lines are typed above the namespace: the browser still holds
        // the symbol of the compilation it read before.
        current = Compile("' one\n' two\n' three\n" + Source);

        var resolved = await service.ResolveSourceAsync(show);

        Assert.Equal(ShowLine + 3, resolved!.Line);
        Assert.Equal(show.Source!.Column, resolved.Column);

        // And a declaration that was deleted has nowhere to go.
        current = Compile(Source.Replace("Public Sub Show(text As String)", "Public Sub Display(text As String)"));
        Assert.Null(await service.ResolveSourceAsync(show));
    }

    [Fact]
    public async Task ADeclarationWhoseFileCannotBeOpenedHasNowhereToGo()
    {
        var compilation = Compile();
        var service = new ObjectBrowserService(
            _ => Task.FromResult<IReadOnlyList<BrowserProject>>([new BrowserProject("ShopLib", compilation)]),
            pathExists: _ => false);

        var customer = await TypeAsync(service, "Customer");

        Assert.NotNull(customer.Source);
        Assert.Null(await service.ResolveSourceAsync(customer));
    }

    [Fact]
    public async Task APartialTypeOpensInTheAuthorsFileNotTheDesignerFile()
    {
        var references = Compile().References;
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create(
            "Forms",
            [
                Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(
                    "Partial Public Class Form1\nEnd Class\n", path: "/work/Form1.Designer.vb"),
                Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(
                    "Partial Public Class Form1\nEnd Class\n", path: "/work/Form1.vb")
            ],
            references);

        var service = ServiceFor(compilation);
        var form = (await service.GetTypesAsync(await ProjectLibraryAsync(service), "")).Single();

        Assert.Equal("/work/Form1.vb", form.Source!.FilePath);
    }

    [Fact]
    public async Task ADeclarationInGeneratedCodeIsMappedToTheTemplateThatProducedIt()
    {
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create(
            "Views",
            [Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(
                "#ExternalSource(\"/work/Index.vbhtml\", 40)\nPublic Class Index\nEnd Class\n#End ExternalSource\n",
                path: "/work/Index.vbhtml.g.vb")],
            Compile().References);

        var service = ServiceFor(compilation);
        var index = (await service.GetTypesAsync(await ProjectLibraryAsync(service), "")).Single();

        Assert.Equal("/work/Index.vbhtml", index.Source!.FilePath);
        Assert.Equal(40, index.Source.Line);

        // The mapping has a line and no column: the generated column is meaningless there.
        Assert.Equal(1, index.Source.Column);
    }

    [Fact]
    public async Task AProjectThatAnotherReferencesIsListedOnceAsAProject()
    {
        var core = Compile("Namespace Core\n Public Class Engine\n End Class\nEnd Namespace\n");
        var coreReference = core.ToMetadataReference();

        var app = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create(
            "App",
            [Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(
                "Namespace Shell\n Public Class Host\n  Public Engine As Core.Engine\n End Class\nEnd Namespace\n")],
            [.. core.References, coreReference]);

        // The first compilation is named ShopLib by Compile(); name it for what it is.
        core = core.WithAssemblyName("CoreLib");
        app = app.WithReferences([.. core.References, core.ToMetadataReference()]);

        var service = new ObjectBrowserService(
            _ => Task.FromResult<IReadOnlyList<BrowserProject>>(
                [new BrowserProject("CoreLib", core), new BrowserProject("App", app)]),
            pathExists: _ => true);

        var libraries = await service.GetLibrariesAsync();

        Assert.Equal(1, libraries.Count(l => l.Name == "CoreLib"));
        Assert.Equal(BrowserLibraryKind.Project, libraries.Single(l => l.Name == "CoreLib").Kind);

        // "All" holds the Engine once, not once as a project and once as a reference.
        var all = libraries[0];
        var types = await service.GetTypesAsync(all, "Core");
        Assert.Single(types, type => type.Name == "Engine");
    }

    [Fact]
    public async Task OverloadsThatDifferOnlyByReturnTypeAreAllListed()
    {
        var service = ServiceFor(Compile());
        var core = (await service.GetLibrariesAsync()).Single(l => l.Name == "System.Private.CoreLib");
        var decimalType = (await service.GetTypesAsync(core, "System")).Single(type => type.Name == "Decimal");

        var members = await service.GetMembersAsync(decimalType, includeInherited: false);
        var conversions = members.Where(member => member.Symbol is IMethodSymbol { MethodKind: MethodKind.Conversion }).ToList();

        // Decimal converts to and from every numeric type: more than one of
        // them takes a Decimal and nothing else, and they differ by what they return.
        var fromDecimal = conversions.Where(member => ((IMethodSymbol)member.Symbol).Parameters[0].Type.Name == "Decimal").ToList();

        Assert.True(fromDecimal.Count >= 8, $"Found only {fromDecimal.Count} conversions from Decimal.");
        Assert.Equal(fromDecimal.Count, fromDecimal.Select(member => member.Signature).Distinct().Count());
    }

    [Fact]
    public async Task AnInheritedMemberIsHiddenOnlyByAMemberOfTheSameSignature()
    {
        var source = """
            Public Class Base
                Public Sub Run(value As Integer)
                End Sub
                Public Sub Run(value As String)
                End Sub
            End Class

            Public Class Derived
                Inherits Base
                Public Shadows Sub Run(value As Integer)
                End Sub
            End Class
            """;

        var service = ServiceFor(Compile(source));
        var library = await ProjectLibraryAsync(service);
        var derived = (await service.GetTypesAsync(library, "")).Single(type => type.Name == "Derived");

        var runs = (await service.GetMembersAsync(derived, true)).Where(member => member.Name == "Run").ToList();

        Assert.Equal(2, runs.Count);
        Assert.Equal(["Public Sub Run(value As Integer)", "Public Sub Run(value As String)"],
            runs.Select(run => run.Signature).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ALongSearchIsSortedBeforeItIsCutAndSaysHowManyThereWere()
    {
        var service = ServiceFor(Compile());
        var core = (await service.GetLibrariesAsync()).Single(l => l.Name == "System.Private.CoreLib");

        var result = await service.SearchAsync(core, "e");

        Assert.True(result.IsTruncated);
        Assert.Equal(BrowserSearchResult.Cap, result.Types.Count);
        Assert.True(result.Total > BrowserSearchResult.Cap);
        var names = result.Types.Select(type => type.Name.Split('(')[0]).ToList();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact]
    public async Task WritesByRefParamArrayOperatorsGenericAndExtensionMethodsAsVisualBasic()
    {
        var source = """
            Imports System.Runtime.CompilerServices

            Public Structure Money
                Public Shared Operator +(left As Money, right As Money) As Money
                    Return left
                End Operator

                Public Shared Widening Operator CType(value As Integer) As Money
                    Return Nothing
                End Operator
            End Structure

            Public Module Tools
                Public Sub Swap(ByRef first As Integer, ByRef second As Integer)
                End Sub

                Public Function Join(separator As String, ParamArray parts As String()) As String
                    Return separator
                End Function

                Public Function Convert(Of T As {Class, New})(value As Object) As T
                    Return Nothing
                End Function

                <Extension>
                Public Function Twice(value As String) As String
                    Return value
                End Function
            End Module
            """;

        var service = ServiceFor(Compile(source));
        var library = await ProjectLibraryAsync(service);
        var types = await service.GetTypesAsync(library, "");

        async Task<string> SignatureAsync(string type, string member) =>
            (await service.GetMembersAsync(types.Single(t => t.Name == type), false))
                .Single(m => m.Name.Split((char)40)[0] == member).Signature;

        Assert.Equal("Public Sub Swap(ByRef first As Integer, ByRef second As Integer)", await SignatureAsync("Tools", "Swap"));
        Assert.Equal("Public Function Join(separator As String, ParamArray parts As String()) As String", await SignatureAsync("Tools", "Join"));
        Assert.Equal("Public Function Convert(Of T As {Class, New})(value As Object) As T", await SignatureAsync("Tools", "Convert"));
        Assert.Equal("Public Function Twice(value As String) As String", await SignatureAsync("Tools", "Twice"));

        var money = await service.GetMembersAsync(types.Single(t => t.Name == "Money"), false);
        var signatures = money.Select(m => m.Signature).ToList();
        Assert.Contains(signatures, s => s.StartsWith("Public Shared Operator +(left As Money, right As Money)", StringComparison.Ordinal));
        Assert.Contains(signatures, s => s.Contains("Widening Operator CType(value As Integer) As Money", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArraysAndBracketsInASignatureAreNeverTakenForOptionalMarkers()
    {
        // No C# compiler is referenced by the tests, so this covers the
        // Visual Basic side of the rule: only a parameter symbol that is
        // optional gets the keyword, whatever punctuation the display holds.
        var source = """
            Public Module Tools
                Public Sub Run(args As String(), matrix As Integer(,))
                End Sub
            End Module
            """;

        var service = ServiceFor(Compile(source));
        var library = await ProjectLibraryAsync(service);
        var tools = (await service.GetTypesAsync(library, "")).Single();
        var run = (await service.GetMembersAsync(tools, false)).Single(member => member.Name == "Run");

        Assert.Equal("Public Sub Run(args As String(), matrix As Integer(,))", run.Signature);
    }

    [Fact]
    public async Task OptionalAndByRefOptionalParametersAreWrittenTheVisualBasicWay()
    {
        var source = """
            Public Module Tools
                Public Sub Go(ByRef a As Integer, Optional ByVal b As Integer = 1, Optional c As String = "x")
                End Sub
            End Module
            """;

        var service = ServiceFor(Compile(source));
        var library = await ProjectLibraryAsync(service);
        var tools = (await service.GetTypesAsync(library, "")).Single();
        var go = (await service.GetMembersAsync(tools, false)).Single(member => member.Name == "Go");

        Assert.Equal("Public Sub Go(ByRef a As Integer, Optional b As Integer = 1, Optional c As String = \"x\")", go.Signature);
    }

    [Fact]
    public async Task ResolvingASourceSymbolCompilesOnlyTheProjectThatDeclaresIt()
    {
        var a = Compile("Public Class FromA\nEnd Class\n").WithAssemblyName("ProjA");
        var requested = new List<string>();

        BrowserProject Lazy(string name, Compilation compilation) => new(name, name, _ =>
        {
            requested.Add(name);
            return Task.FromResult<Compilation?>(compilation);
        });

        var b = Compile("Public Class FromB\nEnd Class\n").WithAssemblyName("ProjB");
        var service = new ObjectBrowserService(
            _ => Task.FromResult<IReadOnlyList<BrowserProject>>([Lazy("ProjA", a), Lazy("ProjB", b)]),
            pathExists: _ => true);

        var library = (await service.GetLibrariesAsync()).Single(l => l.Name == "ProjA");
        var fromA = (await service.GetTypesAsync(library, "")).Single();
        requested.Clear();

        var target = await service.ResolveSourceAsync(fromA);

        Assert.NotNull(target);
        Assert.Equal(["ProjA"], requested);
    }
}
