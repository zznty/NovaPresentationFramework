using Nova.FontConfig;
using Nova.FreeType;

namespace Nova.HarfBuzz.Tests;

public sealed class HarfBuzzTests
{
    private static FontFace OpenFace(FreeTypeLibrary library)
    {
        using FontConfigLibrary fontConfig = new();
        FontMatch match = fontConfig.Match(new FontQuery("sans-serif"));
        Assert.True(File.Exists(match.FilePath), $"fontconfig matched '{match.FilePath}' which does not exist.");
        return library.OpenFace(match.FilePath);
    }

    [Fact]
    public void Shape_Hi_ReturnsTwoGlyphsWithAdvances()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = OpenFace(library);
        using HarfBuzzShaper shaper = new(face);

        ShapedGlyph[] glyphs = new ShapedGlyph[16];
        int count = shaper.Shape("Hi", ShapeOptions.Default, glyphs);
        Assert.Equal(2, count);
        Assert.Equal(0u, glyphs[0].Cluster);
        Assert.Equal(1u, glyphs[1].Cluster);
        Assert.All(glyphs.Take(count), glyph => Assert.True(glyph.Advance.Width > 0));
    }

    [Fact]
    public void Shape_Rtl_ReversesClusterOrder()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = OpenFace(library);
        using HarfBuzzShaper shaper = new(face);

        ShapedGlyph[] glyphs = new ShapedGlyph[16];
        int count = shaper.Shape("Hi", new ShapeOptions(rightToLeft: true), glyphs);
        Assert.Equal(2, count);
        Assert.Equal(1u, glyphs[0].Cluster);
        Assert.Equal(0u, glyphs[1].Cluster);
        Assert.All(glyphs.Take(count), glyph => Assert.True(glyph.Advance.Width > 0));
    }

    [Fact]
    public void Shape_EmptyText_ReturnsZero()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = OpenFace(library);
        using HarfBuzzShaper shaper = new(face);

        ShapedGlyph[] glyphs = new ShapedGlyph[4];
        Assert.Equal(0, shaper.Shape(string.Empty, ShapeOptions.Default, glyphs));
    }

    [Fact]
    public void Shape_AfterDispose_Throws()
    {
        using FreeTypeLibrary library = new();
        using FontFace face = OpenFace(library);
        HarfBuzzShaper shaper = new(face);
        shaper.Dispose();

        ObjectDisposedException ex = Assert.Throws<ObjectDisposedException>(() =>
            shaper.Shape("Hi", ShapeOptions.Default, new ShapedGlyph[4]));
        Assert.Equal("Nova.HarfBuzz.HarfBuzzShaper", ex.ObjectName);
    }
}
