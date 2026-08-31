using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Imaging;

/// <summary>
/// PNG encode/decode over ImageSharp. The WPF Clipboard's Linux branch uses this to
/// move images through the SDL clipboard's "image/png" mime type.
/// </summary>
public static class PngCodec
{
    /// <summary>Encodes a BGRA32 pixel buffer as PNG.</summary>
    public static byte[] EncodeBgra32(int width, int height, ReadOnlySpan<byte> bgra32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (bgra32.Length != (long)width * height * 4)
        {
            throw new ArgumentException("Buffer length must be width * height * 4.", nameof(bgra32));
        }

        using Image<Bgra32> image = Image.LoadPixelData<Bgra32>(bgra32, width, height);
        using MemoryStream stream = new();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    /// <summary>Decodes a PNG into a BGRA32 pixel buffer, or null when the data is not a PNG.</summary>
    public static (int Width, int Height, byte[] Bgra32)? Decode(ReadOnlySpan<byte> png)
    {
        try
        {
            using Image<Bgra32> image = Image.Load<Bgra32>(png);
            byte[] pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            return (image.Width, image.Height, pixels);
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }
}
