using Nova.FontConfig;

namespace Nova.FreeType.Tests;

public sealed class FreeTypeTests
{
    private static string GetFontFile()
    {
        using FontConfigLibrary fontConfig = new();
        FontMatch match = fontConfig.Match(new FontQuery("sans-serif"));
        Assert.True(File.Exists(match.FilePath), $"fontconfig matched '{match.FilePath}' which does not exist.");
        return match.FilePath;
    }

    [Fact]
    public void Rasterize_NarrowIconGlyph_CentersOnAdvance()
    {
        // The icon font's Phone glyph (E717) is a narrow rectangle whose ink must sit
        // at the METRIC left-side bearing (centered on the advance): the hinted
        // bitmap_left snapped to 0 and left-shifted every narrow icon glyph (Phone,
        // Microphone) while square glyphs (Hangup) looked centered by accident.
        string font = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fonts", "NovaFluentIcons.ttf"));
        if (!File.Exists(font))
        {
            return; // the bundled font is absent in this layout
        }

        using FreeTypeLibrary library = new();
        using FontFace face = library.OpenFace(font);
        uint phone = face.GetGlyphIndex(0xE717);
        GlyphBitmap bitmap = face.Rasterize(phone, 16);

        // The advance is 16px, the ink ~12px wide, so the centered bearing is ~2px.
        Assert.InRange(bitmap.Left, 1, 3);
        Assert.True(bitmap.Size.Width < 14, "the Phone glyph must be narrower than the em");
    }

    [Fact]
    public void OpenFace_ReportsSaneMetrics()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = library.OpenFace(GetFontFile());
        Assert.False(string.IsNullOrWhiteSpace(face.FamilyName));
        Assert.InRange(face.Metrics.UnitsPerEm, 100, 10000);
        Assert.True(face.Metrics.Ascent > 0);
        Assert.True(face.Metrics.Descent < 0);
        Assert.True(face.Metrics.GlyphCount > 0);
    }

    [Fact]
    public void GlyphA_HasIndexAndDesignMetrics()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = library.OpenFace(GetFontFile());
        double upem = face.Metrics.UnitsPerEm;

        uint index = face.GetGlyphIndex('A');
        Assert.NotEqual(0u, index);

        GlyphMetrics metrics = face.GetDesignMetrics(index);
        Assert.InRange(metrics.Advance.Width, upem * 0.25, upem * 2);
        Assert.InRange(metrics.Bounds.Width, 0, upem * 2);
        Assert.InRange(metrics.Bounds.Height, 0, upem * 2);
    }

    [Fact]
    public void Rasterize_16px_ProducesNonEmptyGrayBitmap()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = library.OpenFace(GetFontFile());
        GlyphBitmap bitmap = face.Rasterize(face.GetGlyphIndex('A'), 16);
        Assert.True(bitmap.Size.Width > 0);
        Assert.True(bitmap.Size.Height > 0);
        Assert.True(bitmap.Pixels.Length > 0);
        Assert.NotEqual(0, bitmap.Pitch);
        Assert.True(bitmap.Pixels.Span.IndexOfAnyExcept((byte)0) >= 0);
    }

    [Fact]
    public void Rasterize_SpaceGlyph_ReturnsEmptyBitmap()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = library.OpenFace(GetFontFile());
        GlyphBitmap bitmap = face.Rasterize(face.GetGlyphIndex(' '), 16);
        Assert.True(bitmap.Size.IsEmpty);
        Assert.Equal(0, bitmap.Pixels.Length);
        Assert.Equal(0, bitmap.Pitch);
    }

    [Fact]
    public void OpenMemoryFace_Works()
    {
        using FreeTypeLibrary library = new();
        byte[] fontData = File.ReadAllBytes(GetFontFile());
        using FontFace face = library.OpenFace(fontData);
        Assert.NotEqual(0u, face.GetGlyphIndex('A'));
    }

    [Fact]
    public void OpenMissingFile_ThrowsWithError()
    {
        using FreeTypeLibrary library = new();
        FreeTypeException ex = Assert.Throws<FreeTypeException>(() => library.OpenFace("/nonexistent/missing-font.ttf"));
        Assert.NotEqual(0, ex.Error);
    }

    [Fact]
    public void TryGetTable_NameTable_ReturnsBytes()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = library.OpenFace(GetFontFile());
        Assert.True(face.TryGetTable(0x6E616D65u, out byte[] table));
        Assert.True(table.Length > 0);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        FreeTypeLibrary library = new();
        FontFace face = library.OpenFace(GetFontFile());
        face.Dispose();
        face.Dispose();
        library.Dispose();
        library.Dispose();
    }

    [Fact]
    public void Face_AfterDispose_Throws()
    {
        FreeTypeLibrary library = new();
        FontFace face = library.OpenFace(GetFontFile());
        library.Dispose();

        ObjectDisposedException ex = Assert.Throws<ObjectDisposedException>(() => face.GetGlyphIndex('A'));
        Assert.Equal("Nova.FreeType.FontFace", ex.ObjectName);
    }
}
