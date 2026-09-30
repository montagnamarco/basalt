namespace Basalt.Drawing.Tests.Api;

/// <summary>
/// The formatter is what decides whether two surfaces are the same, so each
/// difference a compiled caller can see has to change the text. Microsoft's
/// System.Drawing.Common has no ref-returning member, for one, so nothing else
/// would notice that distinction being lost.
/// </summary>
public class ApiSurfaceFormatTests
{
    private static readonly SortedSet<string> Lines = ApiSurface.Describe(typeof(SurfaceSample).Assembly);

    [Theory]
    [InlineData("T abstract class Basalt.Drawing.Tests.Api.SurfaceSample")]
    [InlineData("I Basalt.Drawing.Tests.Api.SurfaceSample : System.IDisposable")]
    [InlineData("F public Basalt.Drawing.Tests.Api.SurfaceSample::Constant : System.Int32 const")]
    [InlineData("F public Basalt.Drawing.Tests.Api.SurfaceSample::Shared : System.Int32 static readonly")]
    [InlineData("F protected Basalt.Drawing.Tests.Api.SurfaceSample::Inherited : System.Int32")]
    [InlineData("M protected Basalt.Drawing.Tests.Api.SurfaceSample::.ctor()")]
    [InlineData("M public Basalt.Drawing.Tests.Api.SurfaceSample::Passing(System.Int32, ref System.Int32, out System.Int32, in System.Int32) -> System.Void")]
    [InlineData("M public Basalt.Drawing.Tests.Api.SurfaceSample::ByReference() -> ref System.Int32")]
    [InlineData("M protected Basalt.Drawing.Tests.Api.SurfaceSample::Both() -> System.Void")]
    [InlineData("M protected Basalt.Drawing.Tests.Api.SurfaceSample::Overridable() -> System.Void virtual")]
    [InlineData("M public Basalt.Drawing.Tests.Api.SurfaceSample::Required() -> System.Void abstract")]
    [InlineData("M public Basalt.Drawing.Tests.Api.SurfaceSample::SharedMethod() -> System.Void static")]
    public void A_difference_a_caller_can_see_is_in_the_text(string expected) => Assert.Contains(expected, Lines);

    [Fact]
    public void What_a_caller_cannot_reach_is_left_out()
    {
        Assert.DoesNotContain(Lines, line => line.Contains("Hidden"));
    }
}

public abstract class SurfaceSample : IDisposable
{
    public const int Constant = 1;
    public static readonly int Shared = 2;
    protected int Inherited;
    internal int HiddenField;
    private int _value;

    protected SurfaceSample()
    {
    }

    public void Passing(int byValue, ref int byReference, out int result, in int readOnly) => result = byValue + byReference + readOnly;

    public ref int ByReference() => ref _value;

    protected internal void Both()
    {
    }

    protected virtual void Overridable()
    {
    }

    public abstract void Required();

    public static void SharedMethod()
    {
    }

    internal void HiddenMethod()
    {
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
