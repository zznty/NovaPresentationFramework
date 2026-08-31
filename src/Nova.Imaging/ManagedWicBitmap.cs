using System.Runtime.InteropServices;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Imaging;

/// <summary>
/// An ImageSharp-backed bitmap: holds the decoded <see cref="Image{Bgra32}"/> (straight BGRA,
/// pooled by ImageSharp's <see cref="Configuration.Default"/> allocator) and copies pixels out
/// in the requested layout. This is the object the owner asked us to "wrap and use directly":
/// the <c>Image</c> stays alive as the backing store, and the render path reads its pooled
/// memory instead of a detached per-frame <c>byte[]</c>.
/// </summary>
[PublicAPI]
public sealed class ManagedWicBitmap : IDisposable
{
    private bool _disposed;
    private Image<Bgra32> _image;

    /// <summary>Wraps an existing image (ownership transfers to this instance).</summary>
    public ManagedWicBitmap(Image<Bgra32> image, WicPixelFormat format, double dpiX, double dpiY, int frameIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (frameIndex < 0 || frameIndex >= image.Frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        _image = image;
        FrameIndex = frameIndex;
        Format = format;
        DpiX = dpiX;
        DpiY = dpiY;
    }

    public int PixelWidth => Image.Width;

    public int PixelHeight => Image.Height;

    /// <summary>The internal (straight) pixel layout the <see cref="Image{Bgra32}"/> stores.</summary>
    public WicPixelFormat Format { get; }

    public double DpiX { get; private set; }

    public double DpiY { get; private set; }

    /// <summary>The pooled backing store. Do not dispose or mutate while borrowed.</summary>
    public Image<Bgra32> Image => _image;

    /// <summary>
    /// The frame this bitmap exposes. Multi-frame sources keep one bitmap per frame index over
    /// the shared <see cref="Image"/>.
    /// </summary>
    public int FrameIndex { get; }

    /// <summary>
    /// Replaces the backing image with a more complete decode of the SAME image (progressive
    /// decoding). The old backing returns to the allocator pool. Callers holding
    /// <see cref="Image"/> from before the swap must not use it afterwards. UI-thread only:
    /// the NPF composition reads bitmap pixels on the render thread of the dispatcher loop,
    /// and progressive updates marshal there too.
    /// </summary>
    internal void UpdateFrom(Image<Bgra32> replacement)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(replacement);
        if (ReferenceEquals(_image, replacement))
        {
            return;
        }

        Image<Bgra32> old = Interlocked.Exchange(ref _image, replacement);
        old?.Dispose();
    }

    /// <summary>
    /// Copies the pixels of the given source rectangle into <paramref name="destination"/> in
    /// the requested layout. <paramref name="destinationStrideBytes"/> is the destination row
    /// stride; rows are <see cref="PixelWidth"/> wide in the source layout.
    /// </summary>
    public void CopyPixels(
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        WicPixelFormat destinationFormat,
        Span<byte> destination,
        int destinationStrideBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceX < 0 || sourceY < 0)
        {
            return;
        }

        int bytesPerPixel = BytesPerPixel(destinationFormat);
        int rowBytes = sourceWidth * bytesPerPixel;
        long minimum = ((long)(sourceHeight - 1) * destinationStrideBytes) + rowBytes;
        if (destination.Length < minimum || destinationStrideBytes < rowBytes)
        {
            throw new ArgumentException("The destination buffer is smaller than the copy requires.", nameof(destination));
        }

        bool premultiply = destinationFormat is WicPixelFormat.Pbgra32 or WicPixelFormat.Prgba32;
        bool rgbaOrder = destinationFormat is WicPixelFormat.Rgba32 or WicPixelFormat.Prgba32;

        ImageFrame<Bgra32> frame = Image.Frames[FrameIndex];
        if (TryGetContiguousPixels(frame, out Memory<Bgra32> pixels))
        {
            CopyRegionContiguous(pixels.Span, frame.Width, sourceX, sourceY, sourceWidth, sourceHeight, destination, destinationStrideBytes, bytesPerPixel, premultiply, rgbaOrder);
            return;
        }

