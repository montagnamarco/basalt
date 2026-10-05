using System.Drawing;
using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Microsoft.CodeAnalysis.VisualBasic;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsDesignerValuesTests
{
    [Fact]
    public void EvaluatesStandardLayoutAndNumericCastExpressions()
    {
        Assert.Equal(new Point(-5, 20), Value("New Global.System.Drawing.Point((-5), CInt(20))", typeof(Point)));
        Assert.Equal(new Size(90, 30), Value("New System.Drawing.Size(90, 30)", typeof(Size)));
        Assert.Equal(new SizeF(6f, 13f), Value("New System.Drawing.SizeF(6.0!, 13.0!)", typeof(SizeF)));
        Assert.Equal(new PointF(1.25f, -2.5f), Value("New System.Drawing.PointF(CSng(1.25), -2.5!)", typeof(PointF)));
        Assert.Equal(new Forms.Padding(4, 2, 4, 2), Value("New System.Windows.Forms.Padding(4, 2, 4, 2)", typeof(Forms.Padding)));
        Assert.Equal((byte)1, Value("CType(1, Byte)", typeof(byte)));
    }

    [Fact]
    public void EvaluatesDesignerAnchorAndFontStyleFlagsWithoutCompilingThem()
    {
        var expression = "CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)";
        Assert.Equal(Forms.AnchorStyles.Top | Forms.AnchorStyles.Right, Value(expression, typeof(Forms.AnchorStyles)));
        Assert.Equal(FontStyle.Bold | FontStyle.Italic,
            Value("(Global.System.Drawing.FontStyle.Bold Or Global.System.Drawing.FontStyle.Italic)", typeof(FontStyle)));
        var serialized = WinFormsDesignerValues.Serialize(Forms.AnchorStyles.Top | Forms.AnchorStyles.Left);
        Assert.Equal(Forms.AnchorStyles.Top | Forms.AnchorStyles.Left, Value(serialized, typeof(Forms.AnchorStyles)));
        Assert.Equal((Forms.AnchorStyles)31,
            Value(WinFormsDesignerValues.Serialize((Forms.AnchorStyles)31), typeof(Forms.AnchorStyles)));
    }

    [AvaloniaFact]
    public void EvaluatesRealDesignerFontConstructorAndRoundTripsItsProperties()
    {
        using var font = Assert.IsType<Font>(Value("New System.Drawing.Font(\"Segoe UI\", 9.0!, CType((System.Drawing.FontStyle.Bold Or System.Drawing.FontStyle.Italic), System.Drawing.FontStyle), System.Drawing.GraphicsUnit.Point, CType(0, Byte), False)", typeof(Font)));
        Assert.Equal("Segoe UI", font.OriginalFontName);
        Assert.Equal(9f, font.Size);
        Assert.Equal(FontStyle.Bold | FontStyle.Italic, font.Style);
        Assert.Equal(GraphicsUnit.Point, font.Unit);
        Assert.Equal(0, font.GdiCharSet);
        using var roundTrip = Assert.IsType<Font>(Value(WinFormsDesignerValues.Serialize(font), typeof(Font)));
        Assert.Equal(font.OriginalFontName, roundTrip.OriginalFontName);
        Assert.Equal(font.Size, roundTrip.Size);
        Assert.Equal(font.Style, roundTrip.Style);
        Assert.Equal(font.GdiCharSet, roundTrip.GdiCharSet);
        using var unitFont = Assert.IsType<Font>(Value("New System.Drawing.Font(\"Tahoma\", 12.0!, CType(2, System.Drawing.GraphicsUnit))", typeof(Font)));
        Assert.Equal(GraphicsUnit.Pixel, unitFont.Unit);
    }

    [AvaloniaFact]
    public void SystemFontPreviewReturnsAnOwnedCopy()
    {
        using var font = Assert.IsType<Font>(Value("System.Drawing.SystemFonts.DefaultFont", typeof(Font)));
        Assert.False(font.IsSystemFont);
        Assert.True(font.Size > 0);
    }

    [Fact]
    public void EvaluatesAndRoundTripsKnownSystemAndArgbColors()
    {
        Assert.Equal(Color.Red, Value("System.Drawing.Color.Red", typeof(Color)));
        Assert.Equal(SystemColors.Control, Value("Global.System.Drawing.SystemColors.Control", typeof(Color)));
        var color = Color.FromArgb(128, 20, 40, 60);
        Assert.Equal(color, Value("System.Drawing.Color.FromArgb(CType(128, Integer), 20, 40, 60)", typeof(Color)));
        Assert.Equal(Color.FromArgb(255, 20, 40, 60), Value("System.Drawing.Color.FromArgb(20, 40, 60)", typeof(Color)));
        Assert.Equal(Color.FromArgb(80, Color.Red), Value("System.Drawing.Color.FromArgb(80, System.Drawing.Color.Red)", typeof(Color)));
        Assert.Equal(color, Value(WinFormsDesignerValues.Serialize(color), typeof(Color)));
        Assert.Equal(SystemColors.Control, Value(WinFormsDesignerValues.Serialize(SystemColors.Control), typeof(Color)));
    }

    [Theory]
    [InlineData("DangerousCall()", typeof(string))]
    [InlineData("New User.Drawing.Point(1, 2)", typeof(Point))]
    [InlineData("New User.Padding(4)", typeof(Forms.Padding))]
    [InlineData("New System.Drawing.Point(DangerousCall(), 2)", typeof(Point))]
    [InlineData("New System.Drawing.Point(1, 2) With {.X = 20}", typeof(Point))]
    [InlineData("CType(123, User.Integer)", typeof(int))]
    [InlineData("User.Color.FromArgb(20, 40, 60)", typeof(Color))]
    public void RejectsUserCodeAndNonWhitelistedTypes(string expression, Type type)
    {
        Assert.Throws<NotSupportedException>(() => Value(expression, type));
    }

    [Fact]
    public void SerializationUsesInvariantFloatingPointSyntax()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            var value = new SizeF(6.25f, 13.5f);
            var serialized = WinFormsDesignerValues.Serialize(value);
            Assert.Contains("6.25!", serialized);
            Assert.Equal(value, Value(serialized, typeof(SizeF)));
            Assert.Equal("\"A \"\"quoted\"\" label\"", WinFormsDesignerValues.Serialize("A \"quoted\" label"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    private static object? Value(string expression, Type type)
    {
        var syntax = SyntaxFactory.ParseExpression(expression);
        Assert.False(syntax.ContainsDiagnostics);
        return WinFormsDesignerValues.Evaluate(syntax, type);
    }
}
