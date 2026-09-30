using System.Reflection;
using System.Text;

namespace Basalt.Drawing.Tests.Api;

/// <summary>
/// Writes the public surface of an assembly as one line per type, enum value,
/// field, constructor and method.
/// </summary>
/// <remarks>
/// An application compiled against Microsoft's System.Drawing.Common binds to
/// ours by simple name, and from then on every call is resolved by exact
/// signature: a constructor with an optional parameter where Microsoft has two
/// constructors compiles from source and throws MissingMethodException from a
/// binary. Properties and events are not listed because a binary never calls
/// them: it calls their accessor methods, which are.
///
/// The same code formats Microsoft's reference assembly (through
/// MetadataLoadContext, in the generator under docs/planning) and ours (through
/// ordinary reflection), so it touches only what both can answer: names,
/// never runtime handles or custom attribute instances.
/// </remarks>
public static class ApiSurface
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static SortedSet<string> Describe(Assembly assembly)
    {
        var lines = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetExportedTypes())
        {
            lines.Add($"T {Kind(type)} {Name(type)}{BaseOf(type)}");

            foreach (var implemented in DeclaredInterfaces(type))
            {
                lines.Add($"I {Name(type)} : {Name(implemented)}");
            }

            foreach (var field in type.GetFields(Declared))
            {
                if (!IsVisible(field.IsPublic, field.IsFamily, field.IsFamilyOrAssembly)) continue;

                // A constant is copied into the caller when it is compiled, a
                // read-only field is read when it runs: swapping one for the
                // other changes what an existing binary sees.
                var storage = field.IsLiteral ? " const" : (field.IsStatic ? " static" : "") + (field.IsInitOnly ? " readonly" : "");
                var constant = type.IsEnum && field.IsLiteral ? " = " + Convert.ToInt64(field.GetRawConstantValue()) : "";
                lines.Add($"F {Access(field.IsPublic)} {Name(type)}::{field.Name} : {Name(field.FieldType)}{storage}{constant}");
            }

            foreach (var constructor in type.GetConstructors(Declared))
            {
                if (constructor.IsStatic) continue;
                if (!IsVisible(constructor.IsPublic, constructor.IsFamily, constructor.IsFamilyOrAssembly)) continue;

                lines.Add($"M {Access(constructor.IsPublic)} {Name(type)}::.ctor({Parameters(constructor)})");
            }

            foreach (var method in type.GetMethods(Declared))
            {
                if (!IsVisible(method.IsPublic, method.IsFamily, method.IsFamilyOrAssembly)) continue;

                var generic = method.IsGenericMethodDefinition ? "`" + method.GetGenericArguments().Length : "";
                var returned = (method.ReturnType.IsByRef ? "ref " : "") + Name(method.ReturnType);
                lines.Add($"M {Access(method.IsPublic)} {Name(type)}::{method.Name}{generic}({Parameters(method)}) -> {returned}{Modifiers(method)}");
            }
        }

        return lines;
    }

    private static bool IsVisible(bool isPublic, bool isFamily, bool isFamilyOrAssembly) =>
        isPublic || isFamily || isFamilyOrAssembly;

    /// <summary>
    /// From outside the assembly "protected internal" is "protected", so the two
    /// are one word here; "public" against "protected" is a real difference: a
    /// call compiled against a public member fails on a protected one.
    /// </summary>
    private static string Access(bool isPublic) => isPublic ? "public" : "protected";

    private static string Kind(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";
        if (type.IsValueType) return "struct";
        if (type.BaseType?.FullName == "System.MulticastDelegate") return "delegate";

        var modifiers = type.IsAbstract && type.IsSealed ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : "";
        return modifiers + "class";
    }

    private static string BaseOf(Type type)
    {
        if (type.IsEnum) return " : " + Name(type.GetEnumUnderlyingType());
        if (type.IsInterface || type.IsValueType || type.BaseType is null) return "";
        if (type.BaseType.FullName is "System.Object" or "System.MulticastDelegate") return "";

        return " : " + Name(type.BaseType);
    }

    /// <summary>The interfaces a type adds, without those its base already had.</summary>
    private static IEnumerable<Type> DeclaredInterfaces(Type type)
    {
        var inherited = new HashSet<string>(
            (type.BaseType?.GetInterfaces() ?? []).Select(Name), StringComparer.Ordinal);

        return type.GetInterfaces().Where(candidate => IsNameable(candidate) && !inherited.Contains(Name(candidate)));
    }

    /// <summary>
    /// An internal interface of the implementation is not part of the contract
    /// and the reference assembly does not carry it.
    /// </summary>
    private static bool IsNameable(Type type) => type.IsPublic || type.IsNestedPublic;

    private static string Modifiers(MethodInfo method)
    {
        if (method.IsStatic) return " static";
        if (method.IsAbstract) return " abstract";
        if (method.IsVirtual && !method.IsFinal) return " virtual";

        return "";
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter =>
            Passing(parameter) + Name(parameter.ParameterType)));

    private static string Passing(ParameterInfo parameter)
    {
        if (!parameter.ParameterType.IsByRef) return "";
        if (parameter.IsOut) return "out ";
        if (parameter.IsIn) return "in ";

        return "ref ";
    }

    private static string Name(Type type)
    {
        if (type.IsByRef) return Name(type.GetElementType()!);
        if (type.IsPointer) return Name(type.GetElementType()!) + "*";
        if (type.IsArray) return Name(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return type.Name;

        var text = new StringBuilder();

        if (type.DeclaringType is not null)
        {
            text.Append(Name(type.DeclaringType)).Append('+');
        }
        else if (!string.IsNullOrEmpty(type.Namespace))
        {
            text.Append(type.Namespace).Append('.');
        }

        text.Append(type.Name);

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            text.Append('<').Append(string.Join(", ", type.GetGenericArguments().Select(Name))).Append('>');
        }

        return text.ToString();
    }
}
