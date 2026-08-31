using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Imaging;

/// <summary>
/// Managed replacement for the windowscodecs imaging surface this host uses. The patched WIC
/// nest in PresentationCore routes every relevant call here; the opaque handles are tokens
/// into <see cref="WicHandleTable"/>. Decoding goes through ImageSharp's pooled memory
/// allocator, so the <see cref="Image{Bgra32}"/> backing store is the decoded image itself.
/// </summary>
[PublicAPI]
public static class ManagedWicCodec
{
    /// <summary>Creates a decoder-info token for the decoder (WIC <c>GetDecoderInfo</c>):
    /// a <see cref="ManagedWicDecoderInfo"/> holding the container format and MIME list.</summary>
    public static int GetDecoderInfo(nint decoderToken, out nint infoToken)
    {
        infoToken = 0;
        if (WicHandleTable.TryGet<ManagedWicDecoder>(decoderToken) is not { } decoder)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        var info = new ManagedWicDecoderInfo(decoder.ContainerFormat, MimeTypesForContainerFormat(decoder.ContainerFormat));
        infoToken = WicHandleTable.Create(info);
        return 0;
    }

    private static string MimeTypesForContainerFormat(WicContainerFormat containerFormat)
    {
        return containerFormat switch
        {
            WicContainerFormat.Bmp => "image/bmp",
            WicContainerFormat.Gif => "image/gif",
            WicContainerFormat.Ico => "image/vnd.microsoft.icon",
            WicContainerFormat.Jpeg => "image/jpeg,image/jpg,image/jpe,image/jfif",
            WicContainerFormat.Png => "image/png",
            WicContainerFormat.Tiff => "image/tiff,image/tif",
            WicContainerFormat.Wmp => "image/vnd.ms-photo",
            WicContainerFormat.Unknown => string.Empty,
            _ => string.Empty
        };
    }

    /// <summary>Creates the imaging-factory token (stateless on this host).</summary>
    public static int CreateImagingFactory(out nint factoryToken)
    {
        factoryToken = WicHandleTable.Create(new object());
        return 0;
    }

    /// <summary>Creates the MIL factory token (stateless on this host).</summary>
    public static int CreateMilFactory(out nint factoryToken)
    {
        factoryToken = WicHandleTable.Create(new object());
        return 0;
    }

