namespace System.Drawing;

/// <summary>Marks a component's toolbox bitmap as a resource of this same assembly, named after its satellite-free default.</summary>
public class BitmapSuffixInSameAssemblyAttribute : Attribute
{
    public BitmapSuffixInSameAssemblyAttribute()
    {
    }
}

/// <summary>Marks a component's toolbox bitmap as a resource of a satellite (localised) assembly instead of this one.</summary>
public class BitmapSuffixInSatelliteAssemblyAttribute : Attribute
{
    public BitmapSuffixInSatelliteAssemblyAttribute()
    {
    }
}
