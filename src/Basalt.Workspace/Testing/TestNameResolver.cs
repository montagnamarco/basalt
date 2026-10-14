using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Basalt.Workspace.Testing;

/// <summary>
/// Gives each test the class it is declared in, when the runner did not say.
///
/// "dotnet test --list-tests" names MSTest and NUnit tests by method alone
/// ("Passes", "Adds (1,1,2)"), so the list cannot run them: the runner's
/// filter matches the fully-qualified name, which has the class in it. The
/// class is not in the listing, and no runner prints it before a run. It is
/// in the compiled assembly, so the methods that carry a test attribute are
/// read from its metadata and matched to the listed names by method name.
/// </summary>
/// <remarks>
/// A name that already carries its class (xUnit lists "Class.Method(...)") is
/// left as it is. A method name declared by two classes gives one name per
/// class: the listing has collapsed the two into one line, and dropping one
/// would hide a test, so both are kept.
/// </remarks>
internal static class TestNameResolver
{
    /// <summary>
    /// The attributes that mark a method as a test, for the frameworks the IDE
    /// discovers. A method is found by the attribute, not by a name convention.
    /// </summary>
    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute",
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestCaseAttribute",
        "NUnit.Framework.TestCaseSourceAttribute",
        "NUnit.Framework.TheoryAttribute",
        "Xunit.FactAttribute",
        "Xunit.TheoryAttribute",
    };

    /// <summary>
    /// The test methods of a built assembly: each method name with the full
    /// names of the classes that declare a test of that name.
    /// </summary>
    /// <remarks>
    /// Returns nothing for an assembly that cannot be read, so the listed
    /// names stay as the runner gave them rather than the discovery failing.
    /// </remarks>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> TestMethodsByName(string assemblyPath)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        try
        {
            // Shared with delete as well as read and write: a build that
            // replaces the assembly must not fail because the listing reads it.
            using var stream = new FileStream(
                assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var portable = new PEReader(stream);

            var reader = portable.GetMetadataReader();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);

                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);

                    if (!method.GetCustomAttributes().Any(a => TestAttributes.Contains(AttributeTypeName(reader, a))))
                        continue;

                    var methodName = reader.GetString(method.Name);
                    var className = FullTypeName(reader, typeHandle);

                    if (!owners.TryGetValue(methodName, out var classes))
                        owners[methodName] = classes = [];

                    if (!classes.Contains(className, StringComparer.Ordinal)) classes.Add(className);
                }
            }
        }
        // InvalidOperationException: a valid PE file without managed metadata
        // (a native library, say) is refused by the metadata reader with it.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or BadImageFormatException or InvalidOperationException)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        return owners.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The names a listed test can run under: the listed name itself when it
    /// already has its class, otherwise the class in front of it for each
    /// class that declares a test of that method.
    /// </summary>
    internal static IReadOnlyList<string> Qualify(
        string listed, IReadOnlyDictionary<string, IReadOnlyList<string>> methodsByName)
    {
        if (HasClass(listed)) return [listed];

        if (!methodsByName.TryGetValue(MethodOf(listed), out var classes) || classes.Count == 0)
            return [listed];

        return [.. classes.Select(c => c + "." + listed)];
    }

    /// <summary>
    /// The method part of a listed name: everything before its arguments.
    /// "Adds (1,1,2)" and "Adds(1,1,2)" are both "Adds".
    /// </summary>
    internal static string MethodOf(string listed)
    {
        var arguments = listed.IndexOf('(');

        return (arguments < 0 ? listed : listed[..arguments]).Trim();
    }

    /// <summary>Whether the name carries its class before the method.</summary>
    internal static bool HasClass(string listed) => MethodOf(listed).Contains('.');

    private static string AttributeTypeName(MetadataReader reader, CustomAttributeHandle handle)
    {
        var constructor = reader.GetCustomAttribute(handle).Constructor;

        switch (constructor.Kind)
        {
            case HandleKind.MemberReference:
                var parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;

                return parent.Kind switch
                {
                    HandleKind.TypeReference => TypeReferenceName(reader, (TypeReferenceHandle)parent),
                    HandleKind.TypeDefinition => FullTypeName(reader, (TypeDefinitionHandle)parent),
                    _ => ""
                };

            case HandleKind.MethodDefinition:
                var declaring = reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType();

                return declaring.IsNil ? "" : FullTypeName(reader, declaring);

            default:
                return "";
        }
    }

    private static string TypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);

        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>
    /// The name the runners use for a class: its namespace, and for a nested
    /// class the outer class joined with '+', as the test host writes it.
    /// </summary>
    private static string FullTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);

        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return FullTypeName(reader, declaring) + "+" + name;

        var ns = reader.GetString(type.Namespace);

        return ns.Length == 0 ? name : ns + "." + name;
    }
}