        // Non-contiguous backing (large images whose allocation is split into separate
        // row blocks): convert row-by-row straight into the destination. Materializing a
        // second image here does NOT guarantee contiguity (the same allocator splits large
        // buffers the same way), and a silent skip left the destination untouched — the
        // all-transparent-texture defect. The per-pixel math is identical to the contiguous
        // path (see CopyRegionContiguous).
        CopyRegionRows(frame, sourceX, sourceY, sourceWidth, sourceHeight, destination, destinationStrideBytes, bytesPerPixel, premultiply, rgbaOrder);
    }

    /// <summary>Exposes the backing frame's single-pixel memory when it is contiguous (zero-copy path).</summary>
    public bool TryGetSinglePixelMemory(out Memory<Bgra32> memory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Image.Frames[FrameIndex].DangerousTryGetSinglePixelMemory(out memory);
    }

    /// <summary>Updates the stored DPI (WIC <c>SetResolution</c>).</summary>
    public void SetResolution(double dpiX, double dpiY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DpiX = dpiX;
        DpiY = dpiY;
        Image.MetaData.HorizontalResolution = dpiX;
        Image.MetaData.VerticalResolution = dpiY;
    }

    /// <summary>Returns an independent owning copy of this bitmap (same pixels, format, DPI
    /// and frame). Used when one decoded bitmap must be handed to multiple owners.</summary>
    public ManagedWicBitmap Clone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ManagedWicBitmap(Image.Clone(), Format, DpiX, DpiY, FrameIndex);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Image.Dispose();
    }

    private static bool TryGetContiguousPixels(ImageFrame<Bgra32> frame, out Memory<Bgra32> memory)
    {
        return frame.DangerousTryGetSinglePixelMemory(out memory);
    }

    private static void CopyRegionContiguous(
        ReadOnlySpan<Bgra32> pixels,
        int frameWidth,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        Span<byte> destination,
        int destinationStrideBytes,
        int bytesPerPixel,
        bool premultiply,
        bool rgbaOrder)
    {
        int maxY = Math.Min(sourceY + sourceHeight, frameWidth <= 0 ? 0 : pixels.Length / frameWidth);
        for (int y = sourceY; y < maxY; y++)
        {
            int rowBase = y * frameWidth;
            int destRowOffset = (y - sourceY) * destinationStrideBytes;
            int maxX = Math.Min(sourceX + sourceWidth, frameWidth);
            for (int x = sourceX; x < maxX; x++)
            {
                WritePixel(pixels[rowBase + x], destination, destRowOffset + ((x - sourceX) * bytesPerPixel), premultiply, rgbaOrder);
            }
        }
    }

    /// <summary>Non-contiguous-backing equivalent of <see cref="CopyRegionContiguous"/>: reads
    /// each split row block with the frame buffer's <c>DangerousGetRowSpan</c> and copies
    /// into the destination in place — a series of row copies, no whole-frame staging buffer.
    /// A straight Bgra32 destination copies row spans verbatim; only premultiply/order
    /// conversion touches individual pixels.</summary>
    private static void CopyRegionRows(
        ImageFrame<Bgra32> frame,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        Span<byte> destination,
        int destinationStrideBytes,
        int bytesPerPixel,
        bool premultiply,
        bool rgbaOrder)
    {
        int maxY = Math.Min(sourceY + sourceHeight, frame.Height);
        int maxX = Math.Min(sourceX + sourceWidth, frame.Width);
        for (int y = sourceY; y < maxY; y++)
        {
            // Dangerous only in that a frame resize/dispose invalidates the span; the frame
            // is alive and stable for the duration of this copy.
            Span<Bgra32> row = frame.PixelBuffer.DangerousGetRowSpan(y);
            int destRowOffset = (y - sourceY) * destinationStrideBytes;
            if (!premultiply && !rgbaOrder)
            {
                row[sourceX..maxX].CopyTo(
                    MemoryMarshal.Cast<byte, Bgra32>(destination[destRowOffset..(destRowOffset + ((maxX - sourceX) * bytesPerPixel))]));
                continue;
            }

            for (int x = sourceX; x < maxX; x++)
            {
                WritePixel(row[x], destination, destRowOffset + ((x - sourceX) * bytesPerPixel), premultiply, rgbaOrder);
            }
        }
    }

    private static void WritePixel(Bgra32 pixel, Span<byte> destination, int offset, bool premultiply, bool rgbaOrder)
    {
        byte a = pixel.A;
        byte b = pixel.B;
        byte g = pixel.G;
        byte r = pixel.R;
        if (premultiply && a != 0)
        {
            b = (byte)(((b * a) + 127) / 255);
            g = (byte)(((g * a) + 127) / 255);
            r = (byte)(((r * a) + 127) / 255);
        }

        if (rgbaOrder)
        {
            destination[offset] = r;
            destination[offset + 1] = g;
            destination[offset + 2] = b;
            destination[offset + 3] = a;
        }
        else
        {
            destination[offset] = b;
            destination[offset + 1] = g;
            destination[offset + 2] = r;
            destination[offset + 3] = a;
        }
    }

    private static int BytesPerPixel(WicPixelFormat format)
    {
        return format switch
        {
            WicPixelFormat.Bgra32 or WicPixelFormat.Pbgra32 or WicPixelFormat.Bgr32 or
            WicPixelFormat.Rgba32 or WicPixelFormat.Prgba32 => 4,
            WicPixelFormat.Bgr24 => 3,
            WicPixelFormat.Gray8 => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.")
        };
    }
}