    /// <summary>
    /// Decodes a seekable stream into a managed decoder. The stream is consumed from its
    /// current position to the end.
    /// </summary>
    public static int CreateDecoder(Stream stream, out nint decoderToken, out WicContainerFormat containerFormat)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            Image<Bgra32> image = Image.Load<Bgra32>(stream);
            containerFormat = DetectContainerFormat(image.MetaData.DecodedImageFormat);
            double dpiX = image.MetaData.HorizontalResolution;
            double dpiY = image.MetaData.VerticalResolution;
            ManagedWicDecoder? decoder = new(image, containerFormat, dpiX, dpiY);
            try
            {
                decoderToken = WicHandleTable.Create(decoder);
                decoder = null;
                return 0;
            }
            finally
            {
                decoder?.Dispose();
            }
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or IOException or ImageFormatException)
        {
            decoderToken = 0;
            containerFormat = WicContainerFormat.Unknown;
            return unchecked((int)0x88982F04); // WINCODEC_ERR_UNKNOWNIMAGEFORMAT
        }
    }

    /// <summary>
    /// Creates a progressive decoder token (a <see cref="ManagedWicDecoder"/> with a 1x1
    /// placeholder) whose single frame is filled by
    /// <see cref="UpdateProgressiveDecoderFrame"/> as the download proceeds. The placeholder
    /// must not be exposed before the first update.
    /// </summary>
    public static int CreateProgressiveDecoderToken(WicContainerFormat containerFormat, out nint decoderToken)
    {
        ManagedWicDecoder? decoder = ManagedWicDecoder.CreateProgressive(containerFormat);
        try
        {
            decoderToken = WicHandleTable.Create(decoder);
            decoder = null;
            return 0;
        }
        finally
        {
            decoder?.Dispose();
        }
    }

    /// <summary>
    /// Replaces a progressive decoder's content with a more complete frame of the same image.
    /// The frame's backing image transfers to the decoder (the frame disposes nothing after).
    /// UI-thread only — the live frame bitmap swaps its backing in place, so frames already
    /// materialized by WPF see the new pixels on their next read.
    /// </summary>
    public static int UpdateProgressiveDecoderFrame(nint decoderToken, ProgressiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (WicHandleTable.TryGet<ManagedWicDecoder>(decoderToken) is not { } decoder)
        {
            return unchecked((int)0x88982F60); // WINCODEC_ERR_COMPONENTNOTFOUND
        }

        decoder.UpdateFrame(frame.Detach());
        return 0;
    }

    /// <summary>Returns the frame count of a decoder token.</summary>
    public static int GetFrameCount(nint decoderToken, out uint count)
    {
        if (WicHandleTable.TryGet<ManagedWicDecoder>(decoderToken) is not { } decoder)
        {
            count = 0;
            return unchecked((int)0x80004003); // E_POINTER
        }

        count = (uint)decoder.FrameCount;
        return 0;
    }

    /// <summary>
    /// Extracts frame <paramref name="index"/> as an OWNING bitmap token. The bitmap is a
    /// clone of the frame (fresh pooled memory), so it outlives the decoder.
    /// </summary>
    public static int GetFrame(nint decoderToken, uint index, out nint frameToken)
    {
        frameToken = 0;
        if (WicHandleTable.TryGet<ManagedWicDecoder>(decoderToken) is not { } decoder)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        ManagedWicBitmap frame = decoder.GetFrame((int)index);
        ManagedWicBitmap? tracked = frame;
        try
        {
            frameToken = WicHandleTable.Create(frame);
            tracked = null;
            return 0;
        }
        finally
        {
            tracked?.Dispose();
        }
    }

    /// <summary>Creates a bitmap token from caller-provided pixels (one copy into pooled memory).</summary>
    public static unsafe int CreateBitmapFromMemory(
        int pixelWidth,
        int pixelHeight,
        WicPixelFormat sourceFormat,
        int strideBytes,
        int bufferSize,
        nint buffer,
        out nint bitmapToken)
    {
        bitmapToken = 0;
        if (pixelWidth <= 0 || pixelHeight <= 0 || buffer == 0 || bufferSize <= 0)
        {
            return unchecked((int)0x80070057); // E_INVALIDARG
        }

        int expected = checked(((pixelHeight - 1) * strideBytes) + (pixelWidth * 4));
        if (bufferSize < expected)
        {
            return unchecked((int)0x80070057); // E_INVALIDARG
        }

        Image<Bgra32> image = new(pixelWidth, pixelHeight);
        try
        {
            image.ProcessPixelRows(accessor =>
            {
                byte* source = (byte*)buffer;
                for (int y = 0; y < pixelHeight; y++)
                {
                    Span<Bgra32> row = accessor.GetRowSpan(y);
                    byte* rowSource = source + ((long)y * strideBytes);
                    for (int x = 0; x < pixelWidth; x++)
                    {
                        byte b = rowSource[x * 4];
                        byte g = rowSource[(x * 4) + 1];
                        byte r = rowSource[(x * 4) + 2];
                        byte a = rowSource[(x * 4) + 3];
                        if (sourceFormat == WicPixelFormat.Pbgra32)
                        {
                            // Premultiplied input: un-premultiply to the straight internal layout.
                            if (a is not 0 and not 255)
                            {
                                r = (byte)Math.Min(255, ((r * 255) + (a / 2)) / a);
                                g = (byte)Math.Min(255, ((g * 255) + (a / 2)) / a);
                                b = (byte)Math.Min(255, ((b * 255) + (a / 2)) / a);
                            }
                        }

                        // ImageSharp's Bgra32 ctor takes (r, g, b, a), not (b, g, r, a).
                        row[x] = new Bgra32(r, g, b, a);
                    }
                }
            });

            ManagedWicBitmap? bitmap = new(image, WicPixelFormat.Bgra32, 96, 96);
            try
            {
                bitmapToken = WicHandleTable.Create(bitmap);
                bitmap = null;
                return 0;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            image.Dispose();
            return unchecked((int)0x80070057); // E_INVALIDARG
        }
    }

    /// <summary>Clones a bitmap token into a new owning bitmap (WIC "cache on load" copy).</summary>
    public static int CreateBitmapFromSource(nint sourceToken, out nint bitmapToken)
    {
        bitmapToken = 0;
        if (ResolveBitmap(sourceToken) is not { } source)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        Image<Bgra32> clone = source.Image.Clone();
        ManagedWicBitmap? bitmap = new(clone, WicPixelFormat.Bgra32, source.DpiX, source.DpiY);
        try
        {
            bitmapToken = WicHandleTable.Create(bitmap);
            bitmap = null;
            return 0;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    /// <summary>Creates a format-converter token (configured by <see cref="FormatConverterInitialize"/>).</summary>
    public static int CreateFormatConverter(out nint converterToken)
    {
        converterToken = WicHandleTable.Create(new ManagedFormatConverter());
        return 0;
    }

    /// <summary>
    /// Configures the converter token to source from <paramref name="sourceToken"/>. The
    /// converter token stays usable as an <c>IWICBitmapSource</c> afterwards (WPF keeps using
    /// the converter handle); pixel conversion is lazy — <c>CopyPixels</c> converts from the
    /// straight <c>Bgra32</c> storage to the requested layout at copy time.
    /// </summary>
    public static int FormatConverterInitialize(
        nint converterToken,
        nint sourceToken,
        WicPixelFormat destinationFormat,
        out nint convertedToken)
    {
        _ = destinationFormat;
        convertedToken = 0;
        if (WicHandleTable.TryGet<ManagedFormatConverter>(converterToken) is not { } converter)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        if (ResolveBitmap(sourceToken) is not { } source)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        converter.Initialize(source);
        return 0;
    }

    /// <summary>Bitmap-source size query.</summary>
    public static int BitmapSourceGetSize(nint bitmapToken, out int width, out int height)
    {
        if (ResolveBitmap(bitmapToken) is not { } bitmap)
        {
            width = 0;
            height = 0;
            return unchecked((int)0x80004003); // E_POINTER
        }

        width = bitmap.PixelWidth;
        height = bitmap.PixelHeight;
        return 0;
    }

    /// <summary>Bitmap-source resolution query (DPI).</summary>
    public static int BitmapSourceGetResolution(nint bitmapToken, out double dpiX, out double dpiY)
    {
        if (ResolveBitmap(bitmapToken) is not { } bitmap)
        {
            dpiX = 0;
            dpiY = 0;
            return unchecked((int)0x80004003); // E_POINTER
        }

        dpiX = bitmap.DpiX;
        dpiY = bitmap.DpiY;
        return 0;
    }

    /// <summary>
    /// Copies pixels from a bitmap token into a caller buffer in the requested layout. This is
    /// the premultiply point for the render path: WPF requests Pbgra32, which is exactly the
    /// Vulkan <c>B8G8R8A8Unorm</c> premultiplied layout.
    /// </summary>
    public static unsafe int BitmapSourceCopyPixels(
        nint bitmapToken,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        WicPixelFormat destinationFormat,
        int strideBytes,
        int bufferSize,
        nint buffer)
    {
        if (ResolveBitmap(bitmapToken) is not { } bitmap)
        {
            // A stale token here means the bitmap was already delivered to the render graph
            // (SendCommandBitmapSource detached it) or the handle belongs to a recovered
            // bitmap. The only caller that touches an unknown token is the
            // DUCECompatiblePtr decode-check, which ignores the pixel contents — succeed so
            // the check does not raise HRESULT failure (which WPF turns into a
            // NullReferenceException via Marshal.GetExceptionForHR).
            return 0;
        }

        if (buffer == 0 || bufferSize <= 0)
        {
            return unchecked((int)0x80070057); // E_INVALIDARG
        }

        if (sourceWidth <= 0)
        {
            sourceWidth = bitmap.PixelWidth - sourceX;
        }

        if (sourceHeight <= 0)
        {
            sourceHeight = bitmap.PixelHeight - sourceY;
        }

        int bytesPerPixel = destinationFormat is WicPixelFormat.Bgr24 ? 3 : 4;
        int rowBytes = sourceWidth * bytesPerPixel;
        if (bufferSize < rowBytes || (sourceHeight > 1 && bufferSize < ((long)(sourceHeight - 1) * strideBytes) + rowBytes))
        {
            // WPF's DUCECompatiblePtr decode-check copies 1x1 into a 1-byte buffer purely to
            // force a decode on the UI thread (BitmapSource.cs "Make sure the image is decoded
            // on the UI thread"). There is no room for a real pixel; succeed after forcing the
            // backing store to materialize.
            _ = bitmap.PixelWidth;
            _ = bitmap.PixelHeight;
            return 0;
        }

        if (sourceWidth < 0 || sourceHeight < 0 || sourceX < 0 || sourceY < 0)
        {
            return unchecked((int)0x80070057); // E_INVALIDARG
        }

        try
        {
            bitmap.CopyPixels(
                sourceX,
                sourceY,
                sourceWidth,
                sourceHeight,
                destinationFormat,
                new Span<byte>((void*)buffer, bufferSize),
                strideBytes);
            return 0;
        }
        catch (ArgumentException)
        {
            return unchecked((int)0x80070057); // E_INVALIDARG
        }
    }

    /// <summary>Sets the bitmap resolution (stored DPI).</summary>
    public static int BitmapSetResolution(nint bitmapToken, double dpiX, double dpiY)
    {
        if (ResolveBitmap(bitmapToken) is not { } bitmap)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        bitmap.SetResolution(dpiX, dpiY);
        return 0;
    }

    /// <summary>Reports the pixel format for a bitmap token (nest maps the enum → GUID).</summary>
    public static int BitmapSourceGetPixelFormat(nint bitmapToken, out WicPixelFormat format)
    {
        if (ResolveBitmap(bitmapToken) is not { } bitmap)
        {
            format = WicPixelFormat.Bgra32;
            return unchecked((int)0x80004003); // E_POINTER
        }

        format = bitmap.Format;
        return 0;
    }

    /// <summary>No color contexts on this host.</summary>
    public static int GetColorContextCount(nint bitmapToken, out uint count)
    {
        _ = bitmapToken;
        count = 0;
        return 0;
    }

    /// <summary>No palette on this host (opaque/unknown return).</summary>
    public static int BitmapSourceCopyPalette(nint bitmapToken, nint paletteToken)
    {
        _ = bitmapToken;
        _ = paletteToken;
        return unchecked((int)0x88982F0E); // WINCODEC_ERR_PALETTEUNAVAILABLE
    }


    /// <summary>Resolves a bitmap token, following a format-converter token to its source.</summary>
    private static ManagedWicBitmap? ResolveBitmap(nint token)
    {
        return WicHandleTable.TryGet<ManagedWicBitmap>(token) is { } bitmap
            ? bitmap
            : WicHandleTable.TryGet<ManagedFormatConverter>(token) is { Source: not null } converter
                ? converter.Source
                : null;
    }

    private static WicContainerFormat DetectContainerFormat(IImageFormat? format)
    {
        return format switch
        {
            null => WicContainerFormat.Unknown,
            _ when format.Name.Equals("BMP", StringComparison.OrdinalIgnoreCase) => WicContainerFormat.Bmp,
            _ when format.Name.Equals("GIF", StringComparison.OrdinalIgnoreCase) => WicContainerFormat.Gif,
            _ when format.Name.Equals("ICO", StringComparison.OrdinalIgnoreCase) => WicContainerFormat.Ico,
            _ when format.Name.Equals("JPEG", StringComparison.OrdinalIgnoreCase) => WicContainerFormat.Jpeg,
            _ when format.Name.Equals("PNG", StringComparison.OrdinalIgnoreCase) => WicContainerFormat.Png,
            _ when format.Name.Equals("TIFF", StringComparison.OrdinalIgnoreCase) => WicContainerFormat.Tiff,
            _ => WicContainerFormat.Unknown
        };
    }
}
