namespace System.Drawing;

/// <summary>A surface that can hand out a Windows device context.</summary>
public interface IDeviceContext : IDisposable
{
    IntPtr GetHdc();

    void ReleaseHdc();
}
