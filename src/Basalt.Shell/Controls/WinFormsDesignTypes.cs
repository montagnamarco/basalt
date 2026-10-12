using System.ComponentModel;
using System.Drawing.Design;
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

    /// <summary>
    /// Whether the type is a project or library component that is not a control: it derives from Component and not from
    /// Forms.Control, and it is not a Forms type. A Forms component such as a menu item is never placed in the tray this way.
    /// </summary>
    public static bool IsProjectComponent(Type type) =>
        !IsFormsType(type) && typeof(Component).IsAssignableFrom(type) && !typeof(Forms.Control).IsAssignableFrom(type);

    /// <summary>
    /// Whether the toolbox lists a component. A project's own component is always listed. A library's is listed only when it
    /// marks itself as a component with <c>[ToolboxItem(true)]</c> or <c>[DesignerCategory("Component")]</c>, so a compatibility
    /// package does not flood the toolbox. An item of a Forms collection is never listed: its collection owns it.
    /// </summary>
    public static bool IsListedComponent(Type type, bool projectOwned) =>
        IsProjectComponent(type) && !IsCollectionItem(type) && (projectOwned || MarksItselfAsComponent(type));

    /// <summary>
    /// Whether the type is an item that a Forms collection holds (a menu or ToolStrip item, a grid column or cell, a tree node,
    /// a list item or group, a column header). Such a type derives from Component, but it is not a tray component.
    /// </summary>
    public static bool IsCollectionItem(Type type) =>
        typeof(Forms.ToolStripItem).IsAssignableFrom(type) || typeof(Forms.DataGridViewColumn).IsAssignableFrom(type)
        || typeof(Forms.DataGridViewCell).IsAssignableFrom(type) || typeof(Forms.ColumnHeader).IsAssignableFrom(type)
        || typeof(Forms.TreeNode).IsAssignableFrom(type) || typeof(Forms.ListViewItem).IsAssignableFrom(type)
        || typeof(Forms.ListViewGroup).IsAssignableFrom(type);

    /// <summary>Whether the type itself carries an explicit component mark, read without running its code.</summary>
    private static bool MarksItselfAsComponent(Type type) => type.GetCustomAttributesData().Any(data =>
        data.AttributeType == typeof(ToolboxItemAttribute) && data.ConstructorArguments is [{ Value: true }]
        || data.AttributeType == typeof(DesignerCategoryAttribute) && data.ConstructorArguments is [{ Value: string category }]
            && category.Equals("Component", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the type has a constructor whose only parameter is an IContainer. Visual Studio passes the form's components
    /// container to such a constructor.
    /// </summary>
    public static bool HasContainerConstructor(Type type) => type.GetConstructors()
        .Any(constructor => constructor.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(IContainer));
}
