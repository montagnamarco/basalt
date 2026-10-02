using System.Reflection;
using System.Windows.Forms;

// Every typeof() below deliberately names an obsolete binary-compatibility
// shim; suppress the warning the tests exist to exercise.
#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace Basalt.Forms.Tests.Legacy;

/// <summary>
/// The binary-compatibility shims in src/Basalt.Forms/Legacy exist only so an
/// old assembly loads: every constructor must throw
/// <see cref="PlatformNotSupportedException"/> (never succeed), and every
/// public type in the family must carry Microsoft's exact
/// [Obsolete(DiagnosticId = "WFDEV006")] attribute.
/// </summary>
public class LegacyShimTests
{
    /// <summary>
    /// Every concrete shim type that has at least one public constructor.
    /// Abstract types (DataGridColumnStyle, Menu) and types whose only
    /// constructor is non-public (GridColumnStylesCollection,
    /// GridTableStylesCollection, GridTablesFactory) are left out: Microsoft
    /// never lets anyone write `new` for those either.
    /// </summary>
    /// <summary>A protected nested type: only reflection can name it from outside DataGridColumnStyle.</summary>
    private static readonly Type CompModSwitchesType =
        typeof(DataGridColumnStyle).GetNestedType("CompModSwitches", BindingFlags.NonPublic | BindingFlags.Public)!;

    public static TheoryData<Type> ShimTypesWithPublicConstructors() =>
    [
        typeof(DataGrid),
        typeof(DataGridBoolColumn),
        typeof(DataGridCell),
        typeof(DataGridPreferredColumnWidthTypeConverter),
        typeof(DataGridTableStyle),
        typeof(DataGridTextBox),
        typeof(DataGridTextBoxColumn),
        typeof(Menu.MenuItemCollection),
        typeof(MainMenu),
        typeof(MenuItem),
        typeof(ContextMenu),
        typeof(StatusBar),
        typeof(StatusBar.StatusBarPanelCollection),
        typeof(StatusBarPanel),
        typeof(ToolBar),
        typeof(ToolBar.ToolBarButtonCollection),
        typeof(ToolBarButton),
        CompModSwitchesType,
    ];

    /// <summary>Every public type this worktree added under src/Basalt.Forms/Legacy.</summary>
    public static TheoryData<Type> ShimTypes() =>
    [
        typeof(DataGrid),
        typeof(DataGrid.HitTestInfo),
        typeof(DataGrid.HitTestType),
        typeof(DataGridBoolColumn),
        typeof(DataGridCell),
        typeof(DataGridColumnStyle),
        typeof(DataGridPreferredColumnWidthTypeConverter),
        typeof(DataGridTableStyle),
        typeof(DataGridTextBox),
        typeof(DataGridTextBoxColumn),
        typeof(GridColumnStylesCollection),
        typeof(GridTableStylesCollection),
        typeof(GridTablesFactory),
        typeof(IDataGridEditingService),
        typeof(Menu),
        typeof(Menu.MenuItemCollection),
        typeof(MainMenu),
        typeof(MenuItem),
        typeof(ContextMenu),
        typeof(StatusBar),
        typeof(StatusBar.StatusBarPanelCollection),
        typeof(StatusBarPanel),
        typeof(StatusBarDrawItemEventArgs),
        typeof(StatusBarDrawItemEventHandler),
        typeof(StatusBarPanelClickEventArgs),
        typeof(StatusBarPanelClickEventHandler),
        typeof(ToolBar),
        typeof(ToolBar.ToolBarButtonCollection),
        typeof(ToolBarButton),
        typeof(ToolBarButtonClickEventArgs),
        typeof(ToolBarButtonClickEventHandler),
        CompModSwitchesType,
    ];

    [Theory]
    [MemberData(nameof(ShimTypesWithPublicConstructors))]
    public void Every_public_constructor_throws_PlatformNotSupportedException(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(constructors);

        foreach (var constructor in constructors)
        {
            var arguments = constructor.GetParameters()
                .Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                .ToArray();

            var thrown = Assert.Throws<PlatformNotSupportedException>(() =>
            {
                try
                {
                    constructor.Invoke(arguments);
                }
                catch (TargetInvocationException invocationFailure) when (invocationFailure.InnerException is not null)
                {
                    throw invocationFailure.InnerException;
                }
            });

            Assert.NotNull(thrown);
        }
    }

    [Theory]
    [MemberData(nameof(ShimTypes))]
    public void Every_shim_type_carries_the_WFDEV006_obsoletion(Type type)
    {
        var obsolete = type.GetCustomAttribute<ObsoleteAttribute>();

        Assert.True(obsolete is not null, $"{type} has no [Obsolete] attribute.");
        Assert.Equal("WFDEV006", obsolete!.DiagnosticId);
        Assert.Equal("https://aka.ms/winforms-warnings/{0}", obsolete.UrlFormat);
        Assert.False(obsolete.IsError);
    }
}
