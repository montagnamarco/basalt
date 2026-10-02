using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Web;

/// <summary>
/// AxHost hosts a real ActiveX control through COM on Windows; Basalt has
/// none. The boundary: building one and converting pure data (OLE colours,
/// OLE Automation dates) works, but anything that would create or reach
/// the COM object refuses.
/// </summary>
public class AxHostTests
{
    /// <summary>Exposes AxHost's protected static conversions and lets a test construct a concrete instance.</summary>
    private sealed class TestAxHost() : AxHost("00000000-0000-0000-0000-000000000000")
    {
        public static uint OleColorFromColor(Color color) => GetOleColorFromColor(color);

        public static Color ColorFromOleColor(uint color) => GetColorFromOleColor(color);

        public static double OADateFromTime(DateTime time) => GetOADateFromTime(time);

        public static DateTime TimeFromOADate(double date) => GetTimeFromOADate(date);
    }

    [Fact]
    public void The_constructor_does_not_throw()
    {
        using var host = new TestAxHost();

        Assert.NotNull(host);
    }

    [Fact]
    public void CreateControl_throws_because_there_is_no_ActiveX_object_to_create()
    {
        using var host = new TestAxHost();

        Assert.Throws<PlatformNotSupportedException>(host.CreateControl);
    }

    [Fact]
    public void GetOcx_throws_because_there_is_no_ActiveX_object_to_return()
    {
        using var host = new TestAxHost();

        Assert.Throws<PlatformNotSupportedException>(() => host.GetOcx());
    }

    [Fact]
    public void DoVerb_throws_because_there_is_no_ActiveX_object_to_send_a_verb_to()
    {
        using var host = new TestAxHost();

        Assert.Throws<PlatformNotSupportedException>(() => host.DoVerb(0));
    }

    [Fact]
    public void InvokeEditMode_throws_because_there_is_no_ActiveX_object_to_activate()
    {
        using var host = new TestAxHost();

        Assert.Throws<PlatformNotSupportedException>(host.InvokeEditMode);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(12, 34, 56)]
    [InlineData(255, 255, 255)]
    public void GetOleColorFromColor_and_GetColorFromOleColor_round_trip(byte r, byte g, byte b)
    {
        var color = Color.FromArgb(r, g, b);

        var oleColor = TestAxHost.OleColorFromColor(color);
        var roundTripped = TestAxHost.ColorFromOleColor(oleColor);

        Assert.Equal(color.R, roundTripped.R);
        Assert.Equal(color.G, roundTripped.G);
        Assert.Equal(color.B, roundTripped.B);
    }

    [Fact]
    public void GetOleColorFromColor_packs_like_a_COLORREF_not_like_ARGB()
    {
        // 0x00BBGGRR, the same byte order OLE_COLOR and COLORREF use - the
        // opposite of System.Drawing's 0x00RRGGBB. A wrong byte order would
        // still "round trip" above but swap red and blue here.
        var color = Color.FromArgb(0x11, 0x22, 0x33);

        var oleColor = TestAxHost.OleColorFromColor(color);

        Assert.Equal(0x00332211u, oleColor);
    }

    [Theory]
    [MemberData(nameof(OaDates))]
    public void GetOADateFromTime_and_GetTimeFromOADate_round_trip(DateTime time)
    {
        var oaDate = TestAxHost.OADateFromTime(time);
        var roundTripped = TestAxHost.TimeFromOADate(oaDate);

        Assert.Equal(time, roundTripped);
    }

    public static TheoryData<DateTime> OaDates() =>
    [
        new DateTime(1899, 12, 30),
        new DateTime(2000, 1, 1),
        new DateTime(2024, 6, 15, 13, 45, 30),
    ];
}
