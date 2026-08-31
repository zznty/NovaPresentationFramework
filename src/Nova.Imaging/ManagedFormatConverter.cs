using JetBrains.Annotations;

namespace Nova.Imaging;

/// <summary>
/// A WIC <c>IWICFormatConverter</c> emulation. WPF keeps using the converter handle as an
/// <c>IWICBitmapSource</c> after <c>Initialize</c>, so this object holds the source bitmap and
/// the bitmap-source queries delegate to it. The actual pixel conversion is lazy: the storage
/// is always straight <c>Bgra32</c> and <c>CopyPixels</c> converts to the requested layout at
/// copy time.
/// </summary>
[PublicAPI]
public sealed class ManagedFormatConverter
{
    /// <summary>The bitmap the converter was initialized with, or <c>null</c> before init.</summary>
    public ManagedWicBitmap? Source { get; private set; }

    public void Initialize(ManagedWicBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }
}
