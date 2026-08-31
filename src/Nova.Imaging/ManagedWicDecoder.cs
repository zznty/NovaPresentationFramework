using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Imaging;

/// <summary>
/// An ImageSharp-backed decoder: owns the decoded <see cref="Image{Bgra32}"/> and exposes
/// per-frame <see cref="ManagedWicBitmap"/> views over it. Frame 0 is the whole image for
/// single-frame formats (PNG/JPEG); multi-frame formats (GIF, animated PNG) expose each frame
/// over the same backing image.
/// </summary>
[PublicAPI]
public sealed class ManagedWicDecoder : IDisposable
{
    private Image<Bgra32> _image;
    private ManagedWicBitmap? _liveFrame;
    private bool _disposed;

    public ManagedWicDecoder(Image<Bgra32> image, WicContainerFormat containerFormat, double dpiX, double dpiY)
    {
        ArgumentNullException.ThrowIfNull(image);
        _image = image;
        ContainerFormat = containerFormat;
        DpiX = dpiX;
        DpiY = dpiY;
    }

    /// <summary>Creates a decoder whose frame is filled in progressively (see
    /// <see cref="UpdateFrame"/>). The placeholder is 1x1 transparent until the first frame
    /// arrives; the caller must not expose it before then.</summary>
    public static ManagedWicDecoder CreateProgressive(WicContainerFormat containerFormat)
    {
        Image<Bgra32> placeholder = new(Configuration.Default, 1, 1, new Bgra32(0, 0, 0, 0));
        return new ManagedWicDecoder(placeholder, containerFormat, 96, 96)
        {
            IsProgressive = true,
        };
    }

    /// <summary>
    /// True for progressive decoders: <see cref="GetFrame"/> returns one LIVE bitmap whose
    /// backing is replaced on each <see cref="UpdateFrame"/>, instead of owning clones.
    /// </summary>
    public bool IsProgressive { get; private set; }

    public WicContainerFormat ContainerFormat { get; }

    public int FrameCount => IsProgressive ? 1 : _image.Frames.Count;

    public int PixelWidth => _image.Width;

    public int PixelHeight => _image.Height;

    public double DpiX { get; private set; }

    public double DpiY { get; private set; }

    /// <summary>
    /// Replaces the decoded content with a more complete frame of the SAME image (progressive
    /// decoding). The previous backing returns to the allocator pool; any live frame bitmap
    /// swaps its backing in place. DPI follows the frame's metadata. UI-thread only.
    /// </summary>
    public void UpdateFrame(Image<Bgra32> frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsProgressive)
        {
            throw new InvalidOperationException("UpdateFrame requires a progressive decoder.");
        }

        Image<Bgra32> old = Interlocked.Exchange(ref _image, frame);
        DpiX = frame.MetaData.HorizontalResolution > 0 ? frame.MetaData.HorizontalResolution : DpiX;
        DpiY = frame.MetaData.VerticalResolution > 0 ? frame.MetaData.VerticalResolution : DpiY;
        if (_liveFrame is not null)
        {
            // The live bitmap swaps its own backing and disposes the old image.
            _liveFrame.UpdateFrom(frame);
        }
        else
        {
            old?.Dispose();
        }
    }

    /// <summary>
    /// Returns an OWNING copy of frame <paramref name="index"/> (clone into fresh pooled
    /// memory), so the returned bitmap's lifetime is independent of this decoder. This mirrors
    /// WPF's frame-caching: the frame is extracted once and then kept alive by the frame
    /// handle, not by the decoder stream.
    /// </summary>
    /// <remarks>Progressive decoders are the exception: the single frame is a LIVE view over
    /// the decoder's backing so progressive updates reach already-materialized frames.</remarks>
    public ManagedWicBitmap GetFrame(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index < 0 || index >= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (IsProgressive)
        {
            _liveFrame ??= new ManagedWicBitmap(_image, WicPixelFormat.Bgra32, DpiX, DpiY, 0);
            _liveFrame.UpdateFrom(_image);
            return _liveFrame;
        }

        // Copy exactly the requested frame into a fresh single-frame image so the returned
        // bitmap owns its memory and outlives this decoder. (The compat ImageSharp build
        // exposes no ImageFrame<T>.Clone, so copy row-by-row through ProcessPixelRows.)
        Image<Bgra32> clone = new(_image.Configuration, _image.Width, _image.Height);
        ImageFrame<Bgra32> sourceFrame = _image.Frames[index];
        sourceFrame.ProcessPixelRows(clone.Frames[0], (sourceBuffer, destinationBuffer) =>
        {
            for (int y = 0; y < sourceBuffer.Height; y++)
            {
                sourceBuffer.GetRowSpan(y).CopyTo(destinationBuffer.GetRowSpan(y));
            }
        });

        return new ManagedWicBitmap(clone, WicPixelFormat.Bgra32, DpiX, DpiY, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _image.Dispose();
    }
}
