using JetBrains.Annotations;
using Nova.FreeType;
using Nova.Geometry;
using Nova.Vulkan;

namespace Nova.Text;

/// <summary>
/// Packs gray FreeType bitmaps into one or more R8 atlas textures on a presenter.
/// Grow by allocating a new page, not by resizing an existing texture.
/// </summary>
[PublicAPI]
public sealed class GlyphAtlas : IDisposable
{
    private const int Padding = 1;

    private readonly Dictionary<GlyphId, GlyphQuad> _quads = [];
    private readonly List<IGpuTexture> _pages = [];
    private bool _disposed;
    private int _shelfX;
    private int _shelfY;
    private int _shelfHeight;

    public GlyphAtlas(IVulkanPresenter presenter, PixelSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        if (pageSize.Width < 32 || pageSize.Height < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        Presenter = presenter;
        PageSize = pageSize;
        _pages.Add(CreatePage());
    }

    public IVulkanPresenter Presenter { get; }

    public PixelSize PageSize { get; }

    public int PageCount { get; private set; }

    public GlyphQuad GetOrAdd(uint faceId, FontFace face, uint glyphIndex, double pixelSize)
    {
        ArgumentNullException.ThrowIfNull(face);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int quantized = Math.Max(1, (int)Math.Round(pixelSize, MidpointRounding.AwayFromZero));
        var id = new GlyphId(faceId, glyphIndex, quantized);
        if (_quads.TryGetValue(id, out GlyphQuad existing))
        {
            return existing;
        }

        GlyphBitmap bitmap = face.Rasterize(glyphIndex, quantized);
        GlyphQuad quad = bitmap.Size.IsEmpty
            ? CreateEmptyQuad()
            : Pack(bitmap);
        _quads.Add(id, quad);
        return quad;
    }

    public bool TryGet(GlyphId id, out GlyphQuad quad)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _quads.TryGetValue(id, out quad);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IGpuTexture page in _pages)
        {
            page.Dispose();
        }

        _pages.Clear();
        _quads.Clear();
    }

    private IGpuTexture CreatePage()
    {
        byte[] zeroed = new byte[checked(PageSize.Width * PageSize.Height)];
        IGpuTexture page = Presenter.CreateTexture(new TextureUpload(PageSize, PixelFormat.R8Unorm, zeroed, PageSize.Width));
        PageCount++;
        return page;
    }

    private GlyphQuad CreateEmptyQuad()
    {
        // A 1x1 quad over the atlas origin, which is padding and therefore stays zero.
        IGpuTexture first = _pages[0];
        return new GlyphQuad(
            first.Handle,
            new Rect(0, 0, 1.0 / PageSize.Width, 1.0 / PageSize.Height),
            new PixelSize(1, 1),
            0,
            0);
    }

    private GlyphQuad Pack(GlyphBitmap bitmap)
    {
        int cellWidth = bitmap.Size.Width + (Padding * 2);
        int cellHeight = bitmap.Size.Height + (Padding * 2);
        (IGpuTexture page, int x, int y) = Place(cellWidth, cellHeight);
        int bitmapX = x + Padding;
        int bitmapY = y + Padding;

        ReadOnlySpan<byte> pixels = bitmap.Pitch == bitmap.Size.Width
            ? bitmap.Pixels.Span
            : CopyRows(bitmap);
        Presenter.UpdateTexture(
            page.Handle,
            bitmapX,
            bitmapY,
            new TextureUpload(bitmap.Size, PixelFormat.R8Unorm, pixels, bitmap.Size.Width));

        double pageWidth = PageSize.Width;
        double pageHeight = PageSize.Height;
        return new GlyphQuad(
            page.Handle,
            new Rect(
                bitmapX / pageWidth,
                bitmapY / pageHeight,
                bitmap.Size.Width / pageWidth,
                bitmap.Size.Height / pageHeight),
            bitmap.Size,
            bitmap.Left,
            bitmap.Top);
    }

    private (IGpuTexture Page, int X, int Y) Place(int cellWidth, int cellHeight)
    {
        if (cellWidth > PageSize.Width || cellHeight > PageSize.Height)
        {
            throw new InvalidOperationException("The glyph is larger than the atlas page.");
        }

        while (true)
        {
            IGpuTexture page = _pages[^1];
            if (_shelfX + cellWidth <= PageSize.Width && _shelfY + cellHeight <= PageSize.Height)
            {
                (IGpuTexture, int, int) placement = (page, _shelfX, _shelfY);
                _shelfX += cellWidth;
                _shelfHeight = Math.Max(_shelfHeight, cellHeight);
                return placement;
            }

            if (_shelfX > 0)
            {
                // Start a new shelf row on the same page.
                _shelfY += _shelfHeight;
                _shelfX = 0;
                _shelfHeight = 0;
                continue;
            }

            // The shelf row already starts at x = 0; start a new page.
            _pages.Add(CreatePage());
            _shelfX = 0;
            _shelfY = 0;
            _shelfHeight = 0;
        }
    }

    private static byte[] CopyRows(GlyphBitmap bitmap)
    {
        int width = bitmap.Size.Width;
        int height = bitmap.Size.Height;
        if (width == 0 || height == 0)
        {
            return [];
        }

        ReadOnlyMemory<byte> source = bitmap.Pixels;
        if (bitmap.Pitch == width)
        {
            return source.Span[..(width * height)].ToArray();
        }

        byte[] packed = new byte[width * height];
        int stride = Math.Abs(bitmap.Pitch);
        if (bitmap.Pitch > 0)
        {
            for (int row = 0; row < height; row++)
            {
                source.Span.Slice(row * stride, width).CopyTo(packed.AsSpan(row * width));
            }
        }
        else
        {
            // Negative pitch: rows are stored bottom-up, so the first memory row
            // is the last image row.
            for (int row = 0; row < height; row++)
            {
                source.Span.Slice((height - 1 - row) * stride, width).CopyTo(packed.AsSpan(row * width));
            }
        }

        return packed;
    }
}
