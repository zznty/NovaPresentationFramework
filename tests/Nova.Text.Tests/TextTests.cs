using Nova.FontConfig;
using Nova.Geometry;
using Nova.HarfBuzz;
using Nova.TestSupport;
using Nova.Vulkan;

namespace Nova.Text.Tests;

public sealed class TextTests
{
    private static Typeface ResolveSansSerif(TextShaper shaper)
    {
        Typeface typeface = shaper.Resolve(new FontQuery("sans-serif"));
        Assert.True(File.Exists(typeface.Match.FilePath), $"fontconfig matched '{typeface.Match.FilePath}' which does not exist.");
        return typeface;
    }

    [Fact]
    public void Resolve_SansSerif_ReturnsExistingFace()
    {
        using TextShaper shaper = new();
        Typeface typeface = ResolveSansSerif(shaper);
        Assert.NotEqual(0u, typeface.FaceId);
        Assert.False(string.IsNullOrWhiteSpace(typeface.Match.Family));
    }

    [Fact]
    public void Resolve_SameQuery_ReturnsSameTypeface()
    {
        using TextShaper shaper = new();
        Typeface first = shaper.Resolve(new FontQuery("sans-serif"));
        Typeface second = shaper.Resolve(new FontQuery("sans-serif"));
        Assert.Same(first, second);
        Assert.Equal(first.FaceId, second.FaceId);
    }

    [Fact]
    public void Shape_Hi_At16Pixels_TwoAdvancingGlyphs()
    {
        using TextShaper shaper = new();
        Typeface typeface = ResolveSansSerif(shaper);
        PositionedGlyph[] glyphs = new PositionedGlyph[16];

        int count = shaper.Shape(typeface, "Hi", 16, ShapeOptions.Default, glyphs);

        Assert.Equal(2, count);
        Assert.True(glyphs[0].Advance.Width > 0);
        Assert.True(glyphs[1].Advance.Width > 0);
        Assert.True(glyphs[1].Origin.X > 0);
        Assert.NotEqual(0u, glyphs[0].Id.GlyphIndex);
        Assert.Equal(typeface.FaceId, glyphs[0].Id.FaceId);
        Assert.Equal(16, glyphs[0].Id.PixelSize);
    }

    [Fact]
    public void Shape_EmptyText_ReturnsZero()
    {
        using TextShaper shaper = new();
        Typeface typeface = ResolveSansSerif(shaper);
        PositionedGlyph[] glyphs = new PositionedGlyph[4];
        Assert.Equal(0, shaper.Shape(typeface, string.Empty, 16, ShapeOptions.Default, glyphs));
    }

    [Fact]
    public void Atlas_GetOrAddTwice_ReturnsSameQuadAndRenders()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        using GlyphAtlas atlas = new(presenter, new PixelSize(64, 64));
        using TextShaper shaper = new();
        Typeface typeface = ResolveSansSerif(shaper);
        uint glyph = typeface.Face.GetGlyphIndex('A');

        GlyphQuad first = atlas.GetOrAdd(typeface.FaceId, typeface.Face, glyph, 16);
        GlyphQuad second = atlas.GetOrAdd(typeface.FaceId, typeface.Face, glyph, 16);

        Assert.Equal(first, second);
        Assert.True(first.Texture.IsValid);
        Assert.True(first.Uv.Width > 0);
        Assert.True(first.Uv.Height > 0);
        Assert.True(first.Size.Width > 0);
        Assert.Equal(1, atlas.PageCount);

        presenter.Render(queue =>
        {
            queue.Clear(new ColorRgba(0, 0, 0, 1));
            queue.DrawTexturedQuad(
                new Point(0, 0),
                new Point(64, 0),
                new Point(64, 64),
                new Point(0, 64),
                first.Texture,
                new Point(first.Uv.X, first.Uv.Y),
                new Point(first.Uv.Right, first.Uv.Y),
                new Point(first.Uv.Right, first.Uv.Bottom),
                new Point(first.Uv.X, first.Uv.Bottom),
                ColorRgba.White);
        });
        ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();

        bool anyNonBlack = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels.Span[i] == 0 && pixels.Span[i + 1] == 0 && pixels.Span[i + 2] == 0)
            {
                continue;
            }

            anyNonBlack = true;
            break;
        }

        Assert.True(anyNonBlack, "The glyph quad produced no visible pixels.");
    }

    [Fact]
    public void Atlas_EmptyGlyph_ReturnsOneByOneTransparentQuad()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        using GlyphAtlas atlas = new(presenter, new PixelSize(64, 64));
        using TextShaper shaper = new();
        Typeface typeface = ResolveSansSerif(shaper);
        uint glyph = typeface.Face.GetGlyphIndex(' ');

        GlyphQuad quad = atlas.GetOrAdd(typeface.FaceId, typeface.Face, glyph, 16);
        GlyphQuad cached = atlas.GetOrAdd(typeface.FaceId, typeface.Face, glyph, 16);

        Assert.Equal(quad, cached);
        Assert.True(quad.Texture.IsValid);
        Assert.Equal(new PixelSize(1, 1), quad.Size);
        Assert.Equal(0, quad.BearingX);
        Assert.Equal(0, quad.BearingY);
    }

    [Fact]
    public void Atlas_TryGet_DoesNotRasterize()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        using GlyphAtlas atlas = new(presenter, new PixelSize(64, 64));
        using TextShaper shaper = new();
        Typeface typeface = ResolveSansSerif(shaper);
        uint glyph = typeface.Face.GetGlyphIndex('B');
        var id = new GlyphId(typeface.FaceId, glyph, 16);

        Assert.False(atlas.TryGet(id, out _));
        _ = atlas.GetOrAdd(typeface.FaceId, typeface.Face, glyph, 16);
        Assert.True(atlas.TryGet(id, out GlyphQuad quad));
        Assert.True(quad.Texture.IsValid);
    }

    [Fact]
    public void Atlas_ManyDistinctGlyphs_GrowsPages_DisposeTwice()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        GlyphAtlas atlas = new(presenter, new PixelSize(32, 32));
        using TextShaper shaper = new();
        Typeface typeface = ResolveSansSerif(shaper);

        for (uint codepoint = 'a'; codepoint <= 'z'; codepoint++)
        {
            uint glyph = typeface.Face.GetGlyphIndex(codepoint);
            GlyphQuad quad = atlas.GetOrAdd(typeface.FaceId, typeface.Face, glyph, 16);
            Assert.True(quad.Texture.IsValid);
        }

        Assert.True(atlas.PageCount >= 2, $"expected multiple pages, got {atlas.PageCount}");
        atlas.Dispose();
        atlas.Dispose();
    }
}
