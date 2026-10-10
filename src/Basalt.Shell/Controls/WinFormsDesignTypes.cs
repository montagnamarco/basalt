using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// Finds the types a designer file names, and says which of them the designer may place.
/// </summary>
/// <remarks>
/// One resolver for the preview, the toolbox and every placement path. The built-in Forms
/// assembly is tried first, so a name such as <c>System.Windows.Forms.Button</c> always means
/// the Forms control; the project's own types are tried only when Forms does not have the name.
/// </remarks>
internal static class WinFormsDesignTypes
{
    /// <summary>The type with this full name, from Forms first and then the design context; null when neither has it.</summary>
    public static Type? Find(string qualifiedName, WinFormsDesignAssemblyContext? context) =>
        typeof(Forms.Control).Assembly.GetType(qualifiedName, throwOnError: false, ignoreCase: true)
        ?? context?.FindType(qualifiedName);

    /// <summary>Whether the type is a built-in Forms type.</summary>
    public static bool IsFormsType(Type type) => type.Assembly == typeof(Forms.Control).Assembly;

    /// <summary>Whether the designer may place the type: a Forms type or a type of the current design context.</summary>
    public static bool IsDesignable(Type type, WinFormsDesignAssemblyContext? context) =>
        IsFormsType(type) || context?.Contains(type) == true;
}
