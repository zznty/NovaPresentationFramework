using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Imaging;

/// <summary>
/// A progressive decode of a forward-only stream (a network download): the source bytes are
/// buffered exactly once inside the ImageSharp progressive iterator (an allocator-backed
/// chunked buffer — no temp file, no GC re-copy per decode) and decode attempts run at most
/// once per <see cref="Period"/>, each yielding a more complete <see cref="ProgressiveFrame"/>.
/// The final frame is the complete image.
/// </summary>
[PublicAPI]
public sealed class ProgressiveDecoderSession : IDisposable
{
    private const int HeaderProbeSize = 16;

    private readonly ImageDecoder _decoder;
    private readonly DecoderOptions _options;
    private readonly Stream _source;
    private readonly byte[] _prefix;
    private bool _disposed;

    private ProgressiveDecoderSession(ImageDecoder decoder, WicContainerFormat containerFormat, Stream source, byte[] prefix, TimeSpan period)
    {
        _decoder = decoder;
        ContainerFormat = containerFormat;
        _source = source;
        _prefix = prefix;
        Period = period;
        _options = new DecoderOptions
        {
            SegmentIntegrityHandling = SegmentIntegrityHandling.IgnoreData,
        };
    }

    /// <summary>The container format detected from the stream header.</summary>
    public WicContainerFormat ContainerFormat { get; }

    /// <summary>The minimum interval between decode attempts (frames).</summary>
    public TimeSpan Period { get; }

    /// <summary>
    /// Probes the stream header (up to 16 bytes, re-presented on each read) and starts a
    /// progressive session. The source stream is consumed by <see cref="GetFramesAsync"/> and
    /// must stay readable until the session completes; ownership stays with the caller.
    /// </summary>
    public static ProgressiveDecoderSession Create(Stream source, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(source));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero, nameof(period));

        byte[] prefix = ProbeHeader(source);
        (ImageDecoder decoder, WicContainerFormat format) = DetectFormat(prefix);
        return new ProgressiveDecoderSession(decoder, format, source, prefix, period);
    }

    /// <summary>
    /// Iterates the progressively more complete decoded frames, converted to BGRA. The caller
    /// owns and must dispose each frame. The final frame is the fully decoded image; a
    /// non-image or corrupt stream throws on the final attempt.
    /// </summary>
    public async IAsyncEnumerable<ProgressiveFrame> GetFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var prefixed = new PrefixedStream(_prefix, _source);
        await foreach (Image frame in _decoder.DecodeAsync(_options, prefixed, Period, cancellationToken).ConfigureAwait(false))
        {
            Image<Bgra32> converted = frame.CloneAs<Bgra32>();
            frame.Dispose();
            yield return new ProgressiveFrame(converted);
        }
    }

    /// <summary>Disposes the source stream (ownership transferred to the session on create).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Dispose();
    }

    private static byte[] ProbeHeader(Stream source)
    {
        byte[] header = new byte[HeaderProbeSize];
        int read = 0;
        while (read < HeaderProbeSize)
        {
            int count = source.Read(header, read, HeaderProbeSize - read);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return header[..read];
    }

    private static (ImageDecoder Decoder, WicContainerFormat Format) DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
        {
            return (PngDecoder.Instance, WicContainerFormat.Png);
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return (JpegDecoder.Instance, WicContainerFormat.Jpeg);
        }

        if (header.Length >= 6 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'8')
        {
            return (GifDecoder.Instance, WicContainerFormat.Gif);
        }

        if (header.Length >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M')
        {
            return (BmpDecoder.Instance, WicContainerFormat.Bmp);
        }

        if (header.Length >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return (WebpDecoder.Instance, WicContainerFormat.Wmp);
        }

        return header.Length >= 4 && ((header[0] == (byte)'I' && header[1] == (byte)'I' && header[2] == 0x2A && header[3] == 0)
            || (header[0] == (byte)'M' && header[1] == (byte)'M' && header[2] == 0 && header[3] == 0x2A))
            ? (TiffDecoder.Instance, WicContainerFormat.Tiff)
            : throw new InvalidDataException("The stream header does not match a supported image format.");
    }

    /// <summary>Re-presents the probed header bytes before the remaining source.</summary>
    private sealed class PrefixedStream(byte[] prefix, Stream remainder) : Stream
    {
        private int _prefixPosition;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _prefixPosition < prefix.Length
                ? CopyPrefix(buffer.AsSpan(offset), count)
                : remainder.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _prefixPosition < prefix.Length
                ? ValueTask.FromResult(CopyPrefix(buffer.Span, buffer.Length))
                : remainder.ReadAsync(buffer, cancellationToken);
        }

        private int CopyPrefix(Span<byte> destination, int count)
        {
            int copied = Math.Min(count, prefix.Length - _prefixPosition);
            prefix.AsSpan(_prefixPosition, copied).CopyTo(destination);
            _prefixPosition += copied;
            return copied;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}

/// <summary>
/// One progressively decoded frame (BGRA, straight). The caller owns the instance and must
/// dispose it; each frame is independent (the pooled pixel buffers return on disposal).
/// </summary>
[PublicAPI]
public sealed class ProgressiveFrame : IDisposable
{
    private bool _transferred;

    internal ProgressiveFrame(Image<Bgra32> image)
    {
        Image = image;
    }

    internal Image<Bgra32> Image { get; }

    public int PixelWidth => Image.Width;

    public int PixelHeight => Image.Height;

    /// <summary>Transfers ownership of the backing image to the caller (the progressive
    /// decoder); after the transfer this instance disposes nothing.</summary>
    internal Image<Bgra32> Detach()
    {
        _transferred = true;
        return Image;
    }

    public void Dispose()
    {
        if (!_transferred)
        {
            Image.Dispose();
        }
    }
}
