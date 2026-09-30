namespace System.Drawing.Imaging;

/// <summary>The settings passed to an encoder in one <see cref="Image.Save(Stream, ImageCodecInfo, EncoderParameters)"/> call.</summary>
public sealed class EncoderParameters : IDisposable
{
    public EncoderParameters()
        : this(1)
    {
    }

    public EncoderParameters(int count) => Param = new EncoderParameter[count];

    public EncoderParameter[] Param { get; set; }

    public void Dispose()
    {
        foreach (var parameter in Param) parameter?.Dispose();

        GC.SuppressFinalize(this);
    }
}
