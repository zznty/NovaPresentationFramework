using JetBrains.Annotations;

namespace Nova.Imaging;

/// <summary>
/// Container formats the managed decoder can report, mirroring the WPF
/// <c>MILGuidData.GUID_ContainerFormat*</c> set so the patched nest can map back to a
/// concrete <c>BitmapDecoder</c> subclass.
/// </summary>
[PublicAPI]
public enum WicContainerFormat
{
    Unknown,
    Bmp,
    Gif,
    Ico,
    Jpeg,
    Png,
    Tiff,
    Wmp
}
