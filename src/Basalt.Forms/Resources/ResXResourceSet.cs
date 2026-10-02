namespace System.Resources;

/// <summary>A ResourceSet backed by a .resx file or stream instead of a compiled .resources blob.</summary>
public class ResXResourceSet : ResourceSet
{
    public ResXResourceSet(Stream stream) : base(new ResXResourceReader(stream))
    {
        ReadResources();
    }

    public ResXResourceSet(string fileName) : base(new ResXResourceReader(fileName))
    {
        ReadResources();
    }

    public override Type GetDefaultReader() => typeof(ResXResourceReader);

    public override Type GetDefaultWriter() => typeof(ResXResourceWriter);
}
