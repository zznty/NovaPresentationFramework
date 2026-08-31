using JetBrains.Annotations;

namespace Nova.Imaging;

/// <summary>
/// WPF-free pixel-layout contract between the patched WIC nest (PresentationCore) and the
/// ImageSharp-backed managed codec. The nest maps WPF <c>PixelFormat</c>/GUID values onto
/// these; <see cref="ManagedWicBitmap"/> stores <c>Bgra32</c> (straight) internally and
/// converts to the requested layout on copy.
/// </summary>
[PublicAPI]
public enum WicPixelFormat
{
    /// <summary>32-bit BGRA, straight (non-premultiplied) alpha. Canonical internal layout.</summary>
    Bgra32,

    /// <summary>32-bit BGRA, premultiplied alpha. WPF Pbgra32; matches the Vulkan
    /// <c>B8G8R8A8Unorm</c> premultiplied blend convention.</summary>
    Pbgra32,

    /// <summary>24-bit BGR, opaque.</summary>
    Bgr24,

    /// <summary>32-bit BGRX, opaque (alpha ignored).</summary>
    Bgr32,

    /// <summary>32-bit RGBA, straight alpha.</summary>
    Rgba32,

    /// <summary>32-bit RGBA, premultiplied alpha.</summary>
    Prgba32,

    /// <summary>8-bit grayscale.</summary>
    Gray8
}
