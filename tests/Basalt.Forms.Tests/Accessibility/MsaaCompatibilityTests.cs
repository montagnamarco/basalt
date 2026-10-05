using System.Drawing;
using System.Runtime.InteropServices;
using Accessibility;
using System.Windows.Forms;
using Windows.Win32.System.Com;

namespace Basalt.Forms.Tests.Accessibility;

public class MsaaCompatibilityTests
{
    [Fact]
    public void Accessory_assembly_exposes_named_indexed_properties_and_the_MSAA_identity()
    {
        var interfaceType = typeof(IAccessible);

        Assert.Equal("Accessibility", interfaceType.Assembly.GetName().Name);
        Assert.Equal(new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), interfaceType.GUID);
        Assert.Single(interfaceType.GetProperty("accName")!.GetIndexParameters());
        Assert.Equal(-5003, interfaceType.GetProperty("accName")!.GetCustomAttributes(typeof(DispIdAttribute), false)
            .Cast<DispIdAttribute>().Single().Value);

        // The two setters follow all 19 read/action slots in Microsoft's MSAA vtable.
        Assert.Equal(
            ["get_accParent", "get_accChildCount", "get_accChild", "get_accName", "get_accValue",
                "get_accDescription", "get_accRole", "get_accState", "get_accHelp", "get_accHelpTopic",
                "get_accKeyboardShortcut", "get_accFocus", "get_accSelection", "get_accDefaultAction",
                "accSelect", "accLocation", "accNavigate", "accHitTest", "accDoDefaultAction",
                "set_accName", "set_accValue"],
            interfaceType.GetMethods().OrderBy(method => method.MetadataToken).Select(method => method.Name));

        var nameGetter = interfaceType.GetMethod("get_accName")!;
        Assert.True(nameGetter.GetParameters()[0].IsOptional);
        Assert.Equal(UnmanagedType.Struct, nameGetter.GetParameters()[0].GetCustomAttributes(typeof(MarshalAsAttribute), false)
            .Cast<MarshalAsAttribute>().Single().Value);
        Assert.Equal(UnmanagedType.BStr, nameGetter.ReturnParameter.GetCustomAttributes(typeof(MarshalAsAttribute), false)
            .Cast<MarshalAsAttribute>().Single().Value);
    }

    [Fact]
    public void Managed_MSAA_interface_uses_accessible_children_names_and_bounds()
    {
        var parent = new SampleAccessible("parent", new Rectangle(0, 0, 100, 80));
        var child = new SampleAccessible("child", new Rectangle(12, 9, 30, 20));
        parent.Child = child;
        var accessible = (IAccessible)parent;

        Assert.Equal(1, accessible.accChildCount);
        Assert.Same(child, accessible.get_accChild(1));
        Assert.Equal("child", accessible.get_accName(1));
        accessible.set_accName(1, "renamed");
        Assert.Equal("renamed", child.Name);

        int x = 0, y = 0, width = 0, height = 0;
        accessible.accLocation(out x, out y, out width, out height, 1);
        Assert.Equal(new Rectangle(12, 9, 30, 20), new Rectangle(x, y, width, height));
    }

    [Fact]
    public void Dispatch_identity_exposes_full_vtable_and_reports_unsupported_native_invocation()
    {
        var accessible = new AccessibleObject();
        var dispatch = (IDispatch.Interface)accessible;
        var iid = Guid.Empty;

        Assert.Equal(new Guid("00020400-0000-0000-C000-000000000046"), typeof(IDispatch.Interface).GUID);
        Assert.Equal(0, dispatch.GetTypeInfoCount(out var count));
        Assert.Equal((uint)0, count);
        Assert.Equal(unchecked((int)0x80004001), dispatch.Invoke(-5003, ref iid, 0, 2,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
    }

    private sealed class SampleAccessible(string name, Rectangle bounds) : AccessibleObject
    {
        public SampleAccessible? Child { get; set; }

        public override string? Name { get; set; } = name;

        public override Rectangle Bounds => bounds;

        public override int GetChildCount() => Child is null ? 0 : 1;

        public override AccessibleObject GetChild(int index) => index == 0 && Child is not null
            ? Child : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
