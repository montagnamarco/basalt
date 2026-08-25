using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Basalt.Core.Model;
using Basalt.Extensibility;

namespace Basalt.Shell.Controls;

/// <summary>
/// Icons shown beside completion entries, one per kind of symbol.
///
/// Drawn rather than loaded from files: a handful of coloured glyphs needs no
/// asset pipeline, scales with the editor font, and keeps the shape and colour
/// conventions familiar from Visual Studio — a purple cube for methods, a
/// yellow class marker, a blue field, and so on.
/// </summary>
public static class CompletionIcons
{
    private static readonly Dictionary<CompletionKind, Bitmap> Cache = [];
    private static readonly Lock Gate = new();

    /// <summary>Rendered size in device pixels; matches a 13pt editor font.</summary>
    private const int Size = 16;

    /// <summary>
    /// Icon for a symbol as the extensibility contracts describe it.
    ///
    /// The provider model reports SymbolKind, which covers more than the older
    /// completion enum: modules, functions and constants have no equivalent
    /// there, so they are mapped here rather than lost.
    /// </summary>
    public static Bitmap? For(SymbolKind kind) => For(kind switch
    {
        SymbolKind.Class => CompletionKind.Class,
        SymbolKind.Structure => CompletionKind.Structure,
        SymbolKind.Interface => CompletionKind.Interface,
        SymbolKind.Enum => CompletionKind.Enum,
        SymbolKind.Module => CompletionKind.Class,
        SymbolKind.Delegate => CompletionKind.Method,
        SymbolKind.Method or SymbolKind.Function => CompletionKind.Method,
        SymbolKind.Property => CompletionKind.Property,
        SymbolKind.Field or SymbolKind.Constant => CompletionKind.Field,
        SymbolKind.Event => CompletionKind.Event,
        SymbolKind.Variable => CompletionKind.Local,
        SymbolKind.Parameter => CompletionKind.Parameter,
        SymbolKind.Namespace => CompletionKind.Namespace,
        SymbolKind.Keyword => CompletionKind.Keyword,
        SymbolKind.Snippet => CompletionKind.Snippet,
        _ => CompletionKind.Other
    });

    public static Bitmap? For(CompletionKind kind)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(kind, out var cached)) return cached;

            var bitmap = Render(kind);
            if (bitmap is not null) Cache[kind] = bitmap;
            return bitmap;
        }
    }

    /// <summary>Colour and glyph conventions matching Visual Studio's symbol icons.</summary>
    private static (Color Fill, string Glyph) Style(CompletionKind kind) => kind switch
    {
        CompletionKind.Class => (Color.Parse("#D8A200"), "C"),
        CompletionKind.Structure => (Color.Parse("#3A87C8"), "S"),
        CompletionKind.Interface => (Color.Parse("#3A87C8"), "I"),
        CompletionKind.Enum => (Color.Parse("#D8A200"), "E"),
        CompletionKind.Method => (Color.Parse("#8E44AD"), "M"),
        CompletionKind.Property => (Color.Parse("#4A4A4A"), "P"),
        CompletionKind.Field => (Color.Parse("#3A87C8"), "F"),
        CompletionKind.Event => (Color.Parse("#C8761E"), "e"),
        CompletionKind.Local => (Color.Parse("#4A7EBB"), "v"),
        CompletionKind.Parameter => (Color.Parse("#4A7EBB"), "p"),
        CompletionKind.Namespace => (Color.Parse("#6E6E6E"), "N"),
        CompletionKind.Keyword => (Color.Parse("#0060C0"), "K"),
        CompletionKind.Snippet => (Color.Parse("#2E8B57"), "s"),
        _ => (Color.Parse("#8A8A8A"), "?")
    };

    private static Bitmap? Render(CompletionKind kind)
    {
        var (fill, glyph) = Style(kind);

        var target = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));

        using (var context = target.CreateDrawingContext())
        {
            var brush = new SolidColorBrush(fill);

            // Rounded square background, the shape Visual Studio uses for
            // symbol icons.
            context.DrawRectangle(
                brush,
                null,
                new RoundedRect(new Rect(1, 1, Size - 2, Size - 2), 3));

            var text = new FormattedText(
                glyph,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Menlo,Consolas,DejaVu Sans Mono,monospace",
                    weight: FontWeight.Bold),
                10,
                Brushes.White);

            context.DrawText(
                text,
                new Point((Size - text.Width) / 2, (Size - text.Height) / 2));
        }

        return target;
    }
}
