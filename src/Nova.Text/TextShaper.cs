using JetBrains.Annotations;
using Nova.FontConfig;
using Nova.FreeType;
using Nova.Geometry;
using Nova.HarfBuzz;

namespace Nova.Text;

/// <summary>Resolves families, opens faces, and shapes runs. Assigns <see cref="Typeface.FaceId"/> values.</summary>
[PublicAPI]
public sealed class TextShaper : IDisposable
{
    private const int StackShapeCapacity = 1024;

    private readonly FontConfigLibrary _fontConfig = new();
    private readonly FreeTypeLibrary _freeType = new();
    private readonly Dictionary<FontQuery, Typeface> _typefaces = [];
    private uint _nextFaceId = 1;
    private bool _disposed;

    public Typeface Resolve(FontQuery query)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_typefaces.TryGetValue(query, out Typeface? typeface))
        {
            return typeface;
        }

        FontMatch match = _fontConfig.Match(query);
        FontFace face = _freeType.OpenFace(match.FilePath, match.FaceIndex);
        typeface = new Typeface(_nextFaceId++, face, match);
        _typefaces.Add(query, typeface);
        return typeface;
    }

    public int Shape(Typeface typeface, ReadOnlySpan<char> text, double pixelSize, ShapeOptions options, Span<PositionedGlyph> destination)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (text.IsEmpty)
        {
            return 0;
        }

        typeface.Face.SetPixelSize(pixelSize);
        using HarfBuzzShaper shaper = new(typeface.Face);

        Span<ShapedGlyph> shaped = text.Length <= StackShapeCapacity
            ? stackalloc ShapedGlyph[StackShapeCapacity]
            : new ShapedGlyph[text.Length];

        int count = shaper.Shape(text, options, shaped);
        int written = Math.Min(count, destination.Length);
        int quantized = Math.Max(1, (int)Math.Round(pixelSize, MidpointRounding.AwayFromZero));
        Point origin = Point.Origin;
        for (int i = 0; i < written; i++)
        {
            ShapedGlyph item = shaped[i];
            destination[i] = new PositionedGlyph(
                new GlyphId(typeface.FaceId, item.GlyphIndex, quantized),
                new Point(origin.X + item.Offset.X, origin.Y + item.Offset.Y),
                item.Advance);
            origin = new Point(origin.X + item.Advance.Width, origin.Y + item.Advance.Height);
        }

        return written;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Typeface typeface in _typefaces.Values)
        {
            typeface.Dispose();
        }

        _typefaces.Clear();
        _freeType.Dispose();
        _fontConfig.Dispose();
    }
}
