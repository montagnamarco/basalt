using System;
using System.Collections.Generic;

namespace Basalt.Razor.Vb;

/// <summary>
/// What the component writer can learn about the components a template uses:
/// their parameters and the types of those parameters.
/// </summary>
/// <remarks>
/// The writer has no type system of its own, and some of what a tag means
/// depends on types: whether &lt;Header&gt; inside &lt;Card&gt; is a
/// RenderFragment parameter or another component, whether Count="5" is the
/// text "5" or the number, whether OnSave="Sub() ..." needs an EventCallback.
/// The C# compiler answers these from the compilation; here the source
/// generator answers them from its compilation and hands the answers over
/// through this interface, so the writer stays free of Roslyn. Without a
/// catalog — the editor today — the writer does what it did before.
/// </remarks>
public interface IComponentCatalog
{
    /// <summary>
    /// The component a tag names, found by Visual Basic's own lookup from
    /// where the template's class is, or null when the name is not one.
    /// </summary>
    /// <param name="tagName">The tag as written, without any (Of ...).</param>
    /// <param name="typeArgumentCount">How many type arguments the tag wrote.</param>
    ComponentShape? Find(string tagName, int typeArgumentCount);
}

/// <summary>A component's parameters, as the writer needs them.</summary>
public sealed class ComponentShape(
    string typeName, IReadOnlyList<string> typeParameters, IReadOnlyList<ComponentParameter> parameters)
{
    /// <summary>The type's full Visual Basic name, generic ones without their arguments.</summary>
    public string TypeName { get; } = typeName;

    /// <summary>The component's own type parameters, in order: TItem.</summary>
    public IReadOnlyList<string> TypeParameters { get; } = typeParameters;

    public IReadOnlyList<ComponentParameter> Parameters { get; } = parameters;

    /// <summary>
    /// A parameter by name. Case-insensitive: Blazor matches parameter names
    /// that way, and so does Visual Basic.
    /// </summary>
    public ComponentParameter? Parameter(string name)
    {
        foreach (var parameter in Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)) return parameter;
        }

        return null;
    }
}

/// <summary>What kind of value a parameter takes, as far as the writer cares.</summary>
public enum ParameterKind
{
    /// <summary>String or Object: a literal attribute value is text.</summary>
    Text,

    /// <summary>Any other type: a literal attribute value is Visual Basic.</summary>
    Value,

    RenderFragment,

    /// <summary>RenderFragment(Of T): content with a context value.</summary>
    RenderFragmentOf,

    EventCallback,

    /// <summary>EventCallback(Of T).</summary>
    EventCallbackOf,
}

/// <summary>One [Parameter] property of a component.</summary>
/// <param name="Name">The property's name as declared.</param>
/// <param name="TypeName">Its full Visual Basic type name, type parameters by name.</param>
/// <param name="Kind">What kind of value it takes.</param>
/// <param name="ArgumentType">T of RenderFragment(Of T) or EventCallback(Of T), else null.</param>
public sealed record ComponentParameter(string Name, string TypeName, ParameterKind Kind, string? ArgumentType);
