using System.Drawing;
using System.Globalization;
using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>Evaluates a small set of designer constants without executing application expressions.</summary>
internal static class WinFormsDesignerValues
{
    public static object? Evaluate(ExpressionSyntax syntax, Type expectedType)
    {
        var type = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        if (syntax is ParenthesizedExpressionSyntax parentheses)
            return Evaluate(parentheses.Expression, type);
        if (syntax is CTypeExpressionSyntax conversion)
            return EvaluateCast(conversion.Expression, conversion.Type.ToString(), type);
        if (syntax is DirectCastExpressionSyntax direct)
            return EvaluateCast(direct.Expression, direct.Type.ToString(), type);
        if (syntax is PredefinedCastExpressionSyntax predefined)
        {
            var castType = predefined.Keyword.Kind() switch
            {
                SyntaxKind.CByteKeyword => typeof(byte),
                SyntaxKind.CSByteKeyword => typeof(sbyte),
                SyntaxKind.CShortKeyword => typeof(short),
                SyntaxKind.CUShortKeyword => typeof(ushort),
                SyntaxKind.CIntKeyword => typeof(int),
                SyntaxKind.CUIntKeyword => typeof(uint),
                SyntaxKind.CLngKeyword => typeof(long),
                SyntaxKind.CULngKeyword => typeof(ulong),
                SyntaxKind.CSngKeyword => typeof(float),
                SyntaxKind.CDblKeyword => typeof(double),
                SyntaxKind.CDecKeyword => typeof(decimal),
                SyntaxKind.CBoolKeyword => typeof(bool),
                SyntaxKind.CStrKeyword => typeof(string),
                _ => throw Unsupported(),
            };
            return ConvertConstant(Evaluate(predefined.Expression, castType), type);
        }
        if (syntax is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(SyntaxKind.NothingLiteralExpression)) return null;
            if (literal.IsKind(SyntaxKind.TrueLiteralExpression)) return ConvertConstant(true, type);
            if (literal.IsKind(SyntaxKind.FalseLiteralExpression)) return ConvertConstant(false, type);
            return ConvertConstant(literal.Token.Value, type);
        }
        if (syntax is UnaryExpressionSyntax unary &&
            (unary.IsKind(SyntaxKind.UnaryMinusExpression) || unary.IsKind(SyntaxKind.UnaryPlusExpression)))
        {
            if (type == typeof(float) || type == typeof(double))
            {
                var floating = (double)Evaluate(unary.Operand, typeof(double))!;
                return ConvertConstant(unary.IsKind(SyntaxKind.UnaryMinusExpression) ? -floating : floating, type);
            }
            var number = (decimal)Evaluate(unary.Operand, typeof(decimal))!;
            return ConvertConstant(unary.IsKind(SyntaxKind.UnaryMinusExpression) ? -number : number, type);
        }
        // Visual Basic's & joins two constant strings, so "A" & "B" previews as "AB". Nothing joins as an empty string.
        if (type == typeof(string) && syntax is BinaryExpressionSyntax { } concatenation
            && concatenation.IsKind(SyntaxKind.ConcatenateExpression))
        {
            return Convert.ToString(Evaluate(concatenation.Left, typeof(string)), CultureInfo.InvariantCulture)
                + Convert.ToString(Evaluate(concatenation.Right, typeof(string)), CultureInfo.InvariantCulture);
        }
        if (type.IsEnum && syntax is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.OrExpression))
        {
            var left = Convert.ToInt64(Evaluate(binary.Left, type), CultureInfo.InvariantCulture);
            var right = Convert.ToInt64(Evaluate(binary.Right, type), CultureInfo.InvariantCulture);
            return Enum.ToObject(type, left | right);
        }
        if (syntax is ObjectCreationExpressionSyntax creation)
            return Construct(creation, type);
        if (syntax is InvocationExpressionSyntax invocation && type == typeof(Color))
            return ColorInvocation(invocation);
        if (syntax is MemberAccessExpressionSyntax member)
        {
            var owner = Normalize(member.Expression.ToString());
            var name = member.Name.Identifier.ValueText;
            if (type.IsEnum && owner.Equals(type.FullName, StringComparison.OrdinalIgnoreCase) &&
                Enum.GetNames(type).Contains(name, StringComparer.OrdinalIgnoreCase))
                return Enum.Parse(type, name, true);
            if (type == typeof(Color) &&
                (owner.Equals("System.Drawing.Color", StringComparison.OrdinalIgnoreCase) ||
                 owner.Equals("System.Drawing.SystemColors", StringComparison.OrdinalIgnoreCase)))
            {
                var ownerType = owner.Equals("System.Drawing.SystemColors", StringComparison.OrdinalIgnoreCase)
                    ? typeof(SystemColors) : typeof(Color);
                var property = ownerType.GetProperties().FirstOrDefault(candidate =>
                    candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && candidate.PropertyType == typeof(Color));
                if (property is not null) return property.GetValue(null);
            }
            if (type == typeof(Font) && owner.Equals("System.Drawing.SystemFonts", StringComparison.OrdinalIgnoreCase))
            {
                var knownName = typeof(SystemFonts).GetProperties().FirstOrDefault(property =>
                    property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.PropertyType == typeof(Font))?.Name;
                var font = knownName is null ? null : SystemFonts.GetFontByName(knownName);
                if (font is not null) return new Font(font, font.Style);
            }
        }
        throw Unsupported();
    }

    private static object? EvaluateCast(ExpressionSyntax expression, string castName, Type expectedType)
    {
        var name = Normalize(castName);
        var castType = PrimitiveTypes.GetValueOrDefault(name);
        if (castType is null && name.Equals(expectedType.FullName, StringComparison.OrdinalIgnoreCase))
            castType = expectedType;
        if (castType is null) throw Unsupported();
        return ConvertConstant(Evaluate(expression, castType), expectedType);
    }

    private static object? ConvertConstant(object? value, Type type)
    {
        if (value is float single && !float.IsFinite(single) || value is double floating && !double.IsFinite(floating))
            throw Unsupported();
        if (value is null || type.IsInstanceOfType(value)) return value;
        if (type.IsEnum)
            return Enum.ToObject(type, Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture)!);
        if (PrimitiveTypes.ContainsValue(type))
        {
            var converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            if (converted is float number && !float.IsFinite(number) || converted is double real && !double.IsFinite(real))
                throw Unsupported();
            return converted;
        }
        throw Unsupported();
    }

    private static object Construct(ObjectCreationExpressionSyntax creation, Type type)
    {
        if (!Normalize(creation.Type.ToString()).Equals(type.FullName, StringComparison.OrdinalIgnoreCase)
            || creation.Initializer is not null) throw Unsupported();
        var args = Arguments(creation.ArgumentList);
        int Integer(int index) => (int)Evaluate(args[index], typeof(int))!;
        float Single(int index) => (float)Evaluate(args[index], typeof(float))!;
        if (type == typeof(Point) && args.Length == 2) return new Point(Integer(0), Integer(1));
        if (type == typeof(Size) && args.Length == 2) return new Size(Integer(0), Integer(1));
        if (type == typeof(PointF) && args.Length == 2) return new PointF(Single(0), Single(1));
        if (type == typeof(SizeF) && args.Length == 2) return new SizeF(Single(0), Single(1));
        if (type == typeof(Forms.Padding) && args.Length is 1 or 4)
            return args.Length == 1 ? new Forms.Padding(Integer(0))
                : new Forms.Padding(Integer(0), Integer(1), Integer(2), Integer(3));
        if (type == typeof(Font) && args.Length is >= 2 and <= 6)
        {
            var family = (string)Evaluate(args[0], typeof(string))!;
            var size = Single(1);
            var thirdIsUnit = args.Length == 3 && TargetsType(args[2], typeof(GraphicsUnit));
            var style = args.Length < 3 || thirdIsUnit ? FontStyle.Regular : (FontStyle)Evaluate(args[2], typeof(FontStyle))!;
            var unit = thirdIsUnit ? (GraphicsUnit)Evaluate(args[2], typeof(GraphicsUnit))!
                : args.Length < 4 ? GraphicsUnit.Point : (GraphicsUnit)Evaluate(args[3], typeof(GraphicsUnit))!;
            var charset = args.Length < 5 ? (byte)1 : (byte)Evaluate(args[4], typeof(byte))!;
            var vertical = args.Length >= 6 && (bool)Evaluate(args[5], typeof(bool))!;
            return new Font(family, size, style, unit, charset, vertical);
        }
        throw Unsupported();
    }

    private static bool TargetsType(ExpressionSyntax syntax, Type type) => syntax switch
    {
        ParenthesizedExpressionSyntax parentheses => TargetsType(parentheses.Expression, type),
        CTypeExpressionSyntax cast => Normalize(cast.Type.ToString()).Equals(type.FullName, StringComparison.OrdinalIgnoreCase),
        DirectCastExpressionSyntax cast => Normalize(cast.Type.ToString()).Equals(type.FullName, StringComparison.OrdinalIgnoreCase),
        MemberAccessExpressionSyntax member => Normalize(member.Expression.ToString()).Equals(type.FullName, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static Color ColorInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            !Normalize(member.Expression.ToString()).Equals("System.Drawing.Color", StringComparison.OrdinalIgnoreCase) ||
            !member.Name.Identifier.ValueText.Equals("FromArgb", StringComparison.OrdinalIgnoreCase)) throw Unsupported();
        var args = Arguments(invocation.ArgumentList);
        int Integer(int index) => (int)Evaluate(args[index], typeof(int))!;
        return args.Length switch
        {
            1 => Color.FromArgb(Integer(0)),
            2 => Color.FromArgb(Integer(0), (Color)Evaluate(args[1], typeof(Color))!),
            3 => Color.FromArgb(Integer(0), Integer(1), Integer(2)),
            4 => Color.FromArgb(Integer(0), Integer(1), Integer(2), Integer(3)),
            _ => throw Unsupported(),
        };
    }

    private static ExpressionSyntax[] Arguments(ArgumentListSyntax? argumentList)
    {
        if (argumentList is null) return [];
        if (argumentList.Arguments.Any(argument => argument is not SimpleArgumentSyntax { NameColonEquals: null }))
            throw Unsupported();
        return argumentList.Arguments.OfType<SimpleArgumentSyntax>().Select(argument => argument.Expression).ToArray();
    }

    public static string Serialize(object? value) => value switch
    {
        null => "Nothing",
        string text => Quote(text),
        bool boolean => boolean ? "True" : "False",
        Point point => $"New System.Drawing.Point({point.X}, {point.Y})",
        Size size => $"New System.Drawing.Size({size.Width}, {size.Height})",
        PointF point => $"New System.Drawing.PointF({Float(point.X)}, {Float(point.Y)})",
        SizeF size => $"New System.Drawing.SizeF({Float(size.Width)}, {Float(size.Height)})",
        Forms.Padding padding => $"New System.Windows.Forms.Padding({padding.Left}, {padding.Top}, {padding.Right}, {padding.Bottom})",
        Font font => $"New System.Drawing.Font({Quote(font.OriginalFontName ?? font.Name)}, {Float(font.Size)}, {Serialize(font.Style)}, {Serialize(font.Unit)}, CType({font.GdiCharSet}, Byte), {Serialize(font.GdiVerticalFont)})",
        Enum enumeration => SerializeEnum(enumeration),
        Color color when color.IsSystemColor => $"System.Drawing.SystemColors.{color.Name}",
        Color color when color.IsNamedColor => $"System.Drawing.Color.{color.Name}",
        Color color => $"System.Drawing.Color.FromArgb({color.A}, {color.R}, {color.G}, {color.B})",
        float number when float.IsFinite(number) => Float(number),
        double number when double.IsFinite(number) => number.ToString("R", CultureInfo.InvariantCulture) + "R",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "D",
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => throw Unsupported(),
    };

    private static string SerializeEnum(Enum value)
    {
        var typeName = "Global." + value.GetType().FullName;
        var names = value.ToString().Split(", ", StringSplitOptions.None);
        var declared = Enum.GetNames(value.GetType());
        return names.All(name => declared.Contains(name, StringComparer.Ordinal))
            ? string.Join(" Or ", names.Select(name => typeName + "." + name))
            : $"CType({value.ToString("D")}, {typeName})";
    }

    private static string Float(float value) => float.IsFinite(value)
        ? value.ToString("R", CultureInfo.InvariantCulture) + "!" : throw Unsupported();
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// The font a font dialog hands back, as the designer writes it. The dialog keeps the character set of the font it was
    /// given, and a font made without one has DEFAULT_CHARSET (1), which no installed font reports. Visual Studio writes the
    /// ANSI character set (0) that the font resolved to, so an edited font with DEFAULT_CHARSET becomes one with ANSI_CHARSET.
    /// Any other character set is kept. A font that is not replaced is returned as it is.
    /// </summary>
    public static Font AsEditedFont(Font edited) => edited.GdiCharSet == DefaultCharSet
        ? new Font(edited.OriginalFontName ?? edited.Name, edited.Size, edited.Style, edited.Unit, AnsiCharSet, edited.GdiVerticalFont)
        : edited;

    private const byte DefaultCharSet = 1;
    private const byte AnsiCharSet = 0;
    private static string Normalize(string name) => name.StartsWith("Global.", StringComparison.OrdinalIgnoreCase) ? name[7..] : name;
    private static NotSupportedException Unsupported() => new(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));

    private static readonly Dictionary<string, Type> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Byte"] = typeof(byte), ["System.Byte"] = typeof(byte),
        ["SByte"] = typeof(sbyte), ["System.SByte"] = typeof(sbyte),
        ["Short"] = typeof(short), ["System.Int16"] = typeof(short),
        ["UShort"] = typeof(ushort), ["System.UInt16"] = typeof(ushort),
        ["Integer"] = typeof(int), ["System.Int32"] = typeof(int),
        ["UInteger"] = typeof(uint), ["System.UInt32"] = typeof(uint),
        ["Long"] = typeof(long), ["System.Int64"] = typeof(long),
        ["ULong"] = typeof(ulong), ["System.UInt64"] = typeof(ulong),
        ["Single"] = typeof(float), ["System.Single"] = typeof(float),
        ["Double"] = typeof(double), ["System.Double"] = typeof(double),
        ["Decimal"] = typeof(decimal), ["System.Decimal"] = typeof(decimal),
        ["Boolean"] = typeof(bool), ["System.Boolean"] = typeof(bool),
        ["String"] = typeof(string), ["System.String"] = typeof(string),
    };
}
