namespace System.Windows.Forms;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RelatedImageListAttribute(string? relatedImageList) : Attribute
{
    public string? RelatedImageList { get; } = relatedImageList;
}
