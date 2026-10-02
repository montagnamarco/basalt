using System.Windows.Forms;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>Wrapping a handle, making one of its own, and finding a window again from either.</summary>
public class NativeWindowTests
{
    [Fact]
    public void CreateHandle_can_be_found_again_with_FromHandle()
    {
        var window = new NativeWindow();
        window.CreateHandle(new CreateParams());

        Assert.NotEqual(IntPtr.Zero, window.Handle);
        Assert.Same(window, NativeWindow.FromHandle(window.Handle));

        window.DestroyHandle();
    }

    [Fact]
    public void DestroyHandle_forgets_it_so_FromHandle_answers_null()
    {
        var window = new NativeWindow();
        window.CreateHandle(new CreateParams());
        var handle = window.Handle;

        window.DestroyHandle();

        Assert.Equal(IntPtr.Zero, window.Handle);
        Assert.Null(NativeWindow.FromHandle(handle));
    }

    [Fact]
    public void AssignHandle_lets_FromHandle_find_the_window_for_someone_elses_handle()
    {
        var owner = new NativeWindow();
        owner.CreateHandle(new CreateParams());

        var subclass = new NativeWindow();
        subclass.AssignHandle(owner.Handle);

        Assert.Same(subclass, NativeWindow.FromHandle(owner.Handle));

        subclass.ReleaseHandle();
        owner.DestroyHandle();
    }

    [Fact]
    public void ReleaseHandle_stops_tracking_it_without_needing_to_destroy_it()
    {
        var window = new NativeWindow();
        window.CreateHandle(new CreateParams());
        var handle = window.Handle;

        window.ReleaseHandle();

        Assert.Equal(IntPtr.Zero, window.Handle);
        Assert.Null(NativeWindow.FromHandle(handle));
    }

    [Fact]
    public void FromHandle_of_a_handle_nobody_registered_is_null()
    {
        Assert.Null(NativeWindow.FromHandle(new IntPtr(0x7FFFFFFF)));
    }
}
